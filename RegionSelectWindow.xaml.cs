using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Rectangle = System.Drawing.Rectangle;

namespace GifRecorder;

public partial class RegionSelectWindow : Window
{
    private System.Windows.Point _startPoint;
    private bool _isDragging;

    public RegionSelectWindow()
    {
        InitializeComponent();
    }

    public Rectangle? SelectedBounds { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var bounds = Win32.GetVirtualScreenBoundsDip(this);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _isDragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        var left = Math.Min(current.X, _startPoint.X);
        var top = Math.Min(current.Y, _startPoint.Y);
        var width = Math.Abs(current.X - _startPoint.X);
        var height = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
    }

    private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();

        var left = Canvas.GetLeft(SelectionRect);
        var top = Canvas.GetTop(SelectionRect);
        var dipRect = new Rect(Left + left, Top + top, SelectionRect.Width, SelectionRect.Height);
        var pixelRect = Win32.DipToPixel(dipRect, this);

        if (pixelRect.Width < 2 || pixelRect.Height < 2)
        {
            SelectedBounds = null;
            DialogResult = false;
        }
        else
        {
            SelectedBounds = pixelRect;
            DialogResult = true;
        }

        Close();
    }
}
