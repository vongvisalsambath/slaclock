namespace SlaClock;

/// <summary>Where an <see cref="SlaTimer"/> is in its life.</summary>
public enum SlaState
{
    /// <summary>Not started yet.</summary>
    NotStarted,
    /// <summary>Counting business time.</summary>
    Running,
    /// <summary>Stopped counting for now, for example while waiting on the customer.</summary>
    Paused,
    /// <summary>Finished: the work was done (or cancelled). The outcome no longer changes.</summary>
    Stopped,
}

/// <summary>What happened to a timer.</summary>
public enum SlaEventKind
{
    /// <summary>The clock started.</summary>
    Started,
    /// <summary>The clock paused.</summary>
    Paused,
    /// <summary>The clock resumed after a pause.</summary>
    Resumed,
    /// <summary>The clock stopped for good.</summary>
    Stopped,
}

/// <summary>One recorded change to a timer.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Reason">An optional note, such as "Waiting on customer".</param>
public sealed record SlaEvent(DateTimeOffset At, SlaEventKind Kind, string? Reason = null);

/// <summary>A timer's position at one moment.</summary>
/// <param name="At">The moment this status describes.</param>
/// <param name="State">Running, paused, stopped, or not started at that moment.</param>
/// <param name="Elapsed">Business time counted so far (pauses and closed hours excluded).</param>
/// <param name="Remaining">Business time left before the target; zero once breached.</param>
/// <param name="Overdue">Business time past the target; zero unless breached.</param>
/// <param name="DueAt">
/// When the target will be reached if the clock keeps running: set while running, the breach moment once breached,
/// and <c>null</c> while paused (the deadline moves with the pause), stopped, or not started.
/// </param>
/// <param name="BreachedAt">The moment the counted time passed the target, if it has.</param>
/// <param name="IsBreached">Counted time has passed the target.</param>
/// <param name="IsAtRisk">Not breached, still open, and at or past the at-risk share of the target.</param>
public sealed record SlaStatus(
    DateTimeOffset At,
    SlaState State,
    TimeSpan Elapsed,
    TimeSpan Remaining,
    TimeSpan Overdue,
    DateTimeOffset? DueAt,
    DateTimeOffset? BreachedAt,
    bool IsBreached,
    bool IsAtRisk);

/// <summary>
/// A service-level timer that counts business time toward a target, and can be paused (for example while a ticket
/// waits on the customer) and resumed. Record what happens with <see cref="Start"/>, <see cref="Pause"/>,
/// <see cref="Resume"/> and <see cref="Stop"/>, then ask for <see cref="StatusAt"/> any moment, past or present.
/// </summary>
/// <example>
/// <code>
/// var sla = new SlaTimer(calendar, target: TimeSpan.FromHours(16));
/// sla.Start(ticket.CreatedAt);
/// sla.Pause(waitingSince, "Waiting on customer");
/// sla.Resume(customerRepliedAt);
/// SlaStatus now = sla.StatusAt(DateTimeOffset.UtcNow);
/// </code>
/// </example>
/// <remarks>Events must be recorded in time order. The timer is not thread-safe; give each ticket its own.</remarks>
public sealed class SlaTimer
{
    private readonly List<SlaEvent> _events = [];

