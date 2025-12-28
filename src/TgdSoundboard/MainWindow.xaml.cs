using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TgdSoundboard.Models;
using TgdSoundboard.Services;
using TgdSoundboard.Views;

namespace TgdSoundboard;

public partial class MainWindow : Window
{
    private Point _dragStartPoint;
    private SoundClip? _draggedClip;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
        StateChanged += MainWindow_StateChanged;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set theme selector to current theme
        var currentTheme = ThemeService.CurrentTheme.ToString();
        foreach (ComboBoxItem item in ThemeSelector.Items)
        {
            if (item.Tag?.ToString() == currentTheme)
            {
                ThemeSelector.SelectedItem = item;
                break;
            }
        }
    }

    private async void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
        {
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
            {
                ThemeService.CurrentTheme = theme;

                // Save theme preference
                var settings = await App.Database.GetAppSettingsAsync();
                settings.Theme = themeName;
                await App.Database.SaveAppSettingsAsync(settings);
            }
        }
    }

    #region Window Chrome Controls

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
        }
        else
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeIcon.Kind = WindowState == WindowState.Maximized
            ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore
            : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        App.MainViewModel.StopAllCommand.Execute(null);
    }

    private void OpenMixer_Click(object sender, RoutedEventArgs e)
    {
        var mixerWindow = new MixerWindow();
        mixerWindow.Owner = this;
        mixerWindow.Show();
    }

    private void OpenAppRouting_Click(object sender, RoutedEventArgs e)
    {
        var appRoutingWindow = new AppRoutingWindow();
        appRoutingWindow.Owner = this;
        appRoutingWindow.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var hasAudioFiles = files.Any(f =>
            {
                var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                return ext is ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a";
            });

            e.Effects = hasAudioFiles ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var file in files)
        {
            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a")
            {
                await App.MainViewModel.AddClipCommand.ExecuteAsync(file);
            }
        }
    }

    private async void ImportClip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All Files|*.*",
            Title = "Select Audio File",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                await App.MainViewModel.AddClipCommand.ExecuteAsync(file);
            }
        }
    }

    private void ImportAndTrim_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All Files|*.*",
            Title = "Select Audio File to Clip"
        };

        if (dialog.ShowDialog() == true)
        {
            var clipEditor = new ClipEditorWindow(dialog.FileName);
            clipEditor.Owner = this;
            if (clipEditor.ShowDialog() == true && clipEditor.Result != null)
            {
                var result = clipEditor.Result;
                _ = App.MainViewModel.SaveTrimmedClipCommand.ExecuteAsync(
                    (result.SourcePath, result.ClipName, result.StartTime, result.EndTime));
            }
        }
    }

    private async void ClipCard_Click(object sender, MouseButtonEventArgs e)
    {
        // Capture start point for drag detection
        if (sender is FrameworkElement element)
        {
            _dragStartPoint = e.GetPosition(element);
        }

        if (sender is FrameworkElement elem && elem.DataContext is SoundClip clip)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to toggle loop
                e.Handled = true;
                clip.IsLooping = !clip.IsLooping;
                await App.Database.UpdateClipAsync(clip);
            }
            else
            {
                // Single-click to play
                App.MainViewModel.PlayClipCommand.Execute(clip);
            }
        }
    }

    private async void EditClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            var dialog = new RenameDialog(clip.Name);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                clip.Name = dialog.ClipName;
                await App.Database.UpdateClipAsync(clip);
            }
        }
    }

    private async void DeleteClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete '{clip.Name}'?",
                "Delete Clip",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await App.MainViewModel.DeleteClipCommand.ExecuteAsync(clip);
            }
        }
    }

    private async void SetClipColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string color)
        {
            // Navigate up to find the clip from the context menu's placement target
            var contextMenu = menuItem.Parent as MenuItem;
            while (contextMenu?.Parent is MenuItem parent)
            {
                contextMenu = parent;
            }

            var rootMenu = contextMenu?.Parent as ContextMenu;
            if (rootMenu?.PlacementTarget is FrameworkElement element && element.DataContext is SoundClip clip)
            {
                clip.Color = color;
                await App.Database.UpdateClipAsync(clip);
            }
        }
    }

    private async void DuplicateClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            // Create a copy with a new name
            var newClip = new SoundClip
            {
                Name = clip.Name + " (Copy)",
                FilePath = clip.FilePath,
                CategoryId = clip.CategoryId,
                Hotkey = string.Empty, // Don't copy hotkey to avoid conflicts
                Color = clip.Color,
                Volume = clip.Volume,
                Duration = clip.Duration,
                CreatedAt = DateTime.Now
            };

            var savedClip = await App.Database.AddClipAsync(newClip);

            // Add to the current category's clips collection
            var category = App.MainViewModel.Categories.FirstOrDefault(c => c.Id == clip.CategoryId);
            category?.Clips.Add(savedClip);
        }
    }

    #region Search/Filter

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        App.MainViewModel.FilterClips(searchText);
    }

    #endregion

    #region Per-Clip Volume

    private async void ClipVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is SoundClip clip)
        {
            clip.Volume = (float)e.NewValue;
            await App.Database.UpdateClipAsync(clip);

            // Update volume if currently playing
            App.AudioPlayback.SetClipVolume(clip.Id, clip.Volume);
        }
    }

    #endregion

    #region Hotkey

    private async void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            var dialog = new HotkeyDialog(clip.Hotkey);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                // Unregister old hotkey
                if (!string.IsNullOrEmpty(clip.Hotkey))
                {
                    App.GlobalHotkeys.UnregisterHotkey(clip.Hotkey);
                }

                clip.Hotkey = dialog.Hotkey;
                await App.Database.UpdateClipAsync(clip);

                // Register new hotkey
                if (!string.IsNullOrEmpty(clip.Hotkey))
                {
                    App.GlobalHotkeys.RegisterHotkey(clip.Hotkey, () =>
                    {
                        Dispatcher.Invoke(() => App.MainViewModel.PlayClipCommand.Execute(clip));
                    });
                }
            }
        }
    }

    #endregion

    #region Drag & Drop Reorder

    private void ClipCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
        {
            var currentPos = e.GetPosition(element);
            var diff = _dragStartPoint - currentPos;

            // Only start drag if moved enough distance
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (element.DataContext is SoundClip clip)
                {
                    _draggedClip = clip;
                    var data = new DataObject("SoundClip", clip);
                    DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
                    _draggedClip = null;
                }
            }
        }
    }

    private void ClipCard_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("SoundClip"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void ClipCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("SoundClip")) return;

        var droppedClip = e.Data.GetData("SoundClip") as SoundClip;
        if (droppedClip == null) return;

        if (sender is FrameworkElement element && element.DataContext is SoundClip targetClip)
        {
            if (droppedClip.Id == targetClip.Id) return;
            if (droppedClip.CategoryId != targetClip.CategoryId) return;

            var category = App.MainViewModel.Categories.FirstOrDefault(c => c.Id == droppedClip.CategoryId);
            if (category == null) return;

            // Get current indices
            var oldIndex = category.Clips.IndexOf(droppedClip);
            var newIndex = category.Clips.IndexOf(targetClip);

            if (oldIndex < 0 || newIndex < 0) return;

            // Move the clip
            category.Clips.Move(oldIndex, newIndex);

            // Update sort orders in database
            for (int i = 0; i < category.Clips.Count; i++)
            {
                category.Clips[i].SortOrder = i;
                await App.Database.UpdateClipAsync(category.Clips[i]);
            }
        }

        e.Handled = true;
    }

    #endregion

    #region Loop, Favorite, Fade

    private async void ToggleLoop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.DataContext is SoundClip clip)
            {
                await App.Database.UpdateClipAsync(clip);
            }
        }
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is FrameworkElement element && element.DataContext is SoundClip clip)
            {
                await App.Database.UpdateClipAsync(clip);
                App.MainViewModel.RefreshFavorites();
            }
        }
    }

    private async void FadeIn_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is SoundClip clip)
        {
            clip.FadeInSeconds = (float)e.NewValue;
            await App.Database.UpdateClipAsync(clip);
        }
    }

    private async void FadeOut_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is SoundClip clip)
        {
            clip.FadeOutSeconds = (float)e.NewValue;
            await App.Database.UpdateClipAsync(clip);
        }
    }

    #endregion

    #region Favorites Bar

    private void FavoriteClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SoundClip clip)
        {
            App.MainViewModel.PlayClipCommand.Execute(clip);
        }
    }

    #endregion

    #region Random Play

    private void RandomPlay_Click(object sender, RoutedEventArgs e)
    {
        var category = App.MainViewModel.SelectedCategory;
        if (category == null || category.Clips.Count == 0) return;

        var random = new Random();
        var randomClip = category.Clips[random.Next(category.Clips.Count)];
        App.MainViewModel.PlayClipCommand.Execute(randomClip);
    }

    #endregion

    #region Import/Export Config

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON Files|*.json",
            Title = "Export Soundboard Config",
            FileName = "soundboard_config.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await App.Database.ExportConfigAsync(dialog.FileName);
                MessageBox.Show("Configuration exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON Files|*.json",
            Title = "Import Soundboard Config"
        };

        if (dialog.ShowDialog() == true)
        {
            var result = MessageBox.Show(
                "This will merge the imported configuration with your existing setup. Continue?",
                "Import Config",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await App.Database.ImportConfigAsync(dialog.FileName);

                    // Reload categories
                    var categories = await App.Database.GetCategoriesAsync();
                    App.MainViewModel.Categories.Clear();
                    foreach (var category in categories)
                    {
                        App.MainViewModel.Categories.Add(category);
                    }

                    if (App.MainViewModel.Categories.Count > 0)
                    {
                        App.MainViewModel.SelectedCategory = App.MainViewModel.Categories[0];
                    }

                    App.MainViewModel.RefreshFavorites();
                    MessageBox.Show("Configuration imported successfully!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    #endregion

    #region Speed/Pitch

    private async void PlaybackSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is SoundClip clip)
        {
            clip.PlaybackSpeed = (float)e.NewValue;
            await App.Database.UpdateClipAsync(clip);
        }
    }

    private async void PitchSemitones_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.DataContext is SoundClip clip)
        {
            clip.PitchSemitones = (int)e.NewValue;
            await App.Database.UpdateClipAsync(clip);
        }
    }

    #endregion

    #region Queue

    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            App.AudioPlayback.AddToQueue(clip);
        }
    }

    private void ShowQueue_Click(object sender, RoutedEventArgs e)
    {
        var queueWindow = new QueueWindow();
        queueWindow.Owner = this;
        queueWindow.Show();
    }

    #endregion

    #region Streamlabs

    private async void SaveReplay_Click(object sender, RoutedEventArgs e)
    {
        await App.MainViewModel.SaveReplayCommand.ExecuteAsync(null);
    }

    private void StreamlabsSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new StreamlabsSettingsDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    #endregion

    #region Category Context Menu

    private async void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.Category category)
        {
            var dialog = new RenameDialog(category.Name);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                category.Name = dialog.ClipName;
                await App.Database.UpdateCategoryAsync(category);
            }
        }
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Models.Category category)
        {
            if (App.MainViewModel.Categories.Count <= 1)
            {
                MessageBox.Show("Cannot delete the last category.", "Delete Category", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{category.Name}'?\nThis will also delete all clips in this category.",
                "Delete Category",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await App.MainViewModel.DeleteCategoryCommand.ExecuteAsync(category);
            }
        }
    }

    #endregion
}
