using System.Drawing;
using System.Windows.Input;

namespace GifRecorder;

public enum CaptureMode
{
    FullScreen,
    Region,
    Window
}

public enum ExportFormat
{
    Gif,
    Avi,
    Mp4
}

public enum GifQuality
{
    Low,
    Medium,
    High
}

public sealed record GifQualityPreset(
    GifQuality Quality,
    string DisplayName,
    int ExportFps,
    int MaxWidth,
    int ColorCount,
    bool UseDither,
    bool OptimizeFrames,
    int AviJpegQuality)
{
    public int DelayCentiseconds => Math.Max(1, (int)Math.Round(100d / Math.Max(1, ExportFps)));

    public ExportOptions ToExportOptions(ExportFormat format)
    {
        return new ExportOptions
        {
            Format = format,
            ExportFps = ExportFps,
            MaxWidth = MaxWidth,
            ColorCount = ColorCount,
            UseDither = UseDither,
            OptimizeFrames = OptimizeFrames,
            AviJpegQuality = AviJpegQuality,
            EnableTrim = false,
            KeepRangesText = string.Empty,
            RemoveRangesText = string.Empty
        };
    }

    public override string ToString() => DisplayName;
}

public sealed record WindowSelectionResult(IntPtr Handle, Rectangle Bounds, string Title);

public sealed record HotkeyModifierOption(string DisplayName, uint Win32Modifiers)
{
    public override string ToString() => DisplayName;
}

public sealed record HotkeyKeyOption(string DisplayName, Key Key)
{
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString() => DisplayName;
}

public sealed record RecordedFrame(Bitmap Bitmap, TimeSpan Timestamp);

public sealed class RecordingSessionData : IDisposable
{
    public RecordingSessionData(IReadOnlyList<RecordedFrame> frames, TimeSpan duration, Size frameSize, int nominalFps, string sourceDescription)
    {
        Frames = frames;
        Duration = duration;
        FrameSize = frameSize;
        NominalFps = nominalFps;
        SourceDescription = sourceDescription;
    }

    public IReadOnlyList<RecordedFrame> Frames { get; }

    public TimeSpan Duration { get; }

    public Size FrameSize { get; }

    public int NominalFps { get; }

    public string SourceDescription { get; }

    public void Dispose()
    {
        foreach (var frame in Frames)
        {
            frame.Bitmap.Dispose();
        }
    }
}

public sealed record TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;

    public override string ToString() => $"{Start.TotalSeconds:0.##}-{End.TotalSeconds:0.##}";
}

public sealed class ExportOptions
{
    public ExportFormat Format { get; init; } = ExportFormat.Gif;

    public int ExportFps { get; init; } = 12;

    public int MaxWidth { get; init; } = 1280;

    public int ColorCount { get; init; } = 128;

    public bool UseDither { get; init; }

    public bool OptimizeFrames { get; init; } = true;

    public int AviJpegQuality { get; init; } = 80;

    public bool EnableTrim { get; init; }

    public string KeepRangesText { get; init; } = string.Empty;

    public string RemoveRangesText { get; init; } = string.Empty;

    public int DelayCentiseconds => Math.Max(1, (int)Math.Round(100d / Math.Max(1, ExportFps)));
}