    /// <summary>Creates a timer.</summary>
    /// <param name="calendar">Which hours count.</param>
    /// <param name="target">How much business time is allowed. Must be positive.</param>
    /// <param name="atRiskAt">Share of the target after which the timer is "at risk" (0 to 1). Default 0.8.</param>
    public SlaTimer(BusinessCalendar calendar, TimeSpan target, double atRiskAt = 0.8)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (target <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(target), target, "The target must be positive.");
        if (double.IsNaN(atRiskAt) || atRiskAt < 0 || atRiskAt > 1)
            throw new ArgumentOutOfRangeException(nameof(atRiskAt), atRiskAt, "The at-risk share must be between 0 and 1.");
        Calendar = calendar;
        Target = target;
        AtRiskAt = atRiskAt;
    }

    /// <summary>Recreates a timer from stored events (for example rows from a database), checking their order.</summary>
    public static SlaTimer FromEvents(BusinessCalendar calendar, TimeSpan target, IEnumerable<SlaEvent> events, double atRiskAt = 0.8)
    {
        ArgumentNullException.ThrowIfNull(events);
        var timer = new SlaTimer(calendar, target, atRiskAt);
        foreach (var e in events) timer.Record(e);
        return timer;
    }

    /// <summary>Which hours count.</summary>
    public BusinessCalendar Calendar { get; }

    /// <summary>Business time allowed.</summary>
    public TimeSpan Target { get; }

    /// <summary>Share of the target after which the timer is at risk.</summary>
    public double AtRiskAt { get; }

    /// <summary>Everything recorded so far, in order.</summary>
    public IReadOnlyList<SlaEvent> Events => _events;

    /// <summary>The state after the last recorded event.</summary>
    public SlaState State => StateAfter(_events.Count);

    /// <summary>Starts counting.</summary>
    public void Start(DateTimeOffset at) => Record(new SlaEvent(at, SlaEventKind.Started));

    /// <summary>Stops counting for now.</summary>
    public void Pause(DateTimeOffset at, string? reason = null) => Record(new SlaEvent(at, SlaEventKind.Paused, reason));

    /// <summary>Continues counting after a pause.</summary>
    public void Resume(DateTimeOffset at) => Record(new SlaEvent(at, SlaEventKind.Resumed));

    /// <summary>Stops for good: the work is done. Allowed while running or paused.</summary>
    public void Stop(DateTimeOffset at, string? reason = null) => Record(new SlaEvent(at, SlaEventKind.Stopped, reason));

    /// <summary>The timer's position at a moment. Events after <paramref name="at"/> are ignored, so this also answers "how did it look then?".</summary>
    public SlaStatus StatusAt(DateTimeOffset at)
    {
        var count = 0;
        while (count < _events.Count && _events[count].At <= at) count++;
        var state = StateAfter(count);

        // the stretches of running time up to `at`
        long elapsed = 0;
        DateTimeOffset? breachedAt = null;
        DateTimeOffset? runningSince = null;
        for (var i = 0; i < count; i++)
        {
            var e = _events[i];
            if (e.Kind is SlaEventKind.Started or SlaEventKind.Resumed) runningSince = e.At;
            else if (runningSince is { } s)
            {
                Count(s, e.At);
                runningSince = null;
            }
        }
        if (runningSince is { } open) Count(open, at);

        var elapsedSpan = TimeSpan.FromTicks(elapsed);
        var breached = elapsedSpan > Target;
        var remaining = breached ? TimeSpan.Zero : Target - elapsedSpan;
        DateTimeOffset? dueAt = state switch
        {
            _ when breached => breachedAt,
            SlaState.Running => Calendar.Add(at, remaining),
            _ => null,
        };
        var atRisk = !breached
                     && state is (SlaState.Running or SlaState.Paused)
                     && elapsedSpan.Ticks >= Target.Ticks * AtRiskAt;

        return new SlaStatus(at, state, elapsedSpan, remaining, breached ? elapsedSpan - Target : TimeSpan.Zero,
            dueAt, breached ? breachedAt : null, breached, atRisk);

        void Count(DateTimeOffset from, DateTimeOffset to)
        {
            var part = Calendar.Elapsed(from, to).Ticks;
            if (breachedAt is null && elapsed + part >= Target.Ticks)
                breachedAt = Calendar.Add(from, TimeSpan.FromTicks(Target.Ticks - elapsed));
            elapsed += part;
        }
    }

    private void Record(SlaEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_events.Count > 0 && e.At < _events[^1].At)
            throw new InvalidOperationException($"Events must be recorded in time order: {e.Kind} at {e.At:O} is before the previous event at {_events[^1].At:O}.");
        var state = State;
        var allowed = e.Kind switch
        {
            SlaEventKind.Started => state == SlaState.NotStarted,
            SlaEventKind.Paused => state == SlaState.Running,
            SlaEventKind.Resumed => state == SlaState.Paused,
            SlaEventKind.Stopped => state is SlaState.Running or SlaState.Paused,
            _ => false,
        };
        if (!allowed) throw new InvalidOperationException($"Cannot record {e.Kind} while the timer is {state}.");
        _events.Add(e);
    }

    private SlaState StateAfter(int count) => count == 0
        ? SlaState.NotStarted
        : _events[count - 1].Kind switch
        {
            SlaEventKind.Started or SlaEventKind.Resumed => SlaState.Running,
            SlaEventKind.Paused => SlaState.Paused,
            _ => SlaState.Stopped,
        };
}
