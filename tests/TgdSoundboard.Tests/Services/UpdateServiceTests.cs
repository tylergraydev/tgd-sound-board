using FluentAssertions;
using TgdSoundboard.Services;

namespace TgdSoundboard.Tests.Services;

public class UpdateServiceTests
{
    [Fact]
    public void GetCurrentVersion_ReturnsValidVersionFormat()
    {
        // Act
        var version = UpdateService.GetCurrentVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+$", "Version should be in format X.Y.Z");
    }

    [Fact]
    public void GetCurrentVersion_ReturnsConsistentValue()
    {
        // Act
        var version1 = UpdateService.GetCurrentVersion();
        var version2 = UpdateService.GetCurrentVersion();

        // Assert
        version1.Should().Be(version2);
    }

    [Fact]
    public void Initialize_DoesNotThrow()
    {
        // Act
        var act = () => UpdateService.Initialize();

        // Assert
        act.Should().NotThrow();
    }
}
