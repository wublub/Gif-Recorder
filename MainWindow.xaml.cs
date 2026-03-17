using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Rectangle = System.Drawing.Rectangle;

namespace GifRecorder;

public partial class MainWindow : Window
{
    private const int ToggleHotkeyId = 1000;
    private const uint ToggleHotkeyModifiers = Win32.ModAlt | Win32.ModNoRepeat;
    private const uint ToggleHotkeyVk = 0x47; // 'G'

    private readonly CaptureSession _captureSession = new();
    private readonly GifEncoderService _gifEncoderService = new();

    private readonly List<GifQualityPreset> _qualityPresets =
    [
        new(GifQuality.Low, "低", 8, 960, 64, false, true, 65),
        new(GifQuality.Medium, "中", 12, 1280, 128, false, true, 78),
        new(GifQuality.High, "高", 15, 1920, 256, true, true, 90)
    ];

    private CaptureMode _captureMode = CaptureMode.FullScreen;
    private Rectangle? _selectedRegion;
    private WindowSelectionResult? _selectedWindow;
    private RecordingSessionData? _currentSession;

    private HwndSource? _hwndSource;
    private ExportOptions _lastExportOptions;

    public MainWindow()
    {
        InitializeComponent();

        _lastExportOptions = _qualityPresets.ElementAtOrDefault(1)?.ToExportOptions(ExportFormat.Gif)
                             ?? new ExportOptions { Format = ExportFormat.Gif, ExportFps = 12 };

        RefreshUi();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        RegisterHotkey();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        UnregisterHotkey();
        _hwndSource?.RemoveHook(WndProc);

        _currentSession?.Dispose();
        _captureSession.ClearFrames();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WmHotkey)
        {
            return IntPtr.Zero;
        }

        if (wParam.ToInt32() != ToggleHotkeyId)
        {
            return IntPtr.Zero;
        }

