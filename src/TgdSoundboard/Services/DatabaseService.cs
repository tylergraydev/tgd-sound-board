using System.IO;
using System.Text.Json;
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

        // Migration: Add new columns if they don't exist
        MigrateDatabase(connection);

        // Create default category if none exist
        var categoryCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Categories");
        if (categoryCount == 0)
        {
            connection.Execute("INSERT INTO Categories (Name, Color, SortOrder) VALUES ('General', '#4CAF50', 0)");
        }
    }

    private void MigrateDatabase(SqliteConnection connection)
    {
        // Check and add IsLooping column
        var columns = connection.Query<string>("PRAGMA table_info(SoundClips)").ToList();

        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN IsLooping INTEGER DEFAULT 0"); } catch { }
        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN IsFavorite INTEGER DEFAULT 0"); } catch { }
        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN FadeInSeconds REAL DEFAULT 0"); } catch { }
        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN FadeOutSeconds REAL DEFAULT 0"); } catch { }
        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN PlaybackSpeed REAL DEFAULT 1.0"); } catch { }
        try { connection.Execute("ALTER TABLE SoundClips ADD COLUMN PitchSemitones INTEGER DEFAULT 0"); } catch { }
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
            CreatedAt = DateTime.Parse(dto.CreatedAt),
            IsLooping = dto.IsLooping,
            IsFavorite = dto.IsFavorite,
            FadeInSeconds = dto.FadeInSeconds,
            FadeOutSeconds = dto.FadeOutSeconds,
            PlaybackSpeed = dto.PlaybackSpeed == 0 ? 1.0f : dto.PlaybackSpeed,
            PitchSemitones = dto.PitchSemitones
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
            INSERT INTO SoundClips (Name, FilePath, CategoryId, Hotkey, Color, Volume, SortOrder, DurationTicks, CreatedAt, IsLooping, IsFavorite, FadeInSeconds, FadeOutSeconds, PlaybackSpeed, PitchSemitones)
            VALUES (@Name, @FilePath, @CategoryId, @Hotkey, @Color, @Volume, @SortOrder, @DurationTicks, @CreatedAt, @IsLooping, @IsFavorite, @FadeInSeconds, @FadeOutSeconds, @PlaybackSpeed, @PitchSemitones);
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
                CreatedAt = clip.CreatedAt.ToString("O"),
                clip.IsLooping,
                clip.IsFavorite,
                clip.FadeInSeconds,
                clip.FadeOutSeconds,
                clip.PlaybackSpeed,
                clip.PitchSemitones
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
                SortOrder = @SortOrder, DurationTicks = @DurationTicks,
                IsLooping = @IsLooping, IsFavorite = @IsFavorite,
                FadeInSeconds = @FadeInSeconds, FadeOutSeconds = @FadeOutSeconds,
                PlaybackSpeed = @PlaybackSpeed, PitchSemitones = @PitchSemitones
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
                DurationTicks = clip.Duration.Ticks,
                clip.IsLooping,
                clip.IsFavorite,
                clip.FadeInSeconds,
                clip.FadeOutSeconds,
                clip.PlaybackSpeed,
                clip.PitchSemitones
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
        settings.MasterVolume = float.TryParse(await GetSettingAsync("MasterVolume"), out var vol) ? vol : 1.0f;
        settings.GridColumns = int.TryParse(await GetSettingAsync("GridColumns"), out var cols) ? cols : 6;
        settings.ClipsDirectory = await GetSettingAsync("ClipsDirectory") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TgdSoundboard", "Clips");
        settings.Theme = await GetSettingAsync("Theme") ?? "Neon";

        // Streamlabs settings
        settings.StreamlabsToken = await GetSettingAsync("StreamlabsToken") ?? string.Empty;
        settings.StreamlabsAutoConnect = bool.TryParse(await GetSettingAsync("StreamlabsAutoConnect"), out var autoConnect) && autoConnect;
        settings.StreamlabsReplayScene = await GetSettingAsync("StreamlabsReplayScene") ?? string.Empty;

        Directory.CreateDirectory(settings.ClipsDirectory);

        return settings;
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        await SetSettingAsync("OutputDeviceId", settings.OutputDeviceId);
        await SetSettingAsync("MasterVolume", settings.MasterVolume.ToString());
        await SetSettingAsync("GridColumns", settings.GridColumns.ToString());
        await SetSettingAsync("ClipsDirectory", settings.ClipsDirectory);
        await SetSettingAsync("Theme", settings.Theme);

        // Streamlabs settings
        await SetSettingAsync("StreamlabsToken", settings.StreamlabsToken);
        await SetSettingAsync("StreamlabsAutoConnect", settings.StreamlabsAutoConnect.ToString());
        await SetSettingAsync("StreamlabsReplayScene", settings.StreamlabsReplayScene);
    }

    public async Task ExportConfigAsync(string filePath)
    {
        var categories = await GetCategoriesAsync();
        var config = new SoundboardConfig
        {
            ExportDate = DateTime.Now,
            Categories = categories.Select(c => new CategoryExport
            {
                Name = c.Name,
                Color = c.Color,
                SortOrder = c.SortOrder,
                Clips = c.Clips.Select(clip => new ClipExport
                {
                    Name = clip.Name,
                    FileName = Path.GetFileName(clip.FilePath),
                    Hotkey = clip.Hotkey,
                    Color = clip.Color,
                    Volume = clip.Volume,
                    SortOrder = clip.SortOrder,
                    IsLooping = clip.IsLooping,
                    IsFavorite = clip.IsFavorite,
                    FadeInSeconds = clip.FadeInSeconds,
                    FadeOutSeconds = clip.FadeOutSeconds
                }).ToList()
            }).ToList()
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task ImportConfigAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var config = JsonSerializer.Deserialize<SoundboardConfig>(json);
        if (config == null) return;

        foreach (var categoryExport in config.Categories)
        {
            var category = await AddCategoryAsync(categoryExport.Name, categoryExport.Color);

            foreach (var clipExport in categoryExport.Clips)
            {
                // Note: The actual audio file needs to be present in the clips directory
                var clip = new SoundClip
                {
                    Name = clipExport.Name,
                    FilePath = clipExport.FileName, // User will need to re-import files
                    CategoryId = category.Id,
                    Hotkey = clipExport.Hotkey,
                    Color = clipExport.Color,
                    Volume = clipExport.Volume,
                    SortOrder = clipExport.SortOrder,
                    IsLooping = clipExport.IsLooping,
                    IsFavorite = clipExport.IsFavorite,
                    FadeInSeconds = clipExport.FadeInSeconds,
                    FadeOutSeconds = clipExport.FadeOutSeconds
                };

                await AddClipAsync(clip);
            }
        }
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
        public bool IsLooping { get; set; }
        public bool IsFavorite { get; set; }
        public float FadeInSeconds { get; set; }
        public float FadeOutSeconds { get; set; }
        public float PlaybackSpeed { get; set; }
        public int PitchSemitones { get; set; }
    }

    private class SoundboardConfig
    {
        public DateTime ExportDate { get; set; }
        public List<CategoryExport> Categories { get; set; } = new();
    }

    private class CategoryExport
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#4CAF50";
        public int SortOrder { get; set; }
        public List<ClipExport> Clips { get; set; } = new();
    }

    private class ClipExport
    {
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Hotkey { get; set; } = string.Empty;
        public string Color { get; set; } = "#2196F3";
        public float Volume { get; set; } = 1.0f;
        public int SortOrder { get; set; }
        public bool IsLooping { get; set; }
        public bool IsFavorite { get; set; }
        public float FadeInSeconds { get; set; }
        public float FadeOutSeconds { get; set; }
    }
}
