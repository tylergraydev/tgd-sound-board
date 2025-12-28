using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.Wave;

namespace TgdSoundboard.Controls;

public class WaveformControl : Control
{
    private float[]? _waveformData;
    private Point? _dragStart;
    private bool _isDragging;

    public static readonly DependencyProperty AudioFileProperty =
        DependencyProperty.Register(nameof(AudioFile), typeof(string), typeof(WaveformControl),
            new PropertyMetadata(null, OnAudioFileChanged));

    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(WaveformControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(WaveformControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(WaveformControl),
            new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty PlaybackPositionProperty =
        DependencyProperty.Register(nameof(PlaybackPosition), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnPlaybackPositionChanged));

    public static readonly DependencyProperty WaveformColorProperty =
        DependencyProperty.Register(nameof(WaveformColor), typeof(Brush), typeof(WaveformControl),
            new PropertyMetadata(Brushes.LightBlue));

    public static readonly DependencyProperty SelectionColorProperty =
        DependencyProperty.Register(nameof(SelectionColor), typeof(Brush), typeof(WaveformControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(100, 33, 150, 243))));

    public string? AudioFile
    {
        get => (string?)GetValue(AudioFileProperty);
        set => SetValue(AudioFileProperty, value);
    }

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double PlaybackPosition
    {
        get => (double)GetValue(PlaybackPositionProperty);
        set => SetValue(PlaybackPositionProperty, value);
    }

    public Brush WaveformColor
    {
        get => (Brush)GetValue(WaveformColorProperty);
        set => SetValue(WaveformColorProperty, value);
    }

    public Brush SelectionColor
    {
        get => (Brush)GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }

    static WaveformControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WaveformControl),
            new FrameworkPropertyMetadata(typeof(WaveformControl)));
    }

    public WaveformControl()
    {
        Background = Brushes.Transparent;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
    }

    private static void OnAudioFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control && e.NewValue is string filePath)
        {
            control.LoadWaveform(filePath);
        }
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
        {
            control.InvalidateVisual();
        }
    }

    private static void OnPlaybackPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
        {
            control.InvalidateVisual();
        }
    }

    private void LoadWaveform(string filePath)
    {
        Task.Run(() =>
        {
            try
            {
                using var reader = new AudioFileReader(filePath);
                var samplesPerPixel = (int)(reader.Length / reader.WaveFormat.BlockAlign / 2000.0);
                samplesPerPixel = Math.Max(1, samplesPerPixel);

                var samples = new List<float>();
                var buffer = new float[samplesPerPixel * reader.WaveFormat.Channels];
                int samplesRead;

                while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    float max = 0;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        max = Math.Max(max, Math.Abs(buffer[i]));
                    }
                    samples.Add(max);
                }

                Dispatcher.Invoke(() =>
                {
                    _waveformData = samples.ToArray();
                    Duration = reader.TotalTime;
                    SelectionStart = 0;
                    SelectionEnd = 1;
                    InvalidateVisual();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading waveform: {ex.Message}");
            }
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Background, null, bounds);

        if (_waveformData == null || _waveformData.Length == 0) return;

        var midY = ActualHeight / 2;
        var maxHeight = ActualHeight / 2 - 2;

        // Draw selection background
        if (SelectionStart < SelectionEnd)
        {
            var selStartX = SelectionStart * ActualWidth;
            var selEndX = SelectionEnd * ActualWidth;
            dc.DrawRectangle(SelectionColor, null,
                new Rect(selStartX, 0, selEndX - selStartX, ActualHeight));
        }

        // Draw waveform
        var pen = new Pen(WaveformColor, 1);
        var widthPerSample = ActualWidth / _waveformData.Length;

        for (int i = 0; i < _waveformData.Length; i++)
        {
            var x = i * widthPerSample;
            var height = _waveformData[i] * maxHeight;
            dc.DrawLine(pen, new Point(x, midY - height), new Point(x, midY + height));
        }

        // Draw playback position
        if (PlaybackPosition > 0 && PlaybackPosition <= 1)
        {
            var posX = PlaybackPosition * ActualWidth;
            var playPen = new Pen(Brushes.Red, 2);
            dc.DrawLine(playPen, new Point(posX, 0), new Point(posX, ActualHeight));
        }

        // Draw selection handles
        if (SelectionStart < SelectionEnd)
        {
            var handlePen = new Pen(Brushes.White, 2);
            var startX = SelectionStart * ActualWidth;
            var endX = SelectionEnd * ActualWidth;

            dc.DrawLine(handlePen, new Point(startX, 0), new Point(startX, ActualHeight));
            dc.DrawLine(handlePen, new Point(endX, 0), new Point(endX, ActualHeight));
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _isDragging = true;
        CaptureMouse();

        var normalizedX = _dragStart.Value.X / ActualWidth;
        SelectionStart = Math.Clamp(normalizedX, 0, 1);
        SelectionEnd = SelectionStart;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragStart == null) return;

        var currentPos = e.GetPosition(this);
        var startNorm = _dragStart.Value.X / ActualWidth;
        var endNorm = currentPos.X / ActualWidth;

        if (startNorm > endNorm)
        {
            (startNorm, endNorm) = (endNorm, startNorm);
        }

        SelectionStart = Math.Clamp(startNorm, 0, 1);
        SelectionEnd = Math.Clamp(endNorm, 0, 1);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _dragStart = null;
        ReleaseMouseCapture();

        // Ensure minimum selection
        if (Math.Abs(SelectionEnd - SelectionStart) < 0.01)
        {
            SelectionEnd = Math.Min(SelectionStart + 0.1, 1);
        }
    }
}
