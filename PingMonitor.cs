using System.Net.NetworkInformation;

namespace NetworkTracker;

internal enum PingOutcome
{
    Pending,
    Ok,
    Timeout,
    Error
}

internal readonly record struct HostPing(string Label, PingOutcome Outcome, long RoundtripMilliseconds)
{
    public static HostPing Pending(string label) => new(label, PingOutcome.Pending, 0);

    public static HostPing Ok(string label, long roundtripMilliseconds) => new(label, PingOutcome.Ok, roundtripMilliseconds);

    public static HostPing Failed(string label, PingOutcome outcome) => new(label, outcome, 0);
}

internal sealed class PingMonitor : IDisposable
{
    private readonly (string Label, string Host)[] _hosts =
    [
        ("Google", "google.com"),
        ("Baidu", "baidu.com")
    ];

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private int _started;
    private volatile bool _enabled = true;
    private volatile bool _disposed;

    public event EventHandler<IReadOnlyList<HostPing>>? Updated;

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
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
            await ProbeAll(cancellationToken);
            while (_timer != null && await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed)
                    return;
                await ProbeAll(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task ProbeAll(CancellationToken cancellationToken)
    {
        if (!_enabled || _disposed)
            return;

        HostPing[] results;
        try
        {
            results = await Task.WhenAll(_hosts.Select(host => Probe(host.Label, host.Host, cancellationToken)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return;
        }

        if (_disposed)
            return;

        try
        {
            Updated?.Invoke(this, results);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    // Light reachability checks. These do not measure throughput.
    private static async Task<HostPing> Probe(string label, string host, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(1500), new byte[32], options: null, cancellationToken);
            if (reply.Status == IPStatus.Success)
                return HostPing.Ok(label, reply.RoundtripTime);

            return HostPing.Failed(label, reply.Status == IPStatus.TimedOut ? PingOutcome.Timeout : PingOutcome.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return HostPing.Failed(label, PingOutcome.Error);
        }
    }
}
