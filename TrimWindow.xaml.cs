using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Canvas = System.Windows.Controls.Canvas;
using Imaging = System.Windows.Interop.Imaging;

namespace GifRecorder;

public partial class TrimWindow : Window
{
    private readonly RecordingSessionData _session;
    private readonly ObservableCollection<TrimRangeItem> _ranges = new();

    private readonly DispatcherTimer _timer;

    private double? _markStartSeconds;

    public RecordingSessionData? ResultSession { get; private set; }

    public TrimWindow(RecordingSessionData session)
    {
        InitializeComponent();

        _session = session;
        RangesListBox.ItemsSource = _ranges;

        var fps = Math.Max(1, session.NominalFps);
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000d / fps)
        };
        _timer.Tick += (_, _) => AdvancePlayback();

        Loaded += (_, _) => InitializeUi();
        Closed += (_, _) => _timer.Stop();
    }

    private void InitializeUi()
    {
        var maxSeconds = Math.Max(0.001, _session.Duration.TotalSeconds);
        TimeSlider.Minimum = 0;
        TimeSlider.Maximum = maxSeconds;
        TimeSlider.Value = 0;

        InfoTextBlock.Text = $"帧数 {_session.Frames.Count} | 时长 {maxSeconds:0.##}s | FPS {_session.NominalFps}";

        UpdateTimeLabels();
        UpdatePreview();
        RedrawTimeline();
    }

    private void TimeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateTimeLabels();
        UpdatePreview();
        RedrawTimeline();
    }

    private void RangeCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RedrawTimeline();
    }

    private void PlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            PlayPauseButton.Content = "播放";
        }
        else
        {
            _timer.Start();
            PlayPauseButton.Content = "暂停";
        }
    }

    private void AdvancePlayback()
    {
        var next = TimeSlider.Value + (1d / Math.Max(1, _session.NominalFps));
        if (next >= TimeSlider.Maximum)
        {
            TimeSlider.Value = TimeSlider.Maximum;
            _timer.Stop();
            PlayPauseButton.Content = "播放";
            return;
        }

        TimeSlider.Value = next;
    }

    private void MarkStartButton_OnClick(object sender, RoutedEventArgs e)
    {
        _markStartSeconds = TimeSlider.Value;
        UpdateTimeLabels();
        RedrawTimeline();
    }

    private void AddKeepButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddRange(TrimRangeKind.Keep);
    }

    private void AddRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddRange(TrimRangeKind.Remove);
    }

    private void AddRange(TrimRangeKind kind)
    {
        if (_markStartSeconds is null)
        {
            System.Windows.MessageBox.Show(this, "请先点击“标记起点”。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var start = _markStartSeconds.Value;
        var end = TimeSlider.Value;
        if (Math.Abs(end - start) < 0.0001)
        {
            System.Windows.MessageBox.Show(this, "起点与终点相同，请先移动进度条。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (end < start)
        {
            (start, end) = (end, start);
        }

        start = Math.Clamp(start, TimeSlider.Minimum, TimeSlider.Maximum);
        end = Math.Clamp(end, TimeSlider.Minimum, TimeSlider.Maximum);

        _ranges.Add(new TrimRangeItem(kind, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
        _markStartSeconds = null;

        UpdateTimeLabels();
        RedrawTimeline();
    }

    private void DeleteRangeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RangesListBox.SelectedItem is not TrimRangeItem item)
        {
            return;
        }

        _ranges.Remove(item);
        RedrawTimeline();
    }

    private void ClearRangesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _ranges.Clear();
        _markStartSeconds = null;
        UpdateTimeLabels();
        RedrawTimeline();
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_ranges.Count == 0)
            {
                // 没有区间：视为不做裁剪
                ResultSession = null;
                DialogResult = true;
                Close();
                return;
            }

            var keep = _ranges
                .Where(r => r.Kind == TrimRangeKind.Keep)
                .Select(r => new TimeRange(r.Start, r.End))
                .ToList();

            var remove = _ranges
                .Where(r => r.Kind == TrimRangeKind.Remove)
                .Select(r => new TimeRange(r.Start, r.End))
                .ToList();

            var selected = _session.Frames
                .Where(frame => keep.Count == 0 || keep.Any(range => IsInside(frame.Timestamp, range)))
                .Where(frame => remove.Count == 0 || remove.All(range => !IsInside(frame.Timestamp, range)))
                .ToList();

            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "裁剪后没有可用内容，请调整区间。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fps = Math.Max(1, _session.NominalFps);
            var cloned = selected
                .Select((frame, index) => new RecordedFrame((Bitmap)frame.Bitmap.Clone(), TimeSpan.FromSeconds(index / (double)fps)))
                .ToList();

            var duration = TimeSpan.FromSeconds(cloned.Count / (double)fps);
            ResultSession = new RecordingSessionData(
                cloned,
                duration,
                _session.FrameSize,
                fps,
                _session.SourceDescription + "（已裁剪）");

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "裁剪失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsInside(TimeSpan value, TimeRange range)
    {
        return value >= range.Start && value <= range.End;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateTimeLabels()
    {
        CurrentTimeTextBlock.Text = $"当前 {TimeSlider.Value:0.##}s";
        MarkTextBlock.Text = _markStartSeconds is null ? "起点：未标记" : $"起点 {_markStartSeconds.Value:0.##}s";
    }

    private void UpdatePreview()
    {
        if (_session.Frames.Count == 0)
        {
            return;
        }

        var t = TimeSpan.FromSeconds(TimeSlider.Value);
        var index = FindNearestFrameIndex(t);
        index = Math.Clamp(index, 0, _session.Frames.Count - 1);

        var bmp = _session.Frames[index].Bitmap;
        PreviewImage.Source = ToBitmapSource(bmp);
    }

    private int FindNearestFrameIndex(TimeSpan time)
    {
        var frames = _session.Frames;
        var lo = 0;
        var hi = frames.Count - 1;

        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (frames[mid].Timestamp < time)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        if (lo <= 0)
        {
            return 0;
        }

        var prev = lo - 1;
        var a = frames[prev].Timestamp;
        var b = frames[lo].Timestamp;
        return (time - a) <= (b - time) ? prev : lo;
    }

    private void RedrawTimeline()
    {
        RangeCanvas.Children.Clear();

        var width = RangeCanvas.ActualWidth;
        var height = RangeCanvas.ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var duration = Math.Max(0.001, _session.Duration.TotalSeconds);

        foreach (var item in _ranges)
        {
            var start = Math.Clamp(item.Start.TotalSeconds, 0, duration);
            var end = Math.Clamp(item.End.TotalSeconds, 0, duration);
            if (end <= start)
            {
                continue;
            }

            var x1 = start / duration * width;
            var x2 = end / duration * width;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, x2 - x1),
                Height = Math.Max(8, height - 10),
                RadiusX = 4,
                RadiusY = 4,
                Fill = item.Kind == TrimRangeKind.Keep
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 46, 204, 113))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 231, 76, 60))
            };

            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, 5);
            RangeCanvas.Children.Add(rect);
        }

        // 当前播放位置
        var currentX = Math.Clamp(TimeSlider.Value / duration * width, 0, width);
        RangeCanvas.Children.Add(new Line
        {
            X1 = currentX,
            X2 = currentX,
            Y1 = 0,
            Y2 = height,
            StrokeThickness = 2,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 80, 80, 80))
        });

        // 起点标记
        if (_markStartSeconds is not null)
        {
            var markX = Math.Clamp(_markStartSeconds.Value / duration * width, 0, width);
            RangeCanvas.Children.Add(new Line
            {
                X1 = markX,
                X2 = markX,
                Y1 = 0,
                Y2 = height,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 52, 152, 219))
            });
        }
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

public enum TrimRangeKind
{
    Keep,
    Remove
}

public sealed class TrimRangeItem
{
    public TrimRangeItem(TrimRangeKind kind, TimeSpan start, TimeSpan end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public TrimRangeKind Kind { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string DisplayText
    {
        get
        {
            var label = Kind == TrimRangeKind.Keep ? "保留" : "删除";
            return $"{label} {Start.TotalSeconds:0.##}-{End.TotalSeconds:0.##} 秒";
        }
    }
}
