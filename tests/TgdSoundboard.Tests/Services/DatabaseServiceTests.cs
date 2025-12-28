using System.IO;
using FluentAssertions;
using TgdSoundboard.Models;
using TgdSoundboard.Services;

namespace TgdSoundboard.Tests.Services;

public class DatabaseServiceTests : IDisposable
{
    private readonly DatabaseService _sut;
    private readonly string _testDbPath;

    public DatabaseServiceTests()
    {
        // Use a unique test database for each test run
        _testDbPath = Path.Combine(Path.GetTempPath(), $"TgdSoundboard_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDbPath);

        // Set environment to use test path
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _testDbPath);
        _sut = new DatabaseService();
    }

    public void Dispose()
    {
        // Cleanup test database
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task GetCategoriesAsync_ReturnsDefaultCategory_WhenDatabaseIsNew()
    {
        // Act
        var categories = await _sut.GetCategoriesAsync();

        // Assert
        categories.Should().NotBeEmpty();
        categories.Should().ContainSingle(c => c.Name == "General");
    }

    [Fact]
    public async Task AddCategoryAsync_CreatesNewCategory()
    {
        // Arrange
        var categoryName = "Test Category";
        var color = "#FF0000";

        // Act
        var category = await _sut.AddCategoryAsync(categoryName, color);

        // Assert
        category.Should().NotBeNull();
        category.Name.Should().Be(categoryName);
        category.Color.Should().Be(color);
        category.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateCategoryAsync_UpdatesExistingCategory()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("Original Name", "#FF0000");
        category.Name = "Updated Name";
        category.Color = "#00FF00";

        // Act
        await _sut.UpdateCategoryAsync(category);
        var categories = await _sut.GetCategoriesAsync();

        // Assert
        var updated = categories.FirstOrDefault(c => c.Id == category.Id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
        updated.Color.Should().Be("#00FF00");
    }

    [Fact]
    public async Task DeleteCategoryAsync_RemovesCategory()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("To Delete", "#FF0000");

        // Act
        await _sut.DeleteCategoryAsync(category.Id);
        var categories = await _sut.GetCategoriesAsync();

        // Assert
        categories.Should().NotContain(c => c.Id == category.Id);
    }

    [Fact]
    public async Task AddClipAsync_CreatesNewClip()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("Clips Category");
        var clip = new SoundClip
        {
            Name = "Test Clip",
            FilePath = @"C:\test\audio.mp3",
            CategoryId = category.Id,
            Volume = 0.8f,
            Color = "#2196F3"
        };

        // Act
        var savedClip = await _sut.AddClipAsync(clip);

        // Assert
        savedClip.Id.Should().BeGreaterThan(0);
        savedClip.Name.Should().Be("Test Clip");
        savedClip.CategoryId.Should().Be(category.Id);
    }

    [Fact]
    public async Task UpdateClipAsync_UpdatesExistingClip()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("Test Category");
        var clip = new SoundClip
        {
            Name = "Original",
            FilePath = @"C:\test\audio.mp3",
            CategoryId = category.Id
        };
        var savedClip = await _sut.AddClipAsync(clip);

        savedClip.Name = "Updated";
        savedClip.Volume = 0.5f;
        savedClip.IsLooping = true;

        // Act
        await _sut.UpdateClipAsync(savedClip);
        var categories = await _sut.GetCategoriesAsync();
        var updatedClip = categories
            .SelectMany(c => c.Clips)
            .FirstOrDefault(c => c.Id == savedClip.Id);

        // Assert
        updatedClip.Should().NotBeNull();
        updatedClip!.Name.Should().Be("Updated");
        updatedClip.Volume.Should().Be(0.5f);
        updatedClip.IsLooping.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteClipAsync_RemovesClip()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("Test Category");
        var clip = new SoundClip
        {
            Name = "To Delete",
            FilePath = @"C:\test\audio.mp3",
            CategoryId = category.Id
        };
        var savedClip = await _sut.AddClipAsync(clip);

        // Act
        await _sut.DeleteClipAsync(savedClip.Id);
        var categories = await _sut.GetCategoriesAsync();

        // Assert
        categories.SelectMany(c => c.Clips).Should().NotContain(c => c.Id == savedClip.Id);
    }

    [Fact]
    public async Task GetAppSettingsAsync_ReturnsSettings()
    {
        // Act
        var settings = await _sut.GetAppSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        // Note: Volume and columns may be default or previously saved values
        settings.MasterVolume.Should().BeInRange(0f, 1f);
        settings.GridColumns.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SaveAppSettingsAsync_PersistsSettings()
    {
        // Arrange
        var settings = await _sut.GetAppSettingsAsync();
        settings.MasterVolume = 0.75f;
        settings.OutputDeviceId = "test-device-id";
        settings.Theme = "Purple";

        // Act
        await _sut.SaveAppSettingsAsync(settings);
        var loaded = await _sut.GetAppSettingsAsync();

        // Assert
        loaded.MasterVolume.Should().Be(0.75f);
        loaded.OutputDeviceId.Should().Be("test-device-id");
        loaded.Theme.Should().Be("Purple");
    }

    [Fact]
    public async Task GetSettingAsync_ReturnsNull_WhenKeyDoesNotExist()
    {
        // Act
        var value = await _sut.GetSettingAsync("NonExistentKey");

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task SetSettingAsync_CreatesAndUpdatesSetting()
    {
        // Act
        await _sut.SetSettingAsync("TestKey", "TestValue");
        var value1 = await _sut.GetSettingAsync("TestKey");

        await _sut.SetSettingAsync("TestKey", "UpdatedValue");
        var value2 = await _sut.GetSettingAsync("TestKey");

        // Assert
        value1.Should().Be("TestValue");
        value2.Should().Be("UpdatedValue");
    }

    [Fact]
    public async Task ClipSortOrder_IsAssignedSequentially()
    {
        // Arrange
        var category = await _sut.AddCategoryAsync("Sort Test");

        // Act
        var clip1 = await _sut.AddClipAsync(new SoundClip
        {
            Name = "Clip 1",
            FilePath = @"C:\test\1.mp3",
            CategoryId = category.Id
        });
        var clip2 = await _sut.AddClipAsync(new SoundClip
        {
            Name = "Clip 2",
            FilePath = @"C:\test\2.mp3",
            CategoryId = category.Id
        });
        var clip3 = await _sut.AddClipAsync(new SoundClip
        {
            Name = "Clip 3",
            FilePath = @"C:\test\3.mp3",
            CategoryId = category.Id
        });

        // Assert
        clip1.SortOrder.Should().Be(0);
        clip2.SortOrder.Should().Be(1);
        clip3.SortOrder.Should().Be(2);
    }
}