        handled = true;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_captureSession.IsRecording)
            {
                await StopRecordingAsync(autoOpenExportDialog: true);
            }
            else
            {
                await StartRecordingAsync();
            }
        });

        return IntPtr.Zero;
    }

    private void RegisterHotkey()
    {
        if (_hwndSource is null)
        {
            return;
        }

        UnregisterHotkey();

        var ok = Win32.RegisterHotKey(_hwndSource.Handle, ToggleHotkeyId, ToggleHotkeyModifiers, ToggleHotkeyVk);
        if (!ok)
        {
            var error = Marshal.GetLastWin32Error();
            StatusTextBlock.Text = $"Alt+G 快捷键注册失败（{error}）。请用“开始录制/停止录制”按钮操作。";
        }
        else
        {
            StatusTextBlock.Text = "准备就绪。点击“开始录制”开始。";
        }
    }

    private void UnregisterHotkey()
    {
        if (_hwndSource is null)
        {
            return;
        }

        Win32.UnregisterHotKey(_hwndSource.Handle, ToggleHotkeyId);
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartRecordingAsync();
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync(autoOpenExportDialog: true);
    }

    private async void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ExportCurrentSessionAsync(openDialog: true);
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            if (_captureSession.IsRecording)
            {
                return;
            }

            EnsureTargetReady();

            _captureSession.ClearFrames();

            var captureFps = Math.Clamp(_lastExportOptions.ExportFps, 1, 60);
            await _captureSession.StartAsync(GetCaptureBounds, captureFps);

            StatusTextBlock.Text = $"正在录制：{GetTargetSummary()}";

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ExportButton.IsEnabled = false;
            TrimButton.IsEnabled = false;
            ClearSessionButton.IsEnabled = false;

            ChooseTargetButton.IsEnabled = false;
            ModeComboBox.IsEnabled = false;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.ToString(), "无法开始录制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopRecordingAsync(bool autoOpenExportDialog)
    {
        if (!_captureSession.IsRecording)
        {
            return;
        }

        try
        {
            StopButton.IsEnabled = false;
            StatusTextBlock.Text = "正在停止录制并载入素材...";

            _currentSession?.Dispose();
            _currentSession = await _captureSession.StopAsync(GetTargetSummary());

            if (_currentSession is null || _currentSession.Frames.Count == 0)
            {
                throw new InvalidOperationException("本次录制没有采集到任何帧。请重新录制。");
            }

            SessionSummaryTextBlock.Text = BuildSessionSummary(_currentSession);

            ExportButton.IsEnabled = true;
            TrimButton.IsEnabled = true;
            ClearSessionButton.IsEnabled = true;

            StatusTextBlock.Text = "录制完成。";

            if (autoOpenExportDialog)
            {
                await ExportCurrentSessionAsync(openDialog: true);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "停止录制失败。";
            System.Windows.MessageBox.Show(this, ex.ToString(), "停止录制失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            ModeComboBox.IsEnabled = true;
            ChooseTargetButton.IsEnabled = _captureMode != CaptureMode.FullScreen;
        }
    }

    private async Task ExportCurrentSessionAsync(bool openDialog)
    {
        try
        {
            if (_currentSession is null)
            {
                throw new InvalidOperationException("当前没有可导出的素材，请先录制或导入 GIF。\n如果刚录完，会在停止后自动弹出导出设置。");
            }

            if (!openDialog)
            {
                throw new InvalidOperationException("当前版本仅支持通过导出弹窗导出。");
            }

            var dialog = new ExportDialog(_qualityPresets, _lastExportOptions)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedOptions is null || string.IsNullOrWhiteSpace(dialog.SelectedOutputPath))
            {
                StatusTextBlock.Text = "已取消导出。";
                return;
            }

            _lastExportOptions = dialog.SelectedOptions;
            var outputPath = dialog.SelectedOutputPath;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            ExportButton.IsEnabled = false;
            StatusTextBlock.Text = "正在导出，请稍候...";

            try
            {
                await _gifEncoderService.SaveAsync(_currentSession, outputPath, _lastExportOptions);
            }
            catch (Exception ex) when (ex is InvalidOperationException && ex.Message.Contains("已输出最小版本"))
            {
                // 当启用“按文件大小限制”且仍无法满足目标时：
                // SaveAsync 会用异常携带提示信息，但文件已经成功输出到目标路径。
                StatusTextBlock.Text = $"导出完成：{outputPath}";
                System.Windows.MessageBox.Show(this, ex.Message, "已导出（已尽量压缩）", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusTextBlock.Text = $"导出完成：{outputPath}";
            System.Windows.MessageBox.Show(this, $"已导出到：\n{outputPath}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "导出失败。";
            System.Windows.MessageBox.Show(this, ex.ToString(), "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportButton.IsEnabled = _currentSession is not null && !_captureSession.IsRecording;
        }
    }

    private void ClearSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        _currentSession?.Dispose();
        _currentSession = null;

        SessionSummaryTextBlock.Text = "暂无录制或导入的素材。";
        StatusTextBlock.Text = "准备就绪。点击“开始录制”开始。";

        ExportButton.IsEnabled = false;
        TrimButton.IsEnabled = false;
        ClearSessionButton.IsEnabled = false;
        ImportGifPathTextBox.Text = string.Empty;
    }

    private void ImportGifButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要导入裁剪的 GIF",
                Filter = "GIF 文件|*.gif",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _currentSession?.Dispose();
            _currentSession = _gifEncoderService.LoadGifAsSession(dialog.FileName);

            ImportGifPathTextBox.Text = dialog.FileName;
            SessionSummaryTextBlock.Text = BuildSessionSummary(_currentSession);

            StatusTextBlock.Text = "GIF 已载入。点击“导出当前素材”可弹出导出设置。";

            ExportButton.IsEnabled = true;
            TrimButton.IsEnabled = true;
            ClearSessionButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.ToString(), "导入 GIF 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenTrimButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentSession is null || _currentSession.Frames.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "当前没有可裁剪的素材。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var trim = new TrimWindow(_currentSession) { Owner = this };
            if (trim.ShowDialog() == true && trim.ResultSession is not null)
            {
                _currentSession.Dispose();
                _currentSession = trim.ResultSession;

                SessionSummaryTextBlock.Text = BuildSessionSummary(_currentSession);
                StatusTextBlock.Text = "已应用裁剪。";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.ToString(), "打开裁剪失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeComboBox is null || ModeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        _captureMode = tag switch
        {
            "Region" => CaptureMode.Region,
            "Window" => CaptureMode.Window,
            _ => CaptureMode.FullScreen
        };

        RefreshUi();
    }

    private void ChooseTargetButton_OnClick(object sender, RoutedEventArgs e)
    {
        switch (_captureMode)
        {
            case CaptureMode.Region:
                PickRegion();
                break;
            case CaptureMode.Window:
                PickWindow();
                break;
            default:
                _selectedRegion = null;
                _selectedWindow = null;
                break;
        }

        RefreshUi();
    }

    private void PickRegion()
    {
        Hide();
        try
        {
            var selector = new RegionSelectWindow();
            if (selector.ShowDialog() == true)
            {
                _selectedRegion = selector.SelectedBounds;
            }
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void PickWindow()
    {
        Hide();
        try
        {
            var picker = new WindowPickerOverlay();
            if (picker.ShowDialog() == true)
            {
                _selectedWindow = picker.SelectionResult;
            }
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void EnsureTargetReady()
    {
        if (_captureMode == CaptureMode.Region && _selectedRegion is null)
        {
            throw new InvalidOperationException("请先框选录制区域。");
        }

        if (_captureMode == CaptureMode.Window && _selectedWindow is null)
        {
            throw new InvalidOperationException("请先点选一个窗口作为录制目标。");
        }
    }

    private Rectangle GetCaptureBounds()
    {
        return _captureMode switch
        {
            CaptureMode.Region when _selectedRegion is not null => _selectedRegion.Value,
            CaptureMode.Window when _selectedWindow is not null => Win32.TryGetWindowBounds(_selectedWindow.Handle, out var bounds) ? bounds : _selectedWindow.Bounds,
            _ => Win32.GetVirtualScreenBounds()
        };
    }

    private void RefreshUi()
    {
        if (ChooseTargetButton is null || TargetSummaryTextBlock is null)
        {
            return;
        }

        ChooseTargetButton.IsEnabled = !_captureSession.IsRecording && _captureMode != CaptureMode.FullScreen;
        TargetSummaryTextBlock.Text = GetTargetSummary();

        if (_currentSession is null)
        {
            ExportButton.IsEnabled = false;
            TrimButton.IsEnabled = false;
            ClearSessionButton.IsEnabled = false;
        }
        else
        {
            ExportButton.IsEnabled = !_captureSession.IsRecording;
            TrimButton.IsEnabled = !_captureSession.IsRecording;
            ClearSessionButton.IsEnabled = !_captureSession.IsRecording;
        }
    }

    private string GetTargetSummary()
    {
        return _captureMode switch
        {
            CaptureMode.Region => _selectedRegion is null
                ? "尚未选择区域"
                : $"区域：{_selectedRegion.Value.Width} × {_selectedRegion.Value.Height}，位置 {_selectedRegion.Value.Left},{_selectedRegion.Value.Top}",
            CaptureMode.Window => _selectedWindow is null
                ? "尚未选择窗口"
                : $"窗口：{_selectedWindow.Title}（{_selectedWindow.Bounds.Width} × {_selectedWindow.Bounds.Height}）",
            _ => "全屏桌面"
        };
    }

    private static string BuildSessionSummary(RecordingSessionData session)
    {
        return $"来源：{session.SourceDescription}\n帧数：{session.Frames.Count}\n时长：{session.Duration.TotalSeconds:0.##} 秒\n尺寸：{session.FrameSize.Width} × {session.FrameSize.Height}\n标称 FPS：{session.NominalFps}";
    }
}
