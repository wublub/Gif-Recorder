using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace GifRecorder;

internal static class Win32
{
    private const int DwmaExtendedFrameBounds = 9;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    public const int WmHotkey = 0x0312;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static Rectangle GetVirtualScreenBounds()
    {
        return new Rectangle(
            System.Windows.Forms.SystemInformation.VirtualScreen.Left,
            System.Windows.Forms.SystemInformation.VirtualScreen.Top,
            System.Windows.Forms.SystemInformation.VirtualScreen.Width,
            System.Windows.Forms.SystemInformation.VirtualScreen.Height);
    }

    public static Rect GetVirtualScreenBoundsDip(Visual visual)
    {
        return PixelToDip(GetVirtualScreenBounds(), visual);
    }

    public static bool TryGetWindowBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || IsIconic(hwnd) || !IsWindowVisible(hwnd))
        {
            return false;
        }

        if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out RectNative rect, Marshal.SizeOf<RectNative>()) == 0)
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        else if (GetWindowRect(hwnd, out rect))
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return bounds.Width > 1 && bounds.Height > 1;
    }

    public static IntPtr GetWindowFromPoint(Point point)
    {
        return WindowFromPoint(new PointStruct(point.X, point.Y));
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static IntPtr GetRootVisibleWindow(IntPtr hwnd, params IntPtr[] ignoreHandles)
    {
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = GetAncestor(hwnd, 2);
        if (root != IntPtr.Zero && IsWindowVisible(root))
        {
            hwnd = root;
        }

        while (hwnd != IntPtr.Zero)
        {
            if (IsSelectableWindow(hwnd, ignoreHandles))
            {
                return hwnd;
            }

            hwnd = GetParent(hwnd);
        }

        return IntPtr.Zero;
    }

    public static IntPtr GetTopLevelWindowFromPoint(Point point, params IntPtr[] ignoreHandles)
    {
        var hwnd = GetWindowFromPoint(point);
        return GetRootVisibleWindow(hwnd, ignoreHandles);
    }

    public static bool IsSelectableWindow(IntPtr hwnd, params IntPtr[] ignoreHandles)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        if (ignoreHandles.Any(handle => handle != IntPtr.Zero && handle == hwnd))
        {
            return false;
        }

        if (!TryGetWindowBounds(hwnd, out var bounds) || bounds.Width < 10 || bounds.Height < 10)
        {
            return false;
        }

        var title = GetWindowTitle(hwnd);
        return !string.IsNullOrWhiteSpace(title);
    }

    public static void MakeWindowClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExLayered);
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public static bool IsLeftMousePressed()
    {
        return (GetAsyncKeyState(0x01) & 0x8000) != 0;
    }

    public static bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey)
    {
        return RegisterHotKeyNative(hwnd, id, modifiers, virtualKey);
    }

    public static void UnregisterHotKey(IntPtr hwnd, int id)
    {
        _ = UnregisterHotKeyNative(hwnd, id);
    }

    public static string DescribeWindow(IntPtr hwnd)
    {
        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            try
            {
                title = Process.GetProcessById(GetWindowProcessId(hwnd)).ProcessName;
            }
            catch
            {
                title = "未命名窗口";
            }
        }

        return title;
    }

    public static Rect PixelToDip(Rectangle pixelRect, Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is null)
        {
            return new Rect(pixelRect.Left, pixelRect.Top, pixelRect.Width, pixelRect.Height);
        }

        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(pixelRect.Left, pixelRect.Top));
        var bottomRight = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(pixelRect.Right, pixelRect.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static Rectangle DipToPixel(Rect dipRect, Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is null)
        {
            return Rectangle.FromLTRB(
                (int)Math.Round(dipRect.Left),
                (int)Math.Round(dipRect.Top),
                (int)Math.Round(dipRect.Right),
                (int)Math.Round(dipRect.Bottom));
        }

        var topLeft = source.CompositionTarget.TransformToDevice.Transform(new System.Windows.Point(dipRect.Left, dipRect.Top));
        var bottomRight = source.CompositionTarget.TransformToDevice.Transform(new System.Windows.Point(dipRect.Right, dipRect.Bottom));
        return Rectangle.FromLTRB(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X),
            (int)Math.Round(bottomRight.Y));
    }

    private static int GetWindowProcessId(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return (int)processId;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(PointStruct point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectNative lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RectNative pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterHotKey")]
    private static extern bool RegisterHotKeyNative(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UnregisterHotKey")]
    private static extern bool UnregisterHotKeyNative(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public PointStruct(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
