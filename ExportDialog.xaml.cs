using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GifRecorder;

public partial class ExportDialog : Window
{
    private readonly IReadOnlyList<GifQualityPreset> _presets;
    private readonly ExportOptions _initialOptions;

    public ExportFormat SelectedFormat { get; private set; } = ExportFormat.Gif;

    public ExportOptions? SelectedOptions { get; private set; }

    public string? SelectedOutputPath { get; private set; }

    public ExportDialog(IReadOnlyList<GifQualityPreset> presets, ExportOptions initialOptions)
    {
        InitializeComponent();
        _presets = presets;
        _initialOptions = initialOptions;

        // 初始化过程中会触发 SelectionChanged，
        // 这里按顺序先填充 ItemsSource，再设置选中项，最后刷新 UI。
        PresetComboBox.ItemsSource = _presets;
        PresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Quality == initialOptions.FormatPresetQuality())
                                      ?? _presets.ElementAtOrDefault(1)
                                      ?? _presets.First();

        SetFormat(initialOptions.Format);
        ApplyPresetToFields((GifQualityPreset?)PresetComboBox.SelectedItem);

        ExportFpsTextBox.Text = Math.Max(1, initialOptions.ExportFps).ToString();
        MaxWidthTextBox.Text = Math.Max(0, initialOptions.MaxWidth).ToString();
        ColorCountTextBox.Text = Math.Clamp(initialOptions.ColorCount, 2, 256).ToString();
        UseDitherCheckBox.IsChecked = initialOptions.UseDither;
        OptimizeFramesCheckBox.IsChecked = initialOptions.OptimizeFrames;
        AviQualityTextBox.Text = Math.Clamp(initialOptions.AviJpegQuality, 1, 100).ToString();

        RefreshFormatUi();
    }

    private void FormatComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedFormat = GetSelectedFormat();
        RefreshFormatUi();
    }

    private void PresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is GifQualityPreset preset)
        {
            ApplyPresetToFields(preset);
        }
    }

    private void ApplyPresetToFields(GifQualityPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        ExportFpsTextBox.Text = preset.ExportFps.ToString();
        MaxWidthTextBox.Text = preset.MaxWidth.ToString();
        ColorCountTextBox.Text = preset.ColorCount.ToString();
        UseDitherCheckBox.IsChecked = preset.UseDither;
        OptimizeFramesCheckBox.IsChecked = preset.OptimizeFrames;
        AviQualityTextBox.Text = preset.AviJpegQuality.ToString();
    }

    private void RefreshFormatUi()
    {
        // InitializeComponent 过程中会触发 SelectionChanged，
        // 此时部分控件可能尚未完成字段绑定，需判空避免空引用。
        if (FormatComboBox is null
            || ColorCountLabel is null
            || ColorCountTextBox is null
            || GifOptionsPanel is null
            || AviOptionsPanel is null)
        {
            return;
        }

        var format = GetSelectedFormat();

        var isGif = format == ExportFormat.Gif;
        var isAvi = format == ExportFormat.Avi;

        ColorCountLabel.Visibility = isGif ? Visibility.Visible : Visibility.Collapsed;
        ColorCountTextBox.Visibility = isGif ? Visibility.Visible : Visibility.Collapsed;
        GifOptionsPanel.Visibility = isGif ? Visibility.Visible : Visibility.Collapsed;

        AviOptionsPanel.Visibility = isAvi ? Visibility.Visible : Visibility.Collapsed;
    }

    private ExportFormat GetSelectedFormat()
    {
        // 初始化阶段可能还未完成字段绑定，FormatComboBox 可能为 null
        if (FormatComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "Avi" => ExportFormat.Avi,
                "Mp4" => ExportFormat.Mp4,
                _ => ExportFormat.Gif
            };
        }

        return ExportFormat.Gif;
    }

    private void SetFormat(ExportFormat format)
    {
        foreach (var obj in FormatComboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag)
            {
                var itemFormat = tag switch
                {
                    "Avi" => ExportFormat.Avi,
                    "Mp4" => ExportFormat.Mp4,
                    _ => ExportFormat.Gif
                };

                if (itemFormat == format)
                {
                    FormatComboBox.SelectedItem = item;
                    SelectedFormat = format;
                    return;
                }
            }
        }

        FormatComboBox.SelectedIndex = 0;
        SelectedFormat = ExportFormat.Gif;
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = new ExportOptions
            {
                Format = GetSelectedFormat(),
                ExportFps = ParseInt(ExportFpsTextBox.Text, 1, 60, "FPS"),
                MaxWidth = ParseInt(MaxWidthTextBox.Text, 0, 7680, "最大宽度"),
                ColorCount = ParseInt(ColorCountTextBox.Text, 2, 256, "颜色数"),
                UseDither = UseDitherCheckBox.IsChecked == true,
                OptimizeFrames = OptimizeFramesCheckBox.IsChecked == true,
                AviJpegQuality = ParseInt(AviQualityTextBox.Text, 1, 100, "AVI 质量"),
                EnableTrim = false,
                KeepRangesText = string.Empty,
                RemoveRangesText = string.Empty
            };

            var outputPath = ChooseOutputPath(options.Format);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            SelectedFormat = options.Format;
            SelectedOptions = options;
            SelectedOutputPath = outputPath;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "导出设置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? ChooseOutputPath(ExportFormat format)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var baseName = $"gif-record-{DateTime.Now:yyyyMMdd-HHmmss}";

        var (filter, ext) = format switch
        {
            ExportFormat.Avi => ("AVI 文件|*.avi", ".avi"),
            ExportFormat.Mp4 => ("MP4 文件|*.mp4", ".mp4"),
            _ => ("GIF 文件|*.gif", ".gif")
        };

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择导出文件",
            Filter = filter,
            DefaultExt = ext,
            AddExtension = true,
            InitialDirectory = desktop,
            FileName = baseName + ext
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static int ParseInt(string text, int min, int max, string fieldName)
    {
        if (!int.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"{fieldName} 请输入整数。");
        }

        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{fieldName} 必须在 {min} 到 {max} 之间。");
        }

        return value;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

internal static class ExportOptionsExtensions
{
    public static GifQuality FormatPresetQuality(this ExportOptions options)
    {
        // 旧逻辑里主界面有 Low/Medium/High 预设，这里给一个尽量合理的回推
        // 仅用于弹窗默认选中，不影响实际导出参数。
        if (options.ColorCount <= 64 || options.ExportFps <= 8)
        {
            return GifQuality.Low;
        }

        if (options.ColorCount >= 256 || options.ExportFps >= 15)
        {
            return GifQuality.High;
        }

        return GifQuality.Medium;
    }
}
