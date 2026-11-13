using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;

namespace ControllerHub.Infrastructure.Services;

public class SessionTrackerService : IDisposable
{
    private readonly IDeviceWatcherService _deviceWatcher;
    private readonly ISessionRepository _sessionRepository;
    private readonly IBatteryRepository _batteryRepository;
    private readonly object _syncLock = new();
    private readonly Dictionary<string, ControllerSession> _activeSessions = new();
    private CancellationTokenSource? _tickerCts;
    private bool _disposed;

    public event EventHandler<TimeSpan>? ActiveSessionTicked;
    public event EventHandler<ControllerSession>? SessionStarted;
    public event EventHandler<ControllerSession>? SessionEnded;

    public ControllerSession? ActiveSession
    {
        get
        {
            lock (_syncLock) return _activeSessions.Values.FirstOrDefault();
        }
    }

    public IReadOnlyList<ControllerSession> ActiveSessions
    {
        get
        {
            lock (_syncLock) return _activeSessions.Values.ToList();
        }
    }

    public SessionTrackerService(
        IDeviceWatcherService deviceWatcher,
        ISessionRepository sessionRepository,
        IBatteryRepository batteryRepository)
    {
        _deviceWatcher = deviceWatcher;
        _sessionRepository = sessionRepository;
        _batteryRepository = batteryRepository;
    }

    public async Task InitializeAsync()
    {
        // 1. Önceki beklenmeyen kapanışlardan kalan sahipsiz açık oturumları kurtar (FR-05)
        await _sessionRepository.RecoverOrphanedSessionsAsync(DateTimeOffset.UtcNow);

        // 2. Cihaz izleyici olaylarına abone ol
        _deviceWatcher.ConnectionChanged += OnDeviceConnectionChanged;
        _deviceWatcher.BatteryLevelUpdated += OnBatteryLevelUpdated;

        // 3. İzleyiciyi başlat
        await _deviceWatcher.StartAsync();
    }

    private async void OnDeviceConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        try
        {
            if (e.Action == DebounceAction.SessionStarted)
            {
                var session = await _sessionRepository.StartSessionAsync(e.Device.DeviceKey, e.TimestampUtc);
                session.DeviceName = e.Device.DeviceName;
                lock (_syncLock)
                {
                    _activeSessions[e.Device.DeviceKey] = session;
                }

                StartLiveTicker();
                SessionStarted?.Invoke(this, session);
            }
            else if (e.Action == DebounceAction.SessionEnded)
            {
                ControllerSession? endedSession = null;
                lock (_syncLock)
                {
                    if (_activeSessions.Remove(e.Device.DeviceKey, out var s))
                    {
                        endedSession = s;
                    }

                    if (_activeSessions.Count == 0)
                    {
                        StopLiveTicker();
                    }
                }

                if (endedSession != null)
                {
                    var closed = await _sessionRepository.EndSessionAsync(
                        endedSession.DeviceKey,
                        e.TimestampUtc,
                        SessionEndReason.Disconnected);

                    if (closed != null)
                    {
                        SessionEnded?.Invoke(this, closed);
                    }
                }
            }
        }
        catch { }
    }

    private async void OnBatteryLevelUpdated(object? sender, int percentage)
    {
        try
        {
            var deviceKey = _deviceWatcher.CurrentDevice.DeviceKey;
            var reading = new BatteryReading
            {
                DeviceKey = deviceKey,
                Percentage = percentage,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = BatterySource.WindowsApiGatt,
                Confidence = BatteryConfidence.Exact
            };

            await _batteryRepository.RecordBatteryReadingAsync(reading);
        }
        catch { }
    }

    private void StartLiveTicker()
    {
        StopLiveTicker();
        _tickerCts = new CancellationTokenSource();
        var token = _tickerCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                ControllerSession? current;
                lock (_syncLock)
                {
                    current = _activeSessions.Values.FirstOrDefault();
                }

                if (current != null && current.IsActive)
                {
                    var duration = current.GetEffectiveDuration(DateTimeOffset.UtcNow);
                    ActiveSessionTicked?.Invoke(this, duration);
                }
            }
        }, token);
    }

    private void StopLiveTicker()
    {
        _tickerCts?.Cancel();
        _tickerCts?.Dispose();
        _tickerCts = null;
    }

    public async Task ShutdownAsync()
    {
        StopLiveTicker();
        List<ControllerSession> sessionsToClose;

        lock (_syncLock)
        {
            sessionsToClose = _activeSessions.Values.ToList();
            _activeSessions.Clear();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessionsToClose)
        {
            try
            {
                await _sessionRepository.EndSessionAsync(
                    session.DeviceKey,
                    now,
                    SessionEndReason.AppExit);
            }
            catch { }
        }

        await _deviceWatcher.StopAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _deviceWatcher.ConnectionChanged -= OnDeviceConnectionChanged;
            _deviceWatcher.BatteryLevelUpdated -= OnBatteryLevelUpdated;
            StopLiveTicker();
        }
    }
}
