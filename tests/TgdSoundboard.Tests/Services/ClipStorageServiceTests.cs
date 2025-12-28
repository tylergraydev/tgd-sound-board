using System.IO;
using FluentAssertions;
using TgdSoundboard.Services;

namespace TgdSoundboard.Tests.Services;

public class ClipStorageServiceTests : IDisposable
{
    private readonly ClipStorageService _sut;
    private readonly string _testDirectory;
    private readonly string _testSourceFile;

    public ClipStorageServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"TgdSoundboard_ClipTest_{Guid.NewGuid()}");
        _sut = new ClipStorageService(_testDirectory);

        // Create a test source file
        _testSourceFile = Path.Combine(Path.GetTempPath(), "test_audio.wav");
        CreateTestWavFile(_testSourceFile);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
            if (File.Exists(_testSourceFile))
            {
                File.Delete(_testSourceFile);
            }
        }
        catch { }
    }

    private void CreateTestWavFile(string path)
    {
        // Create a minimal valid WAV file (44 bytes header + some data)
        using var fs = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // RIFF header
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + 1000); // File size - 8
        writer.Write("WAVE".ToCharArray());

        // fmt chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)2); // Channels
        writer.Write(44100); // Sample rate
        writer.Write(176400); // Byte rate
        writer.Write((short)4); // Block align
        writer.Write((short)16); // Bits per sample

        // data chunk
        writer.Write("data".ToCharArray());
        writer.Write(1000); // Data size
        writer.Write(new byte[1000]); // Audio data (silence)
    }

    [Fact]
    public void Constructor_CreatesClipsDirectory()
    {
        // Assert
        Directory.Exists(_testDirectory).Should().BeTrue();
    }

    [Fact]
    public void ClipsDirectory_ReturnsConfiguredPath()
    {
        // Assert
        _sut.ClipsDirectory.Should().Be(_testDirectory);
    }

    [Fact]
    public async Task ImportAudioFileAsync_CopiesFileToClipsDirectory()
    {
        // Arrange
        var clipName = "My Test Clip";

        // Act
        var destPath = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        destPath.Should().StartWith(_testDirectory);
        Path.GetFileName(destPath).Should().Be("My Test Clip.wav");
    }

    [Fact]
    public async Task ImportAudioFileAsync_SanitizesFileName()
    {
        // Arrange
        var clipName = "Invalid<>Name:With|Chars?";

        // Act
        var destPath = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        var fileName = Path.GetFileNameWithoutExtension(destPath);
        fileName.Should().NotContainAny("<", ">", ":", "|", "?");
    }

    [Fact]
    public async Task ImportAudioFileAsync_CreatesUniqueFileName_WhenFileExists()
    {
        // Arrange
        var clipName = "Duplicate";

        // Act
        var destPath1 = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);
        var destPath2 = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);
        var destPath3 = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);

        // Assert
        destPath1.Should().NotBe(destPath2);
        destPath2.Should().NotBe(destPath3);
        File.Exists(destPath1).Should().BeTrue();
        File.Exists(destPath2).Should().BeTrue();
        File.Exists(destPath3).Should().BeTrue();
    }

    [Fact]
    public void DeleteClipFile_DeletesFileInClipsDirectory()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "to_delete.wav");
        File.Copy(_testSourceFile, filePath);
        File.Exists(filePath).Should().BeTrue();

        // Act
        _sut.DeleteClipFile(filePath);

        // Assert
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void DeleteClipFile_DoesNotDeleteFilesOutsideClipsDirectory()
    {
        // Arrange - file is outside clips directory
        var externalFile = Path.Combine(Path.GetTempPath(), $"external_{Guid.NewGuid()}.wav");
        File.Copy(_testSourceFile, externalFile);

        try
        {
            // Act
            _sut.DeleteClipFile(externalFile);

            // Assert - file should still exist
            File.Exists(externalFile).Should().BeTrue();
        }
        finally
        {
            File.Delete(externalFile);
        }
    }

    [Fact]
    public void DeleteClipFile_HandlesNonExistentFile_WithoutThrowing()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "does_not_exist.wav");

        // Act & Assert
        var act = () => _sut.DeleteClipFile(nonExistentPath);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ImportAudioFileAsync_HandlesEmptyClipName()
    {
        // Arrange
        var clipName = "";

        // Act
        var destPath = await _sut.ImportAudioFileAsync(_testSourceFile, clipName);

        // Assert
        File.Exists(destPath).Should().BeTrue();
        Path.GetFileNameWithoutExtension(destPath).Should().Be("clip");
    }

    [Fact]
    public async Task ImportAudioFileAsync_PreservesFileExtension()
    {
        // Arrange
        var mp3File = Path.Combine(Path.GetTempPath(), "test.mp3");
        File.Copy(_testSourceFile, mp3File, true);

        try
        {
            // Act
            var destPath = await _sut.ImportAudioFileAsync(mp3File, "MyClip");

            // Assert
            Path.GetExtension(destPath).Should().Be(".mp3");
        }
        finally
        {
            File.Delete(mp3File);
        }
    }
}
