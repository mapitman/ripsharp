using System;
using System.Reflection;

using AwesomeAssertions;

using RipSharp.Utilities;

using Xunit;

namespace RipSharp.Tests.Utilities;

public class SpectreProgressDisplayTests
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(65, "00:01:05")]
    [InlineData(3661, "01:01:01")]
    [InlineData(36000, "10:00:00")]
    public void FormatTimeSpan_AlwaysUsesFixedWidth(int seconds, string expected)
    {
        var result = InvokeFormatTimeSpan(TimeSpan.FromSeconds(seconds));

        result.Should().Be(expected);
    }

    [Fact]
    public void TaskValueReset_ResetsElapsedTimer()
    {
        var task = CreateLiveTask("Test", 100);

        SetTaskValue(task, 1);
        SetPrivateField(task, "_startTime", DateTime.UtcNow - TimeSpan.FromSeconds(5));

        SetTaskValue(task, 0);

        GetElapsed(task).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void StopTask_FreezesElapsedTimer()
    {
        var task = CreateLiveTask("Test", 100);
        var startTime = DateTime.UtcNow - TimeSpan.FromSeconds(7);

        SetTaskValue(task, 1);
        SetPrivateField(task, "_startTime", startTime);

        Invoke(task, "StopTask");

        var stopTime = (DateTime?)GetPrivateField(task, "_stopTime");

        stopTime.Should().NotBeNull();
        GetElapsed(task).Should().BeCloseTo(stopTime!.Value - startTime, precision: TimeSpan.FromSeconds(0.25));
    }

    [Fact]
    public void StopTask_DoesNotOverwriteStopTime()
    {
        var task = CreateLiveTask("Test", 100);
        var startTime = DateTime.UtcNow - TimeSpan.FromSeconds(9);
        var firstStopTime = DateTime.UtcNow - TimeSpan.FromSeconds(4);

        SetTaskValue(task, 1);
        SetPrivateField(task, "_startTime", startTime);
        SetPrivateField(task, "_stopTime", firstStopTime);
        SetPrivateField(task, "_isStopped", true);

        Invoke(task, "StopTask");

        var stopTime = (DateTime?)GetPrivateField(task, "_stopTime");
        stopTime.Should().Be(firstStopTime);
        GetElapsed(task).Should().BeCloseTo(firstStopTime - startTime, precision: TimeSpan.FromSeconds(0.25));
    }

    private static string InvokeFormatTimeSpan(TimeSpan value)
    {
        var method = typeof(SpectreProgressDisplay)
            .GetMethod("FormatTimeSpan", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        return (string)method!.Invoke(null, new object[] { value })!;
    }

    private static object CreateLiveTask(string description, long maxValue)
    {
        var taskType = typeof(SpectreProgressDisplay)
            .GetNestedType("LiveTask", BindingFlags.NonPublic);

        taskType.Should().NotBeNull();

        return Activator.CreateInstance(
            taskType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { description, maxValue },
            culture: null) ?? throw new InvalidOperationException("Failed to create LiveTask instance.");
    }

    private static void SetTaskValue(object task, long value)
    {
        var property = task.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull();
        property!.SetValue(task, value);
    }

    private static TimeSpan GetElapsed(object task)
    {
        var method = task.GetType().GetMethod("GetElapsed", BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();

        return (TimeSpan)method!.Invoke(task, null)!;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        field.Should().NotBeNull();
        field!.SetValue(target, value);
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.Invoke(target, null);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        field.Should().NotBeNull();
        return field!.GetValue(target);
    }
}
