using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TgdSoundboard.Views;

public partial class ReplaysWindow : Window
{
    private string _replayFolder = string.Empty;

    public ReplaysWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _replayFolder = App.MainViewModel.Settings.StreamlabsReplayFolder;
        FolderPathText.Text = _replayFolder;
        LoadReplays();
    }

    private void LoadReplays()
    {
        if (string.IsNullOrEmpty(_replayFolder) || !Directory.Exists(_replayFolder))
        {
            EmptyState.Visibility = Visibility.Visible;
            ReplayList.ItemsSource = null;
            return;
        }

        var videoExtensions = new[] { ".mp4", ".mkv", ".flv", ".avi", ".mov", ".webm" };
        var replays = Directory.GetFiles(_replayFolder)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => new ReplayFile
            {
                FilePath = f,
                FileName = Path.GetFileName(f),
                CreatedTime = File.GetCreationTime(f),
                FileSize = new FileInfo(f).Length
            })
            .OrderByDescending(r => r.CreatedTime)
            .ToList();

        EmptyState.Visibility = replays.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReplayList.ItemsSource = replays;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadReplays();
    }

    private void Replay_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ReplayFile replay)
        {
            PlayReplayInStreamlabs(replay.FilePath);
        }
    }

    private void PlayReplay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string filePath)
        {
            PlayReplayInStreamlabs(filePath);
        }
    }

    private async void PlayReplayInStreamlabs(string filePath)
    {
        if (!App.Streamlabs.IsConnected)
        {
            MessageBox.Show(
                "Not connected to Streamlabs Desktop.\n\nGo to Streamlabs Settings to configure the connection.",
                "Not Connected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var success = await App.Streamlabs.PlayReplayAsync(filePath);
            if (!success)
            {
                MessageBox.Show(
                    $"Failed to play replay.\n\n{App.Streamlabs.LastError}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error playing replay: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_replayFolder) && Directory.Exists(_replayFolder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _replayFolder,
                UseShellExecute = true
            });
        }
    }

    private void ContextPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ReplayFile replay)
        {
            PlayReplayInStreamlabs(replay.FilePath);
        }
    }

    private void ContextRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ReplayFile replay)
        {
            var currentName = Path.GetFileNameWithoutExtension(replay.FilePath);
            var extension = Path.GetExtension(replay.FilePath);

            var dialog = new RenameDialog(currentName);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newPath = Path.Combine(_replayFolder, dialog.ClipName + extension);

                    if (File.Exists(newPath))
                    {
                        MessageBox.Show(
                            "A file with that name already exists.",
                            "Rename Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    File.Move(replay.FilePath, newPath);
                    LoadReplays();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to rename file: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }

    private void ContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ReplayFile replay)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{replay.FileName}'?\n\nThis cannot be undone.",
                "Delete Replay",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(replay.FilePath);
                    LoadReplays();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to delete file: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }
}

public class ReplayFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
    public long FileSize { get; set; }

    public string FileSizeText
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024.0 * 1024):F1} MB";
            return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
