namespace TMapEditor.Services;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string InstanceName = "TMapEditor.SingleInstance";
    private const string ActivationEventName = @"Local\TMapEditor.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle? activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _ownsMutex = true;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var activationEvent = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName)
            : null;
        var mutexName = OperatingSystem.IsWindows()
            ? $@"Local\{InstanceName}"
            : InstanceName;
        var mutex = new Mutex(initiallyOwned: false, mutexName);

        try
        {
            if (!mutex.WaitOne(0))
            {
                activationEvent?.Set();
                activationEvent?.Dispose();
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous process exited unexpectedly; ownership is transferred here.
        }

        return new SingleInstanceGuard(mutex, activationEvent);
    }

    public void ListenForActivation(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (_activationEvent is null) return;

        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut) activationRequested();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _activationWait?.Unregister(null);
        _activationWait = null;
        _activationEvent?.Dispose();
        _mutex.Dispose();
    }
}
