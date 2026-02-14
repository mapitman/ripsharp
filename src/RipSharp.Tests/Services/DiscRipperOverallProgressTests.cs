using System.Reflection;

namespace RipSharp.Tests.Services;

public class DiscRipperOverallProgressTests
{
    [Fact]
    public void OverallProgressTracker_UpdatesValueForRipAndEncode()
    {
        var task = new TestProgressTask(maxValue: 6);
        var tracker = CreateTracker(task);

        Invoke(tracker, "MarkRipComplete");
        Invoke(tracker, "MarkEncodeComplete");

        task.Value.Should().Be(2);
    }

    [Fact]
    public void OverallProgressTracker_MarkAllComplete_SetsMaxValue()
    {
        var task = new TestProgressTask(maxValue: 4);
        var tracker = CreateTracker(task);

        Invoke(tracker, "MarkRipComplete");
        Invoke(tracker, "MarkAllComplete");

        task.Value.Should().Be(task.MaxValue);
    }

    private static object CreateTracker(IProgressTask task)
    {
        var trackerType = typeof(DiscRipper)
            .GetNestedType("OverallProgressTracker", BindingFlags.NonPublic);

        trackerType.Should().NotBeNull();

        return Activator.CreateInstance(
            trackerType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { task },
            culture: null) ?? throw new InvalidOperationException("Failed to create tracker instance.");
    }

    private static void Invoke(object tracker, string methodName)
    {
        var method = tracker.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.Invoke(tracker, null);
    }

    private sealed class TestProgressTask : IProgressTask
    {
        private long _value;

        public TestProgressTask(long maxValue)
        {
            MaxValue = maxValue;
        }

        public long Value
        {
            get => _value;
            set => _value = value;
        }

        public long MaxValue { get; }

        public bool IsStopped => false;

        public TimeSpan GetElapsed() => TimeSpan.Zero;

        public void Increment(long value) => _value += value;

        public string Description { get; set; } = string.Empty;

        public void StopTask()
        {
        }

        public void AddMessage(string message)
        {
        }

        public void ClearMessages()
        {
        }

        public List<string> GetRecentMessages(int count) => new();
    }
}
