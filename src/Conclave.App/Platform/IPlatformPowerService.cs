namespace Conclave.App.Platform;

// Cross-platform OS-power events. v1 only fires DeviceWoke (resume from sleep) — that's
// the signal StallDetectionService cares about, since a sleeping laptop is the dominant
// cause of a stalled claude turn. Implementations may fire the event from a non-UI
// thread; subscribers must marshal to the UI thread before mutating VMs.
public interface IPlatformPowerService : IDisposable
{
    event Action? DeviceWoke;
    void Start();
}
