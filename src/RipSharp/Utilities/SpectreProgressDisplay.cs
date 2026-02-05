using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RipSharp.Utilities;

/// <summary>
/// Spectre.Console implementation of progress display with rich terminal animations.
/// </summary>
public class SpectreProgressDisplay : IProgressDisplay
{
    public async Task ExecuteAsync(Func<IProgressContext, Task> action)
    {
        var liveContext = new LiveProgressContext();

        await AnsiConsole.Live(Render(liveContext))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async live =>
            {
                using var refreshCts = new CancellationTokenSource();
                var refreshTask = Task.Run(async () =>
                {
                    while (!refreshCts.IsCancellationRequested)
                    {
                        live.UpdateTarget(Render(liveContext));
                        await Task.Delay(150, refreshCts.Token).ConfigureAwait(false);
                    }
                }, refreshCts.Token);

                await action(liveContext);

                refreshCts.Cancel();
                try
                {
                    await refreshTask;
                }
                catch (TaskCanceledException)
                {
                    // Expected when the refresh loop is cancelled; safely ignore.
                }
                catch (Exception ex)
                {
                    // Log unexpected exceptions from the refresh loop.
                    AnsiConsole.WriteException(ex);
                }

                // Final render after completion
                live.UpdateTarget(Render(liveContext));
            });
    }

    private static IRenderable Render(LiveProgressContext ctx)
    {
        var (ripTask, encodeTask, overallTask) = ctx.GetLatest();

        var ripPanel = new Panel(RenderTask(ripTask, "Ripping"))
        {
            Header = new PanelHeader("Ripping", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = CustomColors.Success,
            Expand = true
        };

        var encodePanel = new Panel(RenderTask(encodeTask, "Encoding"))
        {
            Header = new PanelHeader("Encoding", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = CustomColors.Highlight,
            Expand = true
        };

        var overallPanel = new Panel(RenderTask(overallTask, "Overall"))
        {
            Header = new PanelHeader("Overall Progress", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = CustomColors.Accent,
            Expand = true
        };

        var grid = new Grid();
        grid.AddColumn(new GridColumn() { NoWrap = true, Width = null });
        grid.AddRow(ripPanel);
        grid.AddRow(encodePanel);
        grid.AddRow(overallPanel);

        return grid;
    }

    private static IRenderable RenderTask(LiveTask? task, string label)
    {
        if (task == null)
        {
            return new Text($"Waiting for {label.ToLower()} to start...", new Style(CustomColors.Muted));
        }

        var percent = task.MaxValue > 0 ? Math.Clamp((double)task.Value / task.MaxValue, 0, 1) : 0;
        const int barWidth = 80;
        var filled = (int)Math.Round(percent * barWidth);
        var empty = Math.Max(0, barWidth - filled);
        var filledBar = new string('█', filled);
        var emptyBar = new string('░', empty);
        var pctText = (percent * 100).ToString("0.0").PadLeft(6);

        // Calculate elapsed and remaining time
        var elapsed = task.GetElapsed();
        var elapsedStr = elapsed.TotalSeconds > 0 ? FormatTimeSpan(elapsed) : "00:00:00";
        var remainingStr = "--:--:--";

        if (percent > 0.05 && !task.IsStopped) // Only estimate if we have meaningful progress (>5%)
        {
            var totalEstimated = elapsed.TotalSeconds / percent;
            var remaining = TimeSpan.FromSeconds(totalEstimated - elapsed.TotalSeconds);
            remainingStr = FormatTimeSpan(remaining);
        }
        else if (task.IsStopped)
        {
            remainingStr = "00:00:00";
        }

        var timeInfo = $"{elapsedStr} / {remainingStr}";

        // Combine filled and empty bars with different colors
        var barRenderable = new Markup($"[{ConsoleColors.Success}]{filledBar}[/][{ConsoleColors.Muted}]{emptyBar}[/]");

        var progressBar = new Columns(new IRenderable[]
        {
            barRenderable,
            new Text($"{pctText}%", CustomColors.Accent),
            new Text($"  {timeInfo}", CustomColors.Muted)
        });

        var messages = task.GetRecentMessages(5);
        if (messages.Count == 0)
        {
            return progressBar;
        }

        var rows = new List<IRenderable> { progressBar };
        foreach (var msg in messages)
        {
            rows.Add(new Text(msg, new Style(CustomColors.Muted)));
        }

        return new Rows(rows);
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private class LiveProgressContext : IProgressContext
    {
        private readonly List<LiveTask> _tasks = new();
        private readonly object _lock = new();

        public IProgressTask AddTask(string description, long maxValue)
        {
            var task = new LiveTask(description, maxValue);
            lock (_lock)
            {
                _tasks.Add(task);
            }
            return task;
        }

        public (LiveTask? ripTask, LiveTask? encodeTask, LiveTask? overallTask) GetLatest()
        {
            lock (_lock)
            {
                // Assume first task is ripping, second is encoding, third is overall
                var ripTask = _tasks.Count > 0 ? _tasks[0] : null;
                var encodeTask = _tasks.Count > 1 ? _tasks[1] : null;
                var overallTask = _tasks.Count > 2 ? _tasks[2] : null;
                return (ripTask, encodeTask, overallTask);
            }
        }
    }

    private class LiveTask : IProgressTask
    {
        private readonly object _lock = new();
        private long _value;
        private readonly long _maxValue;
        private string _description;
        private readonly List<string> _messages = new();
        private bool _isStopped;
        private DateTime? _startTime;
        private DateTime? _stopTime;

        public LiveTask(string description, long maxValue)
        {
            _description = description;
            _maxValue = maxValue;
            _startTime = null;
        }

        public bool IsStopped
        {
            get { lock (_lock) { return _isStopped; } }
        }

        public TimeSpan GetElapsed()
        {
            lock (_lock)
            {
                if (_startTime == null)
                {
                    return TimeSpan.Zero;
                }
                var endTime = _stopTime ?? DateTime.UtcNow;
                return endTime - _startTime.Value;
            }
        }

        public long Value
        {
            get { lock (_lock) { return _value; } }
            set
            {
                lock (_lock)
                {
                    if (value <= 0)
                    {
                        _value = 0;
                        _startTime = null;
                        _stopTime = null;
                        _isStopped = false;
                        return;
                    }
                    // Start tracking time when task first gets progress
                    if (_startTime == null && value > 0)
                    {
                        _startTime = DateTime.UtcNow;
                        _stopTime = null;
                    }
                    _value = Math.Min(value, _maxValue);
                }
            }
        }

        public long MaxValue => _maxValue;

        public void Increment(long value)
        {
            lock (_lock)
            {
                // Start tracking time when task first gets progress
                if (_startTime == null && (_value + value) > 0)
                {
                    _startTime = DateTime.UtcNow;
                }
                _value = Math.Min(_value + value, _maxValue);
            }
        }

        public string Description
        {
            get { lock (_lock) { return _description; } }
            set { lock (_lock) { _description = value; } }
        }

        public void StopTask()
        {
            lock (_lock)
            {
                _value = _maxValue;
                _isStopped = true;
                _stopTime = DateTime.UtcNow;
            }
        }

        public void AddMessage(string message)
        {
            lock (_lock)
            {
                _messages.Add(message);
            }
        }

        public void ClearMessages()
        {
            lock (_lock)
            {
                _messages.Clear();
            }
        }

        public List<string> GetRecentMessages(int count)
        {
            lock (_lock)
            {
                return _messages.Skip(Math.Max(0, _messages.Count - count)).ToList();
            }
        }
    }
}
