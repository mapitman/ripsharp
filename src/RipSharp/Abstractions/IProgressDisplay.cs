namespace RipSharp.Abstractions;

/// <summary>
/// Abstraction for displaying progress bars and animated progress indicators.
/// </summary>
public interface IProgressDisplay
{
    /// <summary>
    /// Starts a progress tracking context and executes the provided action.
    /// </summary>
    Task ExecuteAsync(Func<IProgressContext, Task> action);
}

/// <summary>
/// Context for managing multiple progress tasks.
/// </summary>
public interface IProgressContext
{
    /// <summary>
    /// Adds a new progress task with the given description and maximum value.
    /// </summary>
    IProgressTask AddTask(string description, long maxValue);
}

/// <summary>
/// Represents an individual progress task that can be updated.
/// </summary>
public interface IProgressTask
{
    /// <summary>
    /// Gets or sets the current progress value.
    /// </summary>
    long Value { get; set; }

    /// <summary>
    /// Gets or sets the task description (can include markup for styling).
    /// </summary>
    string Description { get; set; }

    /// <summary>
    /// Stops the task, marking it as complete.
    /// </summary>
    void StopTask();
}
