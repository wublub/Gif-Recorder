using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;

namespace GifRecorder;

public partial class WindowPickerOverlay : Window
{
    private readonly DispatcherTimer _timer;
    private IntPtr _currentHandle;
    private Rectangle _currentBounds;
    private bool _leftButtonWasPressed;
    private IntPtr _overlayHandle;

    public WindowPickerOverlay()
    {
        InitializeComponent();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _timer.Tick += Timer_Tick;
    }

    public WindowSelectionResult? SelectionResult { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var bounds = Win32.GetVirtualScreenBoundsDip(this);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        _overlayHandle = new WindowInteropHelper(this).EnsureHandle();
        Win32.MakeWindowClickThrough(this);
        _timer.Start();
        Activate();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var mousePosition = System.Windows.Forms.Control.MousePosition;
        var hwnd = Win32.GetTopLevelWindowFromPoint(new System.Drawing.Point(mousePosition.X, mousePosition.Y), _overlayHandle);
        if (hwnd == IntPtr.Zero || !Win32.TryGetWindowBounds(hwnd, out var bounds))
        {
            HighlightRect.Visibility = Visibility.Collapsed;
            WindowTitleText.Text = "当前窗口：无";
            _currentHandle = IntPtr.Zero;
            _currentBounds = Rectangle.Empty;
        }
        else
        {
            _currentHandle = hwnd;
            _currentBounds = bounds;
            WindowTitleText.Text = $"当前窗口：{Win32.DescribeWindow(hwnd)}";
            HighlightRect.Visibility = Visibility.Visible;
            var dipRect = Win32.PixelToDip(bounds, this);
            Canvas.SetLeft(HighlightRect, dipRect.Left - Left);
            Canvas.SetTop(HighlightRect, dipRect.Top - Top);
            HighlightRect.Width = dipRect.Width;
            HighlightRect.Height = dipRect.Height;
        }

        var leftButtonPressed = Win32.IsLeftMousePressed();
        if (leftButtonPressed && !_leftButtonWasPressed && _currentHandle != IntPtr.Zero && _currentBounds.Width > 0 && _currentBounds.Height > 0)
        {
            _timer.Stop();
            SelectionResult = new WindowSelectionResult(_currentHandle, _currentBounds, Win32.DescribeWindow(_currentHandle));
            DialogResult = true;
            Close();
            return;
        }

        _leftButtonWasPressed = leftButtonPressed;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            _timer.Stop();
            DialogResult = false;
            Close();
        }
    }
}
