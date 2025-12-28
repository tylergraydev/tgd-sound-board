using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TgdSoundboard.Models;
using TgdSoundboard.Views;

namespace TgdSoundboard;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
        StateChanged += MainWindow_StateChanged;
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

    private void ClipCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            App.MainViewModel.PlayClipCommand.Execute(clip);
        }
    }

    private void EditClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            // TODO: Open edit dialog
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
}
