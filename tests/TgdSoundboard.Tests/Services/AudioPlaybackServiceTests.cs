using System.IO;
using FluentAssertions;
using TgdSoundboard.Services;

namespace TgdSoundboard.Tests.Services;

public class AudioPlaybackServiceTests : IDisposable
{
    private readonly string _testWavFile;

    public AudioPlaybackServiceTests()
    {
        _testWavFile = Path.Combine(Path.GetTempPath(), $"test_audio_{Guid.NewGuid()}.wav");
        CreateTestWavFile(_testWavFile, durationSeconds: 2.5);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testWavFile))
            {
                File.Delete(_testWavFile);
            }
        }
        catch { }
    }

    private void CreateTestWavFile(string path, double durationSeconds)
    {
        var sampleRate = 44100;
        var channels = 2;
        var bitsPerSample = 16;
        var numSamples = (int)(sampleRate * durationSeconds);
        var dataSize = numSamples * channels * (bitsPerSample / 8);

        using var fs = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        // RIFF header
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());

        // fmt chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8)); // Byte rate
        writer.Write((short)(channels * (bitsPerSample / 8))); // Block align
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write("data".ToCharArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    [Fact]
    public void GetAudioDuration_ReturnsCorrectDuration_ForValidFile()
    {
        // Act
        var duration = AudioPlaybackService.GetAudioDuration(_testWavFile);

        // Assert
        duration.TotalSeconds.Should().BeApproximately(2.5, 0.1);
    }

    [Fact]
    public void GetAudioDuration_ReturnsZero_ForInvalidFile()
    {
        // Arrange
        var invalidPath = @"C:\nonexistent\file.mp3";

        // Act
        var duration = AudioPlaybackService.GetAudioDuration(invalidPath);

        // Assert
        duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetAudioDuration_ReturnsZero_ForCorruptedFile()
    {
        // Arrange
        var corruptFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid()}.wav");
        File.WriteAllText(corruptFile, "not a valid wav file");

        try
        {
            // Act
            var duration = AudioPlaybackService.GetAudioDuration(corruptFile);

            // Assert
            duration.Should().Be(TimeSpan.Zero);
        }
        finally
        {
            File.Delete(corruptFile);
        }
    }

    [Fact]
    public void GetOutputDevices_ReturnsListWithoutThrowing()
    {
        // Act
        var act = () => AudioPlaybackService.GetOutputDevices();

        // Assert
        act.Should().NotThrow();
        var devices = act();
        devices.Should().NotBeNull();
    }

    [Fact]
    public void GetInputDevices_ReturnsListWithoutThrowing()
    {
        // Act
        var act = () => AudioPlaybackService.GetInputDevices();

        // Assert
        act.Should().NotThrow();
        var devices = act();
        devices.Should().NotBeNull();
    }

    [Fact]
    public void GetOutputDevices_MarksVirtualCablesCorrectly()
    {
        // Act
        var devices = AudioPlaybackService.GetOutputDevices();

        // Assert - verify that devices with "CABLE" or "Virtual" in name are marked as virtual
        foreach (var device in devices)
        {
            if (device.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                device.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                device.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            {
                device.IsVirtualCable.Should().BeTrue($"Device '{device.Name}' should be marked as virtual cable");
            }
        }
    }

    [Fact]
    public void GetOutputDevices_HasAtMostOneDefault()
    {
        // Act
        var devices = AudioPlaybackService.GetOutputDevices();

        // Assert
        devices.Count(d => d.IsDefault).Should().BeLessThanOrEqualTo(1);
    }
}
