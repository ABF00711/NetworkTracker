using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetworkTracker;

internal readonly record struct ThroughputPoint(double DownBytesPerSecond, double UpBytesPerSecond, string AdapterName)
{
    public static ThroughputPoint Empty { get; } = new(0, 0, "");
}

internal sealed class BandwidthMonitor : IDisposable
{
    private readonly object _gate = new();
    private string? _preferredAdapterId;
    private long _lastIn;
    private long _lastOut;
    private long _lastStamp;
    private bool _hasBaseline;
    private string? _baselineAdapterId;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private int _started;
    private volatile bool _disposed;

    public event EventHandler<ThroughputPoint>? Sampled;

    public void SetPreferredAdapter(string? adapterId)
    {
        lock (_gate)
        {
            _preferredAdapterId = adapterId;
            _hasBaseline = false;
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _timer?.Dispose();
    }

    private async Task Loop(CancellationToken cancellationToken)
    {
        try
        {
            Read();
            while (_timer != null && await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed)
                    return;

                try
                {
                    if (Read() is ThroughputPoint sample)
                        Sampled?.Invoke(this, sample);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write(ex);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private ThroughputPoint? Read()
    {
        string? preferred;
        lock (_gate)
            preferred = _preferredAdapterId;

        NetworkInterface? adapter = AdapterCatalog.Resolve(preferred);
        if (adapter == null)
        {
            lock (_gate)
                _hasBaseline = false;
            return ThroughputPoint.Empty;
        }

        long bytesIn;
        long bytesOut;
        try
        {
            IPInterfaceStatistics stats = adapter.GetIPStatistics();
            bytesIn = stats.BytesReceived;
            bytesOut = stats.BytesSent;
        }
        catch (NetworkInformationException)
        {
            lock (_gate)
                _hasBaseline = false;
            return new ThroughputPoint(0, 0, adapter.Name);
        }

        long stamp = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (!_hasBaseline || !string.Equals(_baselineAdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase))
            {
                _lastIn = bytesIn;
                _lastOut = bytesOut;
                _lastStamp = stamp;
                _baselineAdapterId = adapter.Id;
                _hasBaseline = true;
                return null;
            }

            double seconds = (stamp - _lastStamp) / (double)Stopwatch.Frequency;
            long downBytes = bytesIn - _lastIn;
            long upBytes = bytesOut - _lastOut;
            _lastIn = bytesIn;
            _lastOut = bytesOut;
            _lastStamp = stamp;

            if (seconds < 0.2 || downBytes < 0 || upBytes < 0)
                return new ThroughputPoint(0, 0, adapter.Name);

            double down = downBytes / seconds;
            double up = upBytes / seconds;
            const double maxBytesPerSecond = 100d * 1_000_000_000d / 8d;
            if (down > maxBytesPerSecond || up > maxBytesPerSecond)
                return new ThroughputPoint(0, 0, adapter.Name);

            return new ThroughputPoint(down, up, adapter.Name);
        }
    }
}
