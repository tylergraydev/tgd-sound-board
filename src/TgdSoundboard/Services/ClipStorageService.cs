using System.IO;
using NAudio.Wave;

namespace TgdSoundboard.Services;

public class ClipStorageService
{
    private readonly string _clipsDirectory;

    public ClipStorageService(string? clipsDirectory = null)
    {
        _clipsDirectory = clipsDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TgdSoundboard", "Clips");
        Directory.CreateDirectory(_clipsDirectory);
    }

    public string ClipsDirectory => _clipsDirectory;

    public async Task<string> ImportAudioFileAsync(string sourceFilePath, string clipName)
    {
        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        var safeFileName = SanitizeFileName(clipName) + extension;
        var destPath = GetUniqueFilePath(safeFileName);

        await Task.Run(() => File.Copy(sourceFilePath, destPath));

        return destPath;
    }

    public async Task<string> SaveTrimmedClipAsync(string sourceFilePath, string clipName,
        TimeSpan startTime, TimeSpan endTime)
    {
        var destPath = GetUniqueFilePath(SanitizeFileName(clipName) + ".wav");

        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(sourceFilePath);
            var startBytes = (long)(startTime.TotalSeconds * reader.WaveFormat.AverageBytesPerSecond);
            var endBytes = (long)(endTime.TotalSeconds * reader.WaveFormat.AverageBytesPerSecond);

            // Align to block boundaries
            startBytes -= startBytes % reader.WaveFormat.BlockAlign;
            endBytes -= endBytes % reader.WaveFormat.BlockAlign;

            reader.Position = startBytes;
            var bytesToRead = endBytes - startBytes;

            using var writer = new WaveFileWriter(destPath, reader.WaveFormat);
            var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
            long totalBytesRead = 0;

            while (totalBytesRead < bytesToRead)
            {
                var toRead = (int)Math.Min(buffer.Length, bytesToRead - totalBytesRead);
                var bytesRead = reader.Read(buffer, 0, toRead);
                if (bytesRead == 0) break;

                writer.Write(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;
            }
        });

        return destPath;
    }

    public void DeleteClipFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath) && filePath.StartsWith(_clipsDirectory))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting clip file: {ex.Message}");
        }
    }

    private string GetUniqueFilePath(string fileName)
    {
        var basePath = Path.Combine(_clipsDirectory, fileName);
        if (!File.Exists(basePath)) return basePath;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;

        while (File.Exists(basePath))
        {
            basePath = Path.Combine(_clipsDirectory, $"{nameWithoutExt}_{counter}{extension}");
            counter++;
        }

        return basePath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "clip" : sanitized.Trim();
    }
}
