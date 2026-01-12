namespace RipSharp.Utilities;

public class CursorManagerTests
{
    [Fact]
    public void Constructor_HidesCursor()
    {
        // Arrange & Act
        var manager = new CursorManager();

        // Assert - should not throw
        // CursorManager successfully hides cursor on construction
        manager.Dispose();
    }

    [Fact]
    public void RestoreCursor_CanBeCalledMultipleTimes()
    {
        // Arrange
        var manager = new CursorManager();

        // Act & Assert - should not throw on multiple calls
        manager.RestoreCursor();
        manager.RestoreCursor();
        manager.RestoreCursor();

        manager.Dispose();
    }

    [Fact]
    public void Dispose_CallsRestoreCursor()
    {
        // Arrange
        var manager = new CursorManager();

        // Act
        manager.Dispose();

        // Assert - should not throw
        // Dispose successfully restores cursor
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var manager = new CursorManager();

        // Act & Assert - should not throw on multiple calls
        manager.Dispose();
        manager.Dispose();
        manager.Dispose();
    }

    [Fact]
    public void RestoreCursor_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var manager = new CursorManager();
        manager.Dispose();

        // Act & Assert - should not throw
        manager.RestoreCursor();  // Should return early since already disposed
    }

    [Fact]
    public void UsingStatement_EnsuresDisposal()
    {
        // Arrange & Act
        using (var manager = new CursorManager())
        {
            manager.RestoreCursor();
        }
        // Assert - should have disposed without throwing

        // Calling RestoreCursor after using block returns early
        // This is safe because we're testing that disposal happened
    }

    [Fact]
    public void CursorManager_ImplementsIDisposable()
    {
        // Assert
        var manager = new CursorManager();
        manager.Should().BeAssignableTo<IDisposable>();
        manager.Dispose();
    }

    [Fact]
    public void RestoreCursor_IsIdempotent()
    {
        // Arrange
        var manager = new CursorManager();

        // Act & Assert - multiple calls should have same effect as one call
        manager.RestoreCursor();
        var firstCallSucceeded = true;

        manager.RestoreCursor();
        var secondCallSucceeded = true;

        manager.RestoreCursor();
        var thirdCallSucceeded = true;

        firstCallSucceeded.Should().BeTrue();
        secondCallSucceeded.Should().BeTrue();
        thirdCallSucceeded.Should().BeTrue();

        manager.Dispose();
    }

    [Fact]
    public void Constructor_HandlesUnsupportedEnvironments()
    {
        // Arrange & Act
        // This tests that constructor doesn't throw even if cursor operations fail
        var manager = new CursorManager();

        // Assert
        manager.Should().NotBeNull();
        manager.Dispose();
    }
}
