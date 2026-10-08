using System.IO;
using System.Diagnostics;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class CameraFrameEventArgs : EventArgs
{
    public CameraFrameEventArgs(IntPtr data, int width, int height, int stride, int bufferSize)
    {
        Data = data;
        Width = width;
        Height = height;
        Stride = stride;
        BufferSize = bufferSize;
    }

    public IntPtr Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int BufferSize { get; }
}

public sealed class CameraService : IDisposable
{
    private readonly object _gate = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Mat? _latestFrame;
    private volatile bool _mirror = true;
    private bool _disposed;

    public event EventHandler<CameraFrameEventArgs>? FrameReady;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Error;

    public Action<Mat>? FrameProcessor { get; set; }

    public bool IsRunning => _captureTask is { IsCompleted: false };

    public bool Mirror
    {
        get => _mirror;
        set => _mirror = value;
    }

    public Task<IReadOnlyList<CameraDevice>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CameraDevice>>(() =>
        {
            var found = new List<CameraDevice>();
            AppLog.Camera("Enumerating cameras using OpenCV backends.");

            for (var index = 0; index < 8 && !cancellationToken.IsCancellationRequested; index++)
            {
                if (CanOpen(index, VideoCaptureAPIs.DSHOW) || CanOpen(index, VideoCaptureAPIs.MSMF))
                {
                    found.Add(new CameraDevice(index, $"Camera {index + 1}"));
                    AppLog.Camera($"Camera index {index} detected.");
                }
            }

            if (found.Count == 0)
                AppLog.Camera("No camera indexes opened during enumeration.");

            return found;
        }, cancellationToken);
    }

    private static bool CanOpen(int index, VideoCaptureAPIs backend)
    {
        try
        {
            using var probe = new VideoCapture(index, backend);
            return probe.IsOpened();
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Probe index {index} via {backend} failed: {ex.Message}");
            return false;
        }
    }

    public async Task StartAsync(int index)
    {
        ThrowIfDisposed();
        await StopAsync();

        StatusChanged?.Invoke(this, "Opening camera…");
        AppLog.Camera($"Start requested for camera index {index}.");

        Exception? lastError = null;
        foreach (var backend in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
        {
            try
            {
                var capture = new VideoCapture(index, backend);
                if (!capture.IsOpened())
                {
                    capture.Dispose();
                    AppLog.Camera($"Index {index} did not open via {backend}.");
                    continue;
                }

                capture.Set(VideoCaptureProperties.FrameWidth, 1280);
                capture.Set(VideoCaptureProperties.FrameHeight, 720);
                capture.Set(VideoCaptureProperties.Fps, 30);
                capture.Set(VideoCaptureProperties.BufferSize, 1);

                using var test = new Mat();
                if (!capture.Read(test) || test.Empty())
                {
                    AppLog.Camera($"Index {index} opened via {backend}, but first frame failed.");
                    capture.Release();
                    capture.Dispose();
                    continue;
                }

                lock (_gate)
                {
                    _capture = capture;
                    _latestFrame?.Dispose();
                    _latestFrame = test.Clone();
                }

                var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                var fps = capture.Get(VideoCaptureProperties.Fps);
                AppLog.Camera($"Camera {index} running via {backend} at {width}x{height} {fps:0.##}fps.");

                _cts = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(capture, backend, _cts.Token));
                StatusChanged?.Invoke(this, $"Camera live • {width}×{height} • {backend}");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppLog.Camera($"Opening index {index} via {backend} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        var message = lastError is null
            ? "Windows found the camera entry, but none of the native capture backends could read a frame from it."
            : $"Camera could not start: {lastError.Message}";
        Error?.Invoke(this, message);
        StatusChanged?.Invoke(this, "Camera unavailable");
        throw new InvalidOperationException(message, lastError);
    }

    private void CaptureLoop(VideoCapture capture, VideoCaptureAPIs backend, CancellationToken token)
    {
        using var frame = new Mat();
        using var bgra = new Mat();
        var failures = 0;
        var stopwatch = Stopwatch.StartNew();
        long nextFrameAt = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
                    failures++;
                    if (failures >= 12)
                    {
                        var message = $"Camera stopped returning frames via {backend}.";
                        AppLog.Camera(message);
                        Error?.Invoke(this, message);
                        StatusChanged?.Invoke(this, "Camera stream stopped");
                        break;
                    }
                    Thread.Sleep(25);
                    continue;
                }

                failures = 0;

                var now = stopwatch.ElapsedMilliseconds;
                if (now < nextFrameAt)
                    continue;
                nextFrameAt = now + 33;

                if (_mirror)
                    Cv2.Flip(frame, frame, FlipMode.Y);

                try
                {
                    FrameProcessor?.Invoke(frame);
                }
                catch (Exception ex)
                {
                    AppLog.Camera($"Frame processor error: {ex.Message}");
                }

                lock (_gate)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                }

                Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
                var stride = checked((int)bgra.Step());
                var bufferSize = checked(stride * bgra.Rows);
                FrameReady?.Invoke(this, new CameraFrameEventArgs(bgra.Data, bgra.Cols, bgra.Rows, stride, bufferSize));
            }
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Capture loop crashed: {ex}");
            Error?.Invoke(this, $"Camera stream failed: {ex.Message}");
            StatusChanged?.Invoke(this, "Camera stream failed");
        }
    }

    public string SaveSnapshot()
        => SaveSnapshotTo(AppPaths.Outputs, "Kassie-Makeover");

    public string SaveSnapshotTo(string directory, string prefix)
    {
        ThrowIfDisposed();
        Mat? snapshot;
        lock (_gate)
            snapshot = _latestFrame?.Clone();

        if (snapshot is null || snapshot.Empty())
            throw new InvalidOperationException("There is no camera frame to capture yet.");

        using (snapshot)
        {
            Directory.CreateDirectory(directory);
            var safePrefix = string.Concat(prefix.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
            var path = Path.Combine(directory, $"{safePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
            if (!Cv2.ImWrite(path, snapshot))
                throw new IOException("The snapshot could not be written to disk.");
            AppLog.Camera($"Snapshot saved: {path}");
            return path;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        VideoCapture? capture;

        lock (_gate)
        {
            cts = _cts;
            task = _captureTask;
            capture = _capture;
            _cts = null;
            _captureTask = null;
            _capture = null;
        }

        try { cts?.Cancel(); } catch { }
        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }

        try { capture?.Release(); } catch { }
        try { capture?.Dispose(); } catch { }
        try { cts?.Dispose(); } catch { }

        lock (_gate)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        StatusChanged?.Invoke(this, "Camera off");
        AppLog.Camera("Camera stopped and released.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
