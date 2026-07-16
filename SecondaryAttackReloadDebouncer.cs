using System;
using BepInEx;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackReloadDebouncer : IDisposable
{
    internal const long DefaultDelayTicks = TimeSpan.TicksPerMillisecond * 250;

    private static readonly double[] RetryDelayMilliseconds =
    {
        TimeSpan.FromTicks(DefaultDelayTicks).TotalMilliseconds,
        500d,
        1000d,
        2000d
    };

    private readonly object _lock = new();
    private readonly Func<bool> _action;
    private readonly string _operationName;
    private readonly System.Timers.Timer _timer;
    private bool _disposed;
    private int _attempt;
    private int _generation;

    internal SecondaryAttackReloadDebouncer(Func<bool> action, string operationName)
    {
        _action = action;
        _operationName = string.IsNullOrWhiteSpace(operationName) ? "configuration reload" : operationName;
        _timer = new System.Timers.Timer(RetryDelayMilliseconds[0])
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

            _generation++;
            _attempt = 0;
            _timer.Stop();
            _timer.Interval = RetryDelayMilliseconds[0];
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
        int generation;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            generation = _generation;
        }

        bool completed;
        try
        {
            completed = _action();
        }
        catch (Exception exception)
        {
            SecondaryAttacksPlugin.ModLogger.LogError($"Error during {_operationName}: {exception.Message}");
            completed = false;
        }

        lock (_lock)
        {
            if (_disposed || generation != _generation || completed)
            {
                return;
            }

            _attempt++;
            if (_attempt >= RetryDelayMilliseconds.Length)
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning(
                    $"Stopped automatic {_operationName} after {RetryDelayMilliseconds.Length} attempts. A later file or configuration event will start a new retry cycle.");
                return;
            }

            _timer.Stop();
            _timer.Interval = RetryDelayMilliseconds[_attempt];
            _timer.Start();
        }
    }
}
