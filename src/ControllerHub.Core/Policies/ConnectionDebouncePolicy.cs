using ControllerHub.Core.Models;

namespace ControllerHub.Core.Policies;

/// <summary>
/// Bluetooth ve PnP geçişlerindeki 2-3 saniyelik sinyal dalgalanmalarını ve geçici kopmaları
/// süzerek oturumların gereksiz bölünmesini önleyen durum makinesi ve debounce politikası.
/// </summary>
public class ConnectionDebouncePolicy
{
    private readonly TimeSpan _disconnectGracePeriod;
    private readonly TimeSpan _connectDebouncePeriod;

    private DeviceConnectionState _currentState = DeviceConnectionState.Disconnected;
    private DateTimeOffset? _rawSignalLostAtUtc;
    private DateTimeOffset? _rawSignalReceivedAtUtc;

    public ConnectionDebouncePolicy(TimeSpan? disconnectGracePeriod = null, TimeSpan? connectDebouncePeriod = null)
    {
        _disconnectGracePeriod = disconnectGracePeriod ?? TimeSpan.FromSeconds(4);
        _connectDebouncePeriod = connectDebouncePeriod ?? TimeSpan.FromMilliseconds(500);
    }

    public DeviceConnectionState CurrentState => _currentState;
    public bool IsConnected => _currentState == DeviceConnectionState.Connected;

    /// <summary>
    /// Ham PnP veya Bluetooth varlık sinyalini işler ve filtrelenmiş kararlı durumu döner.
    /// </summary>
    /// <param name="rawSignalPresent">Donanım katmanından okunan ham varlık durumu</param>
    /// <param name="nowUtc">Mevcut zaman damgası</param>
    /// <returns>Filtrelenmiş bağlantı durumu ve oturum aksiyonu (Started, Ended, None)</returns>
    public (DeviceConnectionState State, DebounceAction Action) Evaluate(bool rawSignalPresent, DateTimeOffset nowUtc)
    {
        switch (_currentState)
        {
            case DeviceConnectionState.Disconnected:
                if (rawSignalPresent)
                {
                    _rawSignalReceivedAtUtc ??= nowUtc;
                    if (nowUtc - _rawSignalReceivedAtUtc.Value >= _connectDebouncePeriod)
                    {
                        _currentState = DeviceConnectionState.Connected;
                        _rawSignalReceivedAtUtc = null;
                        return (_currentState, DebounceAction.SessionStarted);
                    }
                    _currentState = DeviceConnectionState.Connecting;
                    return (_currentState, DebounceAction.None);
                }
                _rawSignalReceivedAtUtc = null;
                return (_currentState, DebounceAction.None);

            case DeviceConnectionState.Connecting:
                if (rawSignalPresent)
                {
                    _rawSignalReceivedAtUtc ??= nowUtc;
                    if (nowUtc - _rawSignalReceivedAtUtc.Value >= _connectDebouncePeriod)
                    {
                        _currentState = DeviceConnectionState.Connected;
                        _rawSignalReceivedAtUtc = null;
                        return (_currentState, DebounceAction.SessionStarted);
                    }
                    return (_currentState, DebounceAction.None);
                }
                // Sinyal kesildiyse Disconnected'a dön
                _currentState = DeviceConnectionState.Disconnected;
                _rawSignalReceivedAtUtc = null;
                return (_currentState, DebounceAction.None);

            case DeviceConnectionState.Connected:
                if (!rawSignalPresent)
                {
                    _rawSignalLostAtUtc = nowUtc;
                    _currentState = DeviceConnectionState.Disconnecting;
                    return (_currentState, DebounceAction.None);
                }
                return (_currentState, DebounceAction.None);

            case DeviceConnectionState.Disconnecting:
                if (rawSignalPresent)
                {
                    // Grace period içinde sinyal geri geldi; oturum kesintiye uğramaz!
                    _currentState = DeviceConnectionState.Connected;
                    _rawSignalLostAtUtc = null;
                    return (_currentState, DebounceAction.None);
                }

                if (_rawSignalLostAtUtc.HasValue && (nowUtc - _rawSignalLostAtUtc.Value >= _disconnectGracePeriod))
                {
                    // 4 saniye doldu, kopma kesinleşti -> Oturumu bitir
                    _currentState = DeviceConnectionState.Disconnected;
                    _rawSignalLostAtUtc = null;
                    return (_currentState, DebounceAction.SessionEnded);
                }
                return (_currentState, DebounceAction.None);

            default:
                _currentState = rawSignalPresent ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected;
                return (_currentState, DebounceAction.None);
        }
    }
}

public enum DebounceAction
{
    None,
    SessionStarted,
    SessionEnded
}
