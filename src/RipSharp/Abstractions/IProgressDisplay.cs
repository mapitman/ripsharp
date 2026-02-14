namespace BugZapperLabs.RipSharp.Abstractions;

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
    /// Gets the maximum progress value.
    /// </summary>
    long MaxValue { get; }

    /// <summary>
    /// Gets whether the task has been stopped.
    /// </summary>
    bool IsStopped { get; }

    /// <summary>
    /// Gets the elapsed time since the task started.
    /// </summary>
    TimeSpan GetElapsed();

    /// <summary>
    /// Increment the current value by the specified amount.
    /// </summary>
    void Increment(long value);

    /// <summary>
    /// Gets or sets the task description (can include markup for styling).
    /// </summary>
    string Description { get; set; }

    /// <summary>
    /// Stops the task, marking it as complete.
    /// </summary>
    void StopTask();

    /// <summary>
    /// Adds a message to be displayed in this task's panel.
    /// </summary>
    void AddMessage(string message);

    /// <summary>
    /// Clears all messages from this task's panel.
    /// </summary>
    void ClearMessages();

    /// <summary>
    /// Gets the most recent messages, limited to the specified count.
    /// </summary>
    List<string> GetRecentMessages(int count);
}
