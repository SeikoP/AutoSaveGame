namespace AutoSaveGame.Infrastructure.Tests.TestSupport;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset utcNow =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return utcNow;
        }
    }

    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (gate)
        {
            timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (gate)
        {
            utcNow += amount;
            foreach (var timer in timers.ToArray())
            {
                timer.CollectDueCallbacks(utcNow, callbacks);
            }
        }

        foreach (var item in callbacks)
        {
            item.Callback(item.State);
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private DateTimeOffset? dueAt = dueTime == Timeout.InfiniteTimeSpan
            ? null
            : owner.GetUtcNow() + dueTime;
        private TimeSpan period = period;
        private bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
        {
            if (disposed)
            {
                return false;
            }

            dueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : owner.GetUtcNow() + dueTime;
            period = newPeriod;
            return true;
        }

        public void Dispose()
        {
            disposed = true;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void CollectDueCallbacks(
            DateTimeOffset now,
            ICollection<(TimerCallback Callback, object? State)> callbacks)
        {
            if (disposed || dueAt is null || dueAt > now)
            {
                return;
            }

            callbacks.Add((callback, state));
            dueAt = period == Timeout.InfiniteTimeSpan
                ? null
                : now + period;
        }
    }
}

