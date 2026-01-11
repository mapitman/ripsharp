using Spectre.Console;

namespace RipSharp.Utilities;

/// <summary>
/// Spectre.Console implementation of progress display with rich terminal animations.
/// </summary>
public class SpectreProgressDisplay : IProgressDisplay
{
    public async Task ExecuteAsync(Func<IProgressContext, Task> action)
    {
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ElapsedTimeColumn { Style = CustomColors.Highlight },
                new ProgressBarColumn
                {
                    CompletedStyle = CustomColors.Success,
                    RemainingStyle = CustomColors.Muted
                },
                new PercentageColumn { Style = CustomColors.Info },
                new RemainingTimeColumn { Style = CustomColors.Accent },
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var context = new SpectreProgressContext(ctx);
                await action(context);
            });
    }

    private class SpectreProgressContext : IProgressContext
    {
        private readonly ProgressContext _context;

        public SpectreProgressContext(ProgressContext context)
        {
            _context = context;
        }

        public IProgressTask AddTask(string description, long maxValue)
        {
            var task = _context.AddTask(description, maxValue: maxValue);
            return new SpectreProgressTask(task);
        }
    }

    private class SpectreProgressTask : IProgressTask
    {
        private readonly Spectre.Console.ProgressTask _task;

        public SpectreProgressTask(Spectre.Console.ProgressTask task)
        {
            _task = task;
        }

        public long Value
        {
            get => (long)_task.Value;
            set => _task.Value = value;
        }

        public string Description
        {
            get => _task.Description;
            set => _task.Description = value;
        }

        public void StopTask() => _task.StopTask();
    }
}
