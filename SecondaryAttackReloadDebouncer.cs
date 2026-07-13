using System;
using BepInEx;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackReloadDebouncer : IDisposable
{
    internal const long DefaultDelayTicks = TimeSpan.TicksPerMillisecond * 250;

    private readonly object _lock = new();
    private readonly Action _action;
    private readonly System.Timers.Timer _timer;
    private bool _disposed;

    internal SecondaryAttackReloadDebouncer(Action action)
    {
        _action = action;
        _timer = new System.Timers.Timer(TimeSpan.FromTicks(DefaultDelayTicks).TotalMilliseconds)
        {
            AutoReset = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject
        };
        _timer.Elapsed += OnElapsed;
    }

    internal void Schedule()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _timer.Stop();
            _timer.Start();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Elapsed -= OnElapsed;
            _timer.Dispose();
        }
    }

    private void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
        }

        _action();
    }
}
