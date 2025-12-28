using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using TgdSoundboard.Models;

namespace TgdSoundboard.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "TgdSoundboard");
        Directory.CreateDirectory(appFolder);

        _dbPath = Path.Combine(appFolder, "soundboard.db");
        _connectionString = $"Data Source={_dbPath}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Color TEXT DEFAULT '#4CAF50',
                SortOrder INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS SoundClips (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                CategoryId INTEGER NOT NULL,
                Hotkey TEXT,
                Color TEXT DEFAULT '#2196F3',
                Volume REAL DEFAULT 1.0,
                SortOrder INTEGER DEFAULT 0,
                DurationTicks INTEGER DEFAULT 0,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );
        ");

        // Create default category if none exist
        var categoryCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Categories");
        if (categoryCount == 0)
        {
            connection.Execute("INSERT INTO Categories (Name, Color, SortOrder) VALUES ('General', '#4CAF50', 0)");
        }
    }

    public async Task<List<Category>> GetCategoriesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        var categories = (await connection.QueryAsync<Category>(
            "SELECT * FROM Categories ORDER BY SortOrder")).ToList();

        foreach (var category in categories)
        {
            category.Clips = new System.Collections.ObjectModel.ObservableCollection<SoundClip>(
                await GetClipsByCategoryAsync(category.Id));
        }

        return categories;
    }

    public async Task<List<SoundClip>> GetClipsByCategoryAsync(int categoryId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var clips = await connection.QueryAsync<SoundClipDto>(
            "SELECT * FROM SoundClips WHERE CategoryId = @CategoryId ORDER BY SortOrder",
            new { CategoryId = categoryId });

        return clips.Select(dto => new SoundClip
        {
            Id = dto.Id,
            Name = dto.Name,
            FilePath = dto.FilePath,
            CategoryId = dto.CategoryId,
            Hotkey = dto.Hotkey ?? string.Empty,
            Color = dto.Color ?? "#2196F3",
            Volume = dto.Volume,
            SortOrder = dto.SortOrder,
            Duration = TimeSpan.FromTicks(dto.DurationTicks),
            CreatedAt = DateTime.Parse(dto.CreatedAt)
        }).ToList();
    }

    public async Task<Category> AddCategoryAsync(string name, string color = "#4CAF50")
    {
        await using var connection = new SqliteConnection(_connectionString);
        var maxOrder = await connection.ExecuteScalarAsync<int?>(
            "SELECT MAX(SortOrder) FROM Categories") ?? -1;

        var id = await connection.ExecuteScalarAsync<int>(@"
            INSERT INTO Categories (Name, Color, SortOrder)
            VALUES (@Name, @Color, @SortOrder);
            SELECT last_insert_rowid();",
            new { Name = name, Color = color, SortOrder = maxOrder + 1 });

        return new Category { Id = id, Name = name, Color = color, SortOrder = maxOrder + 1 };
    }

    public async Task UpdateCategoryAsync(Category category)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            UPDATE Categories SET Name = @Name, Color = @Color, SortOrder = @SortOrder
            WHERE Id = @Id",
            category);
    }

    public async Task DeleteCategoryAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync("DELETE FROM Categories WHERE Id = @Id", new { Id = id });
    }

    public async Task<SoundClip> AddClipAsync(SoundClip clip)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var maxOrder = await connection.ExecuteScalarAsync<int?>(
            "SELECT MAX(SortOrder) FROM SoundClips WHERE CategoryId = @CategoryId",
            new { clip.CategoryId }) ?? -1;

        clip.SortOrder = maxOrder + 1;

        var id = await connection.ExecuteScalarAsync<int>(@"
            INSERT INTO SoundClips (Name, FilePath, CategoryId, Hotkey, Color, Volume, SortOrder, DurationTicks, CreatedAt)
            VALUES (@Name, @FilePath, @CategoryId, @Hotkey, @Color, @Volume, @SortOrder, @DurationTicks, @CreatedAt);
            SELECT last_insert_rowid();",
            new
            {
                clip.Name,
                clip.FilePath,
                clip.CategoryId,
                clip.Hotkey,
                clip.Color,
                clip.Volume,
                clip.SortOrder,
                DurationTicks = clip.Duration.Ticks,
                CreatedAt = clip.CreatedAt.ToString("O")
            });

        clip.Id = id;
        return clip;
    }

    public async Task UpdateClipAsync(SoundClip clip)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            UPDATE SoundClips SET
                Name = @Name, FilePath = @FilePath, CategoryId = @CategoryId,
                Hotkey = @Hotkey, Color = @Color, Volume = @Volume,
                SortOrder = @SortOrder, DurationTicks = @DurationTicks
            WHERE Id = @Id",
            new
            {
                clip.Id,
                clip.Name,
                clip.FilePath,
                clip.CategoryId,
                clip.Hotkey,
                clip.Color,
                clip.Volume,
                clip.SortOrder,
                DurationTicks = clip.Duration.Ticks
            });
    }

    public async Task DeleteClipAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync("DELETE FROM SoundClips WHERE Id = @Id", new { Id = id });
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT Value FROM Settings WHERE Key = @Key", new { Key = key });
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(@"
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@Key, @Value)",
            new { Key = key, Value = value });
    }

    public async Task<AppSettings> GetAppSettingsAsync()
    {
        var settings = new AppSettings();

        settings.OutputDeviceId = await GetSettingAsync("OutputDeviceId") ?? string.Empty;
        settings.VirtualCableDeviceId = await GetSettingAsync("VirtualCableDeviceId") ?? string.Empty;
        settings.InputDeviceId = await GetSettingAsync("InputDeviceId") ?? string.Empty;
        settings.LoopbackDeviceId = await GetSettingAsync("LoopbackDeviceId") ?? string.Empty;
        settings.MasterVolume = float.TryParse(await GetSettingAsync("MasterVolume"), out var vol) ? vol : 1.0f;
        settings.PassSystemAudio = bool.TryParse(await GetSettingAsync("PassSystemAudio"), out var pass) && pass;
        settings.PassMicrophone = bool.TryParse(await GetSettingAsync("PassMicrophone"), out var mic) && mic;
        settings.GridColumns = int.TryParse(await GetSettingAsync("GridColumns"), out var cols) ? cols : 6;
        settings.ClipsDirectory = await GetSettingAsync("ClipsDirectory") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TgdSoundboard", "Clips");

        Directory.CreateDirectory(settings.ClipsDirectory);

        return settings;
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        await SetSettingAsync("OutputDeviceId", settings.OutputDeviceId);
        await SetSettingAsync("VirtualCableDeviceId", settings.VirtualCableDeviceId);
        await SetSettingAsync("InputDeviceId", settings.InputDeviceId);
        await SetSettingAsync("LoopbackDeviceId", settings.LoopbackDeviceId);
        await SetSettingAsync("MasterVolume", settings.MasterVolume.ToString());
        await SetSettingAsync("PassSystemAudio", settings.PassSystemAudio.ToString());
        await SetSettingAsync("PassMicrophone", settings.PassMicrophone.ToString());
        await SetSettingAsync("GridColumns", settings.GridColumns.ToString());
        await SetSettingAsync("ClipsDirectory", settings.ClipsDirectory);
    }

    private class SoundClipDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string? Hotkey { get; set; }
        public string? Color { get; set; }
        public float Volume { get; set; }
        public int SortOrder { get; set; }
        public long DurationTicks { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }
}
