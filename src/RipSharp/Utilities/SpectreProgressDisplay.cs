using Spectre.Console;
using Spectre.Console.Rendering;

namespace BugZapperLabs.RipSharp.Utilities;

/// <summary>
/// Spectre.Console implementation of progress display with rich terminal animations.
/// </summary>
public class SpectreProgressDisplay : IProgressDisplay
{
    private readonly IThemeProvider _theme;

    public SpectreProgressDisplay(IThemeProvider theme)
    {
        _theme = theme;
    }

    public async Task ExecuteAsync(Func<IProgressContext, Task> action)
    {
        var liveContext = new LiveProgressContext();

        await AnsiConsole.Live(Render(liveContext))
            .AutoClear(true)
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

            });
    }

    private IRenderable Render(LiveProgressContext ctx)
    {
        var (ripTask, encodeTasks, overallTask) = ctx.GetSnapshot();

        var grid = new Grid();
        grid.AddColumn(new GridColumn() { NoWrap = true, Width = null });

        grid.AddRow(new Panel(RenderTask(ripTask, "Ripping"))
        {
            Header = new PanelHeader("Ripping", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = _theme.SuccessColor,
            Expand = true
        });

        var activeEncodeTasks = encodeTasks.Where(t => !t.IsStopped || t.Value > 0).ToList();
        if (activeEncodeTasks.Count <= 1)
        {
            var task = activeEncodeTasks.FirstOrDefault() ?? encodeTasks.FirstOrDefault();
            grid.AddRow(new Panel(RenderTask(task, "Encoding"))
            {
                Header = new PanelHeader("Encoding", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = _theme.HighlightColor,
                Expand = true
            });
        }
        else
        {
            for (int i = 0; i < activeEncodeTasks.Count; i++)
            {
                grid.AddRow(new Panel(RenderTask(activeEncodeTasks[i], "Encoding"))
                {
                    Header = new PanelHeader($"Encoding [[{i + 1}]]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = _theme.HighlightColor,
                    Expand = true
                });
            }
        }

        grid.AddRow(new Panel(RenderOverallTask(overallTask))
        {
            Header = new PanelHeader("[bold]Overall Progress[/]", Justify.Left),
            Border = BoxBorder.Heavy,
            BorderStyle = _theme.AccentColor,
            Expand = true
        });

        return grid;
    }

    private IRenderable RenderOverallTask(LiveTask? task)
    {
        if (task == null)
        {
            return new Text("Waiting to start...", new Style(_theme.MutedColor));
        }

        var percent = task.MaxValue > 0 ? Math.Clamp((double)task.Value / task.MaxValue, 0, 1) : 0;
        const int barWidth = 80;
        var filled = (int)Math.Round(percent * barWidth);
        var empty = Math.Max(0, barWidth - filled);
        var filledBar = new string('█', filled);
        var emptyBar = new string('░', empty);
        var pctText = (percent * 100).ToString("0.0").PadLeft(6);

        var elapsed = task.GetElapsed();
        var elapsedStr = elapsed.TotalSeconds > 0 ? FormatTimeSpan(elapsed) : "00:00:00";

        var stepsCompleted = task.Value;
        var stepsTotal = task.MaxValue;
        var timeInfo = $"{stepsCompleted}/{stepsTotal} steps    {elapsedStr} elapsed";

        var barRenderable = new Markup($"[green]{filledBar}[/][{_theme.Colors.Muted}]{emptyBar}[/]");

        var progressBar = new Columns(new IRenderable[]
        {
            barRenderable,
            new Text($"{pctText}%", _theme.AccentColor),
            new Text($"  {timeInfo}", _theme.MutedColor)
        });

        return progressBar;
    }

    private IRenderable RenderTask(LiveTask? task, string label)
    {
        if (task == null)
        {
            return new Text($"Waiting for {label.ToLower()} to start...", new Style(_theme.MutedColor));
        }

        var percent = task.MaxValue > 0 ? Math.Clamp((double)task.Value / task.MaxValue, 0, 1) : 0;
        const int barWidth = 80;
        var filled = (int)Math.Round(percent * barWidth);
        var empty = Math.Max(0, barWidth - filled);
        var filledBar = new string('█', filled);
        var emptyBar = new string('░', empty);
        var pctText = (percent * 100).ToString("0.0").PadLeft(6);

        var elapsed = task.GetElapsed();
        var elapsedStr = elapsed.TotalSeconds > 0 ? FormatTimeSpan(elapsed) : "00:00:00";
        var remainingStr = "--:--:--";

        if (task.IsFailed)
        {
            remainingStr = "--:--:--";
        }
        else if (percent > 0.02 && !task.IsStopped)
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

        var barColor = task.IsFailed ? _theme.Colors.Error : _theme.Colors.Success;
        var barRenderable = new Markup($"[{barColor}]{filledBar}[/][{_theme.Colors.Muted}]{emptyBar}[/]");

        var progressBar = new Columns(new IRenderable[]
        {
            barRenderable,
            new Text($"{pctText}%", _theme.AccentColor),
            new Text($"  {timeInfo}", _theme.MutedColor)
        });

        var messages = task.GetRecentMessages(5);
        if (messages.Count == 0)
        {
            return progressBar;
        }

        var msgStyle = task.IsFailed ? new Style(_theme.ErrorColor) : new Style(_theme.MutedColor);
        var rows = new List<IRenderable> { progressBar };
        foreach (var msg in messages)
        {
            rows.Add(new Text(msg, msgStyle));
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
        private readonly Lock _lock = new();

        public IProgressTask AddTask(string description, long maxValue)
        {
            var task = new LiveTask(description, maxValue);
            lock (_lock)
            {
                _tasks.Add(task);
            }
            return task;
        }

        public (LiveTask? RipTask, List<LiveTask> EncodeTasks, LiveTask? OverallTask) GetSnapshot()
        {
            lock (_lock)
            {
                LiveTask? ripTask = null;
                var encodeTasks = new List<LiveTask>();
                LiveTask? overallTask = null;

                foreach (var task in _tasks)
                {
                    if (task.Description.StartsWith("Ripping") || task.Description == "Ripping")
                        ripTask = task;
                    else if (task.Description.StartsWith("Overall") || task.Description == "Overall")
                        overallTask = task;
                    else
                        encodeTasks.Add(task);
                }

                return (ripTask, encodeTasks, overallTask);
            }
        }
    }

    private class LiveTask : IProgressTask
    {
        private readonly Lock _lock = new();
        private long _value;
        private readonly long _maxValue;
        private string _description;
        private readonly List<string> _messages = new();
        private bool _isStopped;
        private bool _isFailed;
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

        public bool IsFailed
        {
            get { lock (_lock) { return _isFailed; } }
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
                        _isFailed = false;
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

        public void StartTracking()
        {
            lock (_lock)
            {
                _startTime ??= DateTime.UtcNow;
            }
        }

        public void StopTask()
        {
            lock (_lock)
            {
                if (_isStopped)
                {
                    return;
                }
                _value = _maxValue;
                _isStopped = true;
                _stopTime = DateTime.UtcNow;
            }
        }

        public void FailTask()
        {
            lock (_lock)
            {
                if (_isFailed) return;
                _isFailed = true;
                _isStopped = true;
                _stopTime ??= DateTime.UtcNow;
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
