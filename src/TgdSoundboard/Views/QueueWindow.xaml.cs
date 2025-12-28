using System.Windows;
using System.Windows.Input;
using TgdSoundboard.Models;

namespace TgdSoundboard.Views;

public partial class QueueWindow : Window
{
    public QueueWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PlayNext_Click(object sender, RoutedEventArgs e)
    {
        App.AudioPlayback.PlayNextInQueue();
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        App.AudioPlayback.ClearQueue();
    }

    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is SoundClip clip)
        {
            App.AudioPlayback.RemoveFromQueue(clip);
        }
    }
}
