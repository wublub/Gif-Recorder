using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using ImageMagick;
using OpenCvSharp;
using SharpAvi;
using SharpAvi.Output;

namespace GifRecorder;

public sealed class GifEncoderService
{
    public async Task SaveAsync(RecordingSessionData session, string outputPath, ExportOptions options, CancellationToken cancellationToken = default)
    {
        if (session.Frames.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的帧，请先开始录制。");
        }

        var frames = BuildFramesToExport(session, options);
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("裁剪后没有可导出的内容，请调整保留/删除区间。");
        }

        await Task.Run(() =>
        {
            switch (options.Format)
            {
                case ExportFormat.Avi:
                    SaveAvi(frames, outputPath, options, cancellationToken);
                    break;
                case ExportFormat.Mp4:
                    SaveMp4(frames, outputPath, options, cancellationToken);
                    break;
                default:
                    if (options.IsGifSizeLimitEnabled)
                    {
                        SaveGifWithSizeLimit(frames, outputPath, options, cancellationToken);
                    }
                    else
                    {
                        SaveGif(frames, outputPath, options, cancellationToken);
                    }

                    break;
            }
        }, cancellationToken);
    }

    public RecordingSessionData LoadGifAsSession(string filePath)
    {
        using var collection = new MagickImageCollection(filePath);
        if (collection.Count == 0)
        {
            throw new InvalidOperationException("导入的 GIF 中没有帧。");
        }

        var frames = new List<RecordedFrame>(collection.Count);
        var elapsed = TimeSpan.Zero;

        foreach (var image in collection)
        {
            using var frameStream = new MemoryStream();
            using var exportImage = image.Clone();
            exportImage.Format = MagickFormat.Png;
            exportImage.Write(frameStream);
            frameStream.Position = 0;
            using var temp = new Bitmap(frameStream);
            frames.Add(new RecordedFrame(new Bitmap(temp), elapsed));
            var delayCs = Math.Max(1, (int)image.AnimationDelay);
            elapsed += TimeSpan.FromMilliseconds(delayCs * 10d);
        }

        var size = new System.Drawing.Size(frames[0].Bitmap.Width, frames[0].Bitmap.Height);
        return new RecordingSessionData(frames, elapsed, size, GuessFps((int)collection[0].AnimationDelay), Path.GetFileName(filePath));
    }

    private static int GuessFps(int delay)
    {
        delay = Math.Max(1, delay);
        return Math.Max(1, (int)Math.Round(100d / delay));
    }

    private static List<RecordedFrame> BuildFramesToExport(RecordingSessionData session, ExportOptions options)
    {
        var frames = session.Frames
            .Select(frame => new RecordedFrame((Bitmap)frame.Bitmap.Clone(), frame.Timestamp))
            .ToList();

        if (!options.EnableTrim)
        {
            return frames;
        }

        var keepRanges = ParseRanges(options.KeepRangesText, session.Duration);
        var removeRanges = ParseRanges(options.RemoveRangesText, session.Duration);

        if (keepRanges.Count > 0)
        {
            frames = frames
                .Where(frame => keepRanges.Any(range => IsInside(frame.Timestamp, range)))
                .ToList();
        }

        if (removeRanges.Count > 0)
        {
            frames = frames
                .Where(frame => removeRanges.All(range => !IsInside(frame.Timestamp, range)))
                .ToList();
        }

        return frames;
    }

    private static bool IsInside(TimeSpan value, TimeRange range)
    {
        return value >= range.Start && value <= range.End;
    }

    public static IReadOnlyList<TimeRange> ParseRanges(string input, TimeSpan totalDuration)
    {
        var result = new List<TimeRange>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var segments = input.Split(new[] { ';', '；', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var pieces = segment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pieces.Length != 2)
            {
                throw new InvalidOperationException($"区间格式错误：{segment}。请使用 1.2-3.5 这样的格式。");
            }

            var start = ParseSecond(pieces[0]);
            var end = ParseSecond(pieces[1]);
            if (start < TimeSpan.Zero || end < TimeSpan.Zero || end <= start)
            {
                throw new InvalidOperationException($"区间无效：{segment}。");
            }

            if (start > totalDuration)
            {
                continue;
            }

            if (end > totalDuration)
            {
                end = totalDuration;
            }

            result.Add(new TimeRange(start, end));
        }

        return result;
    }

    private static TimeSpan ParseSecond(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var second) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out second))
        {
            throw new InvalidOperationException($"无法解析秒数：{text}。");
        }

        return TimeSpan.FromSeconds(second);
    }

    private static void SaveGifWithSizeLimit(IReadOnlyList<RecordedFrame> frames, string outputPath, ExportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var maxBytes = options.MaxFileSizeBytes;
            if (maxBytes <= 0)
            {
                SaveGif(frames, outputPath, options, cancellationToken);
                return;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Environment.CurrentDirectory;
            }

            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            var attemptPath = Path.Combine(outputDirectory, baseName + ".tmp" + Path.GetExtension(outputPath));

            Exception? lastError = null;
            ExportOptions? smallestOptions = null;
            string? smallestPath = null;
            long smallestBytes = long.MaxValue;

            foreach (var attemptOptions in BuildGifSizeLimitAttempts(options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(attemptPath))
                    {
                        File.Delete(attemptPath);
                    }

                    SaveGif(frames, attemptPath, attemptOptions, cancellationToken);

                    var info = new FileInfo(attemptPath);
                    var bytes = info.Exists ? info.Length : long.MaxValue;

                    if (bytes < smallestBytes)
                    {
                        smallestBytes = bytes;
                        smallestOptions = attemptOptions;
                        smallestPath = attemptPath;
                    }

                    if (bytes <= maxBytes)
                    {
                        ReplaceFile(attemptPath, outputPath);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (smallestPath is not null && File.Exists(smallestPath) && smallestOptions is not null)
            {
                if (options.SizeLimitExceededBehavior == SizeLimitExceededBehavior.ExportSmallestWithWarning)
                {
                    ReplaceFile(smallestPath, outputPath);

                    var targetMb = options.MaxFileSizeMb;
                    var actualMb = smallestBytes / (1024d * 1024d);
                    throw new InvalidOperationException($"已尝试自动压缩，但仍无法将文件控制在 {targetMb}MB 内。\n已输出最小版本（约 {actualMb:0.##}MB）。");
                }

                throw new InvalidOperationException($"已尝试自动压缩，但仍无法将文件控制在 {options.MaxFileSizeMb}MB 内。", lastError);
            }

            throw new InvalidOperationException("导出失败：无法生成任何有效的 GIF。", lastError);
        }
        finally
        {
            DisposeFrames(frames);
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(sourcePath, destinationPath);
    }

    private static IEnumerable<ExportOptions> BuildGifSizeLimitAttempts(ExportOptions initial)
    {
        // 这里返回一组“逐步变小”的导出参数。
        // 注意：不要修改 initial 本身，避免污染用户选择。

        static ExportOptions With(
            ExportOptions o,
            int? exportFps = null,
            int? maxWidth = null,
            int? colorCount = null,
            bool? useDither = null,
            bool? optimizeFrames = null)
        {
            return new ExportOptions
            {
                Format = o.Format,
                ExportFps = exportFps ?? o.ExportFps,
                MaxWidth = maxWidth ?? o.MaxWidth,
                ColorCount = colorCount ?? o.ColorCount,
                UseDither = useDither ?? o.UseDither,
                OptimizeFrames = optimizeFrames ?? o.OptimizeFrames,
                AviJpegQuality = o.AviJpegQuality,
                EnableMaxFileSize = o.EnableMaxFileSize,
                MaxFileSizeMb = o.MaxFileSizeMb,
                FileSizeAdjustmentStrategy = o.FileSizeAdjustmentStrategy,
                SizeLimitExceededBehavior = o.SizeLimitExceededBehavior,
                EnableTrim = o.EnableTrim,
                KeepRangesText = o.KeepRangesText,
                RemoveRangesText = o.RemoveRangesText
            };
        }

        var fps0 = Math.Clamp(initial.ExportFps, 1, 60);
        var width0 = Math.Max(0, initial.MaxWidth);
        var colors0 = Math.Clamp(initial.ColorCount, 2, 256);

        // 统一先尝试原始参数
        yield return With(initial);

        var strategy = initial.FileSizeAdjustmentStrategy;

        // 一组候选值（从“略降”到“很小”）
        var widthCandidates = new[]
        {
            width0,
            ClampNonZero(width0, 1600),
            ClampNonZero(width0, 1280),
            ClampNonZero(width0, 960),
            ClampNonZero(width0, 720),
            ClampNonZero(width0, 540)
        }.Distinct().ToArray();

        var colorCandidates = new[]
        {
            colors0,
            Math.Min(colors0, 128),
            Math.Min(colors0, 96),
            Math.Min(colors0, 64),
            Math.Min(colors0, 48),
            Math.Min(colors0, 32)
        }.Distinct().Select(v => Math.Clamp(v, 2, 256)).ToArray();

        var fpsCandidates = new[]
        {
            fps0,
            Math.Min(fps0, 15),
            Math.Min(fps0, 12),
            Math.Min(fps0, 10),
            Math.Min(fps0, 8),
            Math.Min(fps0, 6)
        }.Distinct().Select(v => Math.Clamp(v, 1, 60)).ToArray();

        // 组合顺序按策略决定（简单实现：逐步固定一个维度再推进下一个）
        switch (strategy)
        {
            case FileSizeAdjustmentStrategy.PreferQuality:
                foreach (var cc in colorCandidates)
                {
                    yield return With(initial, colorCount: cc);
                }

                if (initial.UseDither)
                {
                    yield return With(initial, colorCount: Math.Min(colors0, 64), useDither: false);
                }

                foreach (var fps in fpsCandidates)
                {
                    yield return With(initial, exportFps: fps);
                }

                foreach (var w in widthCandidates)
                {
                    yield return With(initial, maxWidth: w);
                }

                break;

            case FileSizeAdjustmentStrategy.PreferSmoothness:
                foreach (var cc in colorCandidates)
                {
                    yield return With(initial, colorCount: cc);
                }

                if (initial.UseDither)
                {
                    yield return With(initial, colorCount: Math.Min(colors0, 64), useDither: false);
                }

                foreach (var w in widthCandidates)
                {
                    yield return With(initial, maxWidth: w);
                }

                // 最后才降 fps
                foreach (var fps in fpsCandidates)
                {
                    yield return With(initial, exportFps: fps);
                }

                break;

            default:
                // Balanced
                foreach (var cc in colorCandidates)
                {
                    yield return With(initial, colorCount: cc);
                }

                if (initial.UseDither)
                {
                    yield return With(initial, colorCount: Math.Min(colors0, 64), useDither: false);
                }

                foreach (var w in widthCandidates)
                {
                    yield return With(initial, maxWidth: w);
                }

                foreach (var fps in fpsCandidates)
                {
                    yield return With(initial, exportFps: fps);
                }

                break;
        }

        // 最极限兜底：尽量小
        yield return With(initial,
            exportFps: Math.Min(fps0, 6),
            maxWidth: ClampNonZero(width0, 540),
            colorCount: 32,
            useDither: false,
            optimizeFrames: true);

        static int ClampNonZero(int original, int max)
        {
            if (original <= 0)
            {
                return 0;
            }

            return Math.Min(original, max);
        }
    }

    private static void SaveGif(IReadOnlyList<RecordedFrame> frames, string outputPath, ExportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var collection = new MagickImageCollection();

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = new MemoryStream();
                frame.Bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                using var magickImage = new MagickImage(stream);
                ResizeIfNeeded(magickImage, (uint)options.MaxWidth);
                magickImage.Format = MagickFormat.Gif;
                magickImage.AnimationDelay = (uint)options.DelayCentiseconds;
                collection.Add(magickImage.Clone());
            }

            foreach (var image in collection)
            {
                image.GifDisposeMethod = GifDisposeMethod.Background;
            }

            Quantize(collection, options.ColorCount, options.UseDither);
            if (options.OptimizeFrames)
            {
                collection.OptimizePlus();
            }

            collection.Write(outputPath);
        }
        finally
        {
            DisposeFrames(frames);
        }
    }

    private static void SaveAvi(IReadOnlyList<RecordedFrame> frames, string outputPath, ExportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var firstNormalized = NormalizeFrame(frames[0].Bitmap, options.MaxWidth);
            using var writer = new AviWriter(outputPath)
            {
                FramesPerSecond = Math.Max(1, options.ExportFps),
                EmitIndex1 = true
            };

            var stream = writer.AddVideoStream(firstNormalized.Width, firstNormalized.Height, BitsPerPixel.Bpp24);
            stream.Codec = CodecIds.MotionJpeg;

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var normalized = NormalizeFrame(frame.Bitmap, options.MaxWidth);
                var jpegBytes = EncodeJpeg(normalized, options.AviJpegQuality);
                stream.WriteFrame(true, jpegBytes, 0, jpegBytes.Length);
            }
        }
        finally
        {
            DisposeFrames(frames);
        }
    }

    private static void SaveMp4(IReadOnlyList<RecordedFrame> frames, string outputPath, ExportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var firstNormalized = NormalizeFrame(frames[0].Bitmap, options.MaxWidth);
            var width = firstNormalized.Width;
            var height = firstNormalized.Height;

            // OpenCV 的部分编码器要求尺寸为偶数
            if (width % 2 != 0) width -= 1;
            if (height % 2 != 0) height -= 1;
            width = Math.Max(2, width);
            height = Math.Max(2, height);

            var fps = Math.Max(1, options.ExportFps);

            // 依次尝试常见 FourCC，提升可用性
            var candidates = new[]
            {
                VideoWriter.FourCC('H','2','6','4'),
                VideoWriter.FourCC('a','v','c','1'),
                VideoWriter.FourCC('m','p','4','v')
            };

            Exception? lastError = null;
            foreach (var fourcc in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var writer = new VideoWriter(outputPath, fourcc, fps, new OpenCvSharp.Size(width, height));
                    if (!writer.IsOpened())
                    {
                        continue;
                    }

                    foreach (var frame in frames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var normalized = NormalizeFrame(frame.Bitmap, options.MaxWidth);
                        using var mat = BitmapToBgrMat(normalized, width, height);
                        writer.Write(mat);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException("MP4 导出失败：当前系统缺少可用的视频编码器（H264/avc1/mp4v）。", lastError);
        }
        finally
        {
            DisposeFrames(frames);
        }
    }

    private static Mat BitmapToBgrMat(Bitmap bitmap, int targetWidth, int targetHeight)
    {
        // 统一转换为 BGR24，避免编码器不兼容
        using var normalized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(normalized))
        {
            g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
        }

        var rect = new Rectangle(0, 0, normalized.Width, normalized.Height);
        var data = normalized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var bytes = stride * data.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);

            // 用 FromPixelData 构造 Mat（带 stride），然后 Clone 成紧凑 Mat
            using var wrapped = Mat.FromPixelData(data.Height, data.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
    {
        using var stream = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        bitmap.Save(stream, jpegEncoder, parameters);
        return stream.ToArray();
    }

    private static Bitmap NormalizeFrame(Bitmap original, int maxWidth)
    {
        if (maxWidth > 0 && original.Width > maxWidth)
        {
            var newHeight = (int)Math.Round(original.Height * (maxWidth / (double)original.Width));
            var resized = new Bitmap(maxWidth, newHeight, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(resized);
            graphics.DrawImage(original, 0, 0, maxWidth, newHeight);
            return resized;
        }

        var normalized = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(normalized);
        g.DrawImage(original, 0, 0, original.Width, original.Height);
        return normalized;
    }

    private static void ResizeIfNeeded(MagickImage image, uint maxWidth)
    {
        if (maxWidth == 0 || image.Width <= maxWidth)
        {
            return;
        }

        var height = (uint)Math.Round(image.Height * (maxWidth / (double)image.Width));
        image.Resize(maxWidth, height);
    }

    private static void Quantize(MagickImageCollection collection, int colorCount, bool useDither)
    {
        var settings = new QuantizeSettings
        {
            Colors = (uint)Math.Clamp(colorCount, 2, 256),
            DitherMethod = useDither ? DitherMethod.Riemersma : DitherMethod.No
        };

        collection.Quantize(settings);
    }

    private static void DisposeFrames(IEnumerable<RecordedFrame> frames)
    {
        foreach (var frame in frames)
        {
            frame.Bitmap.Dispose();
        }
    }
}
