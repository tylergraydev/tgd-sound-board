using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NAudio.Wave;

namespace TgdSoundboard.Views;

public partial class ClipEditorWindow : Window, INotifyPropertyChanged
{
    private string _filePath;
    private string _fileName;
    private string _clipName;
    private double _selectionStart;
    private double _selectionEnd = 1.0;
    private TimeSpan _duration;
    private double _playbackPosition;
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioReader;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ClipEditorResult? Result { get; private set; }

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string ClipName
    {
        get => _clipName;
        set { _clipName = value; OnPropertyChanged(); }
    }

    public double SelectionStart
    {
        get => _selectionStart;
        set
        {
            _selectionStart = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionStartTime));
            OnPropertyChanged(nameof(SelectionDuration));
        }
    }

    public double SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            _selectionEnd = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionEndTime));
            OnPropertyChanged(nameof(SelectionDuration));
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionStartTime));
            OnPropertyChanged(nameof(SelectionEndTime));
            OnPropertyChanged(nameof(SelectionDuration));
        }
    }

    public double PlaybackPosition
    {
        get => _playbackPosition;
        set { _playbackPosition = value; OnPropertyChanged(); }
    }

    public TimeSpan SelectionStartTime => TimeSpan.FromTicks((long)(Duration.Ticks * SelectionStart));
    public TimeSpan SelectionEndTime => TimeSpan.FromTicks((long)(Duration.Ticks * SelectionEnd));
    public TimeSpan SelectionDuration => SelectionEndTime - SelectionStartTime;

    public ClipEditorWindow(string filePath)
    {
        InitializeComponent();
        DataContext = this;

        _filePath = filePath;
        _fileName = System.IO.Path.GetFileName(filePath);
        _clipName = System.IO.Path.GetFileNameWithoutExtension(filePath);
    }

    private void PlaySelection_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();

        try
        {
            _audioReader = new AudioFileReader(FilePath);
            var startPos = (long)(SelectionStart * _audioReader.Length);
            startPos -= startPos % _audioReader.WaveFormat.BlockAlign;
            _audioReader.Position = startPos;

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioReader);
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _waveOut.Play();

            // Update playback position
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            timer.Tick += (s, args) =>
            {
                if (_audioReader == null || _waveOut?.PlaybackState != PlaybackState.Playing)
                {
                    timer.Stop();
                    PlaybackPosition = 0;
                    return;
                }

                var position = (double)_audioReader.Position / _audioReader.Length;
                if (position >= SelectionEnd)
                {
                    StopPlayback();
                    timer.Stop();
                    return;
                }

                PlaybackPosition = position;
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error playing audio: {ex.Message}", "Playback Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
    }

    private void StopPlayback()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _audioReader?.Dispose();
        _audioReader = null;

        PlaybackPosition = 0;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.Invoke(() => PlaybackPosition = 0);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        DialogResult = false;
        Close();
    }

    private void CreateClip_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ClipName))
        {
            MessageBox.Show("Please enter a name for the clip.", "Missing Name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ClipNameTextBox.Focus();
            return;
        }

        if (SelectionDuration.TotalMilliseconds < 100)
        {
            MessageBox.Show("Please select a longer portion of the audio.", "Selection Too Short",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StopPlayback();

        Result = new ClipEditorResult
        {
            SourcePath = FilePath,
            ClipName = ClipName,
            StartTime = SelectionStartTime,
            EndTime = SelectionEndTime
        };

        DialogResult = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopPlayback();
        base.OnClosing(e);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ClipEditorResult
{
    public required string SourcePath { get; init; }
    public required string ClipName { get; init; }
    public required TimeSpan StartTime { get; init; }
    public required TimeSpan EndTime { get; init; }
}
