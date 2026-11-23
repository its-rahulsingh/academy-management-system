namespace Academy.Api.Infrastructure;

/// <summary>
/// Keeps the background jobs gentle on the database. The hosted Postgres (Neon) suspends itself after about five
/// minutes without queries and bills compute while awake, so instead of each job polling on its own timer, the idle
/// checks all run together on the quarter hour (:00, :15, :30, :45). Between those moments a quiet academy causes no
/// queries at all. Work that people are waiting for (an email just queued) does not wait for the slot.
/// </summary>
public static class QuietSchedule
{
    /// <summary>How often idle checks run.</summary>
    public static readonly TimeSpan Slot = TimeSpan.FromMinutes(15);

    /// <summary>Time from <paramref name="utcNow"/> to the next wall-clock multiple of <paramref name="every"/> (never zero).</summary>
    public static TimeSpan UntilNext(TimeSpan every, DateTime utcNow)
    {
        var ticks = every.Ticks;
        var next = (utcNow.Ticks / ticks + 1) * ticks;
        return TimeSpan.FromTicks(next - utcNow.Ticks);
    }
}

/// <summary>
/// Wakes the email sender as soon as a request has saved new mail, so receipts go out within seconds even though
/// the sender otherwise only looks at the queue on the quarter hour. One process-wide signal is enough.
/// </summary>
public static class EmailWakeUp
{
    private static readonly SemaphoreSlim Signal = new(0, 1);

    public static void Notify()
    {
        if (Signal.CurrentCount > 0) return;
        try { Signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    /// <summary>True when woken by <see cref="Notify"/>, false when the timeout passed first.</summary>
    public static Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => Signal.WaitAsync(timeout, ct);
}
