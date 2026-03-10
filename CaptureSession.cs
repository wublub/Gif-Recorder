using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace GifRecorder;

public sealed class CaptureSession
{
    private readonly List<RecordedFrame> _frames = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Stopwatch? _stopwatch;
    private int _nominalFps;

    public bool IsRecording => _cts is not null;

    public Task StartAsync(Func<Rectangle> boundsProvider, int fps, CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("当前已经在录制中。");
        }

        _nominalFps = fps;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stopwatch = Stopwatch.StartNew();
        var token = _cts.Token;
        var frameDelay = TimeSpan.FromMilliseconds(Math.Max(30, 1000d / fps));

        _captureTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var bounds = boundsProvider();
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    var bitmap = Capture(bounds);
                    var timestamp = _stopwatch?.Elapsed ?? TimeSpan.Zero;
                    lock (_sync)
                    {
                        _frames.Add(new RecordedFrame(bitmap, timestamp));
                    }
                }

                try
                {
                    await Task.Delay(frameDelay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);

        return Task.CompletedTask;
    }

    public async Task<RecordingSessionData?> StopAsync(string sourceDescription)
    {
        if (_cts is null)
        {
            return null;
        }

        _cts.Cancel();

        if (_captureTask is not null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        _stopwatch?.Stop();
        _stopwatch = null;

        _cts.Dispose();
        _cts = null;
        _captureTask = null;

        lock (_sync)
        {
            var clonedFrames = _frames
                .Select(frame => new RecordedFrame((Bitmap)frame.Bitmap.Clone(), frame.Timestamp))
                .ToArray();

            var size = clonedFrames.Length > 0
                ? new Size(clonedFrames[0].Bitmap.Width, clonedFrames[0].Bitmap.Height)
                : Size.Empty;

            return new RecordingSessionData(clonedFrames, duration, size, _nominalFps, sourceDescription);
        }
    }

    public void ClearFrames()
    {
        lock (_sync)
        {
            foreach (var frame in _frames)
            {
                frame.Bitmap.Dispose();
            }

            _frames.Clear();
        }
    }

    private static Bitmap Capture(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }
}
