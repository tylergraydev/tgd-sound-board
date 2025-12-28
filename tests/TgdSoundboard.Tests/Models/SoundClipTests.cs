using FluentAssertions;
using TgdSoundboard.Models;

namespace TgdSoundboard.Tests.Models;

public class SoundClipTests
{
    [Fact]
    public void SoundClip_HasCorrectDefaultValues()
    {
        // Act
        var clip = new SoundClip();

        // Assert
        clip.Id.Should().Be(0);
        clip.Name.Should().BeEmpty();
        clip.FilePath.Should().BeEmpty();
        clip.CategoryId.Should().Be(0);
        clip.Hotkey.Should().BeEmpty();
        clip.Color.Should().Be("#2196F3");
        clip.Volume.Should().Be(1.0f);
        clip.SortOrder.Should().Be(0);
        clip.Duration.Should().Be(TimeSpan.Zero);
        clip.IsPlaying.Should().BeFalse();
        clip.IsLooping.Should().BeFalse();
        clip.IsFavorite.Should().BeFalse();
        clip.FadeInSeconds.Should().Be(0f);
        clip.FadeOutSeconds.Should().Be(0f);
        clip.PlaybackSpeed.Should().Be(1.0f);
        clip.PitchSemitones.Should().Be(0);
    }

    [Fact]
    public void SoundClip_RaisesPropertyChanged_WhenNameChanges()
    {
        // Arrange
        var clip = new SoundClip();
        var propertyChangedRaised = false;
        clip.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SoundClip.Name))
                propertyChangedRaised = true;
        };

        // Act
        clip.Name = "New Name";

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Fact]
    public void SoundClip_RaisesPropertyChanged_WhenVolumeChanges()
    {
        // Arrange
        var clip = new SoundClip();
        var propertyChangedRaised = false;
        clip.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SoundClip.Volume))
                propertyChangedRaised = true;
        };

        // Act
        clip.Volume = 0.5f;

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Fact]
    public void SoundClip_RaisesPropertyChanged_WhenIsPlayingChanges()
    {
        // Arrange
        var clip = new SoundClip();
        var propertyChangedRaised = false;
        clip.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SoundClip.IsPlaying))
                propertyChangedRaised = true;
        };

        // Act
        clip.IsPlaying = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Fact]
    public void SoundClip_AllowsNegativePitchSemitones()
    {
        // Arrange
        var clip = new SoundClip();

        // Act
        clip.PitchSemitones = -5;

        // Assert
        clip.PitchSemitones.Should().Be(-5);
    }

    [Fact]
    public void SoundClip_AllowsPositivePitchSemitones()
    {
        // Arrange
        var clip = new SoundClip();

        // Act
        clip.PitchSemitones = 7;

        // Assert
        clip.PitchSemitones.Should().Be(7);
    }

    [Fact]
    public void SoundClip_AllowsPlaybackSpeedVariation()
    {
        // Arrange
        var clip = new SoundClip();

        // Act & Assert
        clip.PlaybackSpeed = 0.5f;
        clip.PlaybackSpeed.Should().Be(0.5f);

        clip.PlaybackSpeed = 2.0f;
        clip.PlaybackSpeed.Should().Be(2.0f);
    }
}
