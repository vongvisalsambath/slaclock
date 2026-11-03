namespace SlaClock;

/// <summary>
/// When a business is open: weekly hours (split shifts allowed), holidays, and special hours on given dates,
/// all in one time zone. Answers "is it open?", "when does it next open?", "what is the deadline if this
/// takes 16 business hours?" and "how much business time passed between these two moments?".
/// </summary>
/// <remarks>
/// <para>Hours are wall-clock times in the calendar's time zone. Durations are real elapsed time, so on a
/// daylight-saving change the business day is really an hour shorter or longer. Opening hours that fall in the
/// skipped hour (spring forward) move forward by the length of the gap; in the repeated hour (fall back), the
/// first occurrence is used.</para>
/// <para>Instances are immutable and safe to share between threads. Results are returned with the calendar's
/// UTC offset at that moment, so they read as local times.</para>
/// </remarks>
public sealed class BusinessCalendar
{
    /// <summary>How far ahead the calendar looks for open hours before giving up (in days).</summary>
    internal const int SearchHorizonDays = 366 * 10;

    private const long MaxMergedTicks = TimeSpan.TicksPerDay * 7;

    private readonly IReadOnlyList<TimeRange>[] _weekly;
    private readonly IReadOnlyDictionary<DateOnly, DateRule> _dates;
    private readonly IReadOnlyDictionary<(int Month, int Day), string?> _yearly;

    internal BusinessCalendar(
        TimeZoneInfo timeZone,
        IReadOnlyList<TimeRange>[] weekly,
        IReadOnlyDictionary<DateOnly, DateRule> dates,
        IReadOnlyDictionary<(int, int), string?> yearly)
    {
        TimeZone = timeZone;
        _weekly = weekly;
        _dates = dates;
        _yearly = yearly;
    }

    /// <summary>The time zone the opening hours are in.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>Starts a calendar in an IANA (<c>"Asia/Phnom_Penh"</c>) or Windows time zone.</summary>
    /// <exception cref="ArgumentException">The time zone is not known on this machine.</exception>
    public static BusinessCalendarBuilder Create(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        try
        {
            return new BusinessCalendarBuilder(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"Unknown time zone \"{timeZoneId}\". Use an IANA id such as \"Asia/Phnom_Penh\" or \"Europe/London\".", nameof(timeZoneId), e);
        }
    }

    /// <summary>Starts a calendar in the given time zone.</summary>
    public static BusinessCalendarBuilder Create(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return new BusinessCalendarBuilder(timeZone);
    }

    /// <summary>Open 24 hours a day, every day (wall-clock time). Useful for urgent tickets with a 24/7 target.</summary>
    public static BusinessCalendar AlwaysOpen { get; } =
        Create(TimeZoneInfo.Utc).EveryDay(new TimeRange(TimeSpan.Zero, TimeSpan.FromDays(1))).Build();

    /// <summary>The opening hours on a local date, after holidays and special hours are applied. Empty when closed.</summary>
    public IReadOnlyList<TimeRange> HoursOn(DateOnly date)
    {
        if (_dates.TryGetValue(date, out var rule)) return rule.Hours;
        if (_yearly.ContainsKey((date.Month, date.Day))) return [];
        return _weekly[(int)date.DayOfWeek];
    }

    /// <summary>The name of the holiday or special day on a local date, if one was given.</summary>
    public string? DayName(DateOnly date)
    {
        if (_dates.TryGetValue(date, out var rule)) return rule.Name;
        return _yearly.TryGetValue((date.Month, date.Day), out var name) ? name : null;
    }

    /// <summary>Whether the business is open at a moment.</summary>
    public bool IsOpen(DateTimeOffset at)
    {
        var t = at.UtcTicks;
        foreach (var (start, end) in IntervalsFrom(LocalDate(at).AddDays(-1)))
        {
            if (start > t) return false;
            if (t < end) return true;
        }
        return false;
    }

    /// <summary>The first open moment at or after <paramref name="from"/>: <paramref name="from"/> itself when already open.</summary>
    public DateTimeOffset NextOpen(DateTimeOffset from)
    {
        var t = from.UtcTicks;
        foreach (var (start, end) in IntervalsFrom(LocalDate(from).AddDays(-1)))
        {
            if (end > t) return ToLocal(Math.Max(start, t));
        }
        throw NoOpenHours();
    }

    /// <summary>
    /// The moment when <paramref name="businessTime"/> of open time has passed, counting from <paramref name="from"/>.
    /// This is the deadline: "16 business hours from now". A result that lands exactly on closing time is returned
    /// as closing time (due by 17:00 Friday), not as the next opening.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="businessTime"/> is negative.</exception>
    public DateTimeOffset Add(DateTimeOffset from, TimeSpan businessTime)
    {
        if (businessTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(businessTime), businessTime, "Business time to add must not be negative.");
        if (businessTime == TimeSpan.Zero) return ToLocal(from.UtcTicks);

        var t = from.UtcTicks;
        var remaining = businessTime.Ticks;
        foreach (var (start, end) in IntervalsFrom(LocalDate(from).AddDays(-1)))
        {
            if (end <= t) continue;
            var s = Math.Max(start, t);
            var available = end - s;
            if (remaining <= available) return ToLocal(s + remaining);
            remaining -= available;
        }
        throw NoOpenHours();
    }

    /// <summary>
    /// How much open time lies between two moments. Negative when <paramref name="to"/> is before <paramref name="from"/>.
    /// </summary>
    public TimeSpan Elapsed(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from) return -Elapsed(to, from);
        long a = from.UtcTicks, b = to.UtcTicks, total = 0;
        foreach (var (start, end) in IntervalsFrom(LocalDate(from).AddDays(-1)))
        {
            if (start >= b) break;
            if (end <= a) continue;
            total += Math.Min(end, b) - Math.Max(start, a);
        }
        return TimeSpan.FromTicks(total);
    }

    /// <summary>The open periods that overlap a time window, clipped to it, in order.</summary>
    public IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> OpenPeriods(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) yield break;
        long a = from.UtcTicks, b = to.UtcTicks;
        foreach (var (start, end) in IntervalsFrom(LocalDate(from).AddDays(-1)))
        {
            if (start >= b) yield break;
            if (end <= a) continue;
            yield return (ToLocal(Math.Max(start, a)), ToLocal(Math.Min(end, b)));
        }
    }

    // ------------------------------------------------------------------ internals

    /// <summary>
    /// Open intervals as UTC ticks, day by day from a local date, merged where they touch
    /// (so a shift across midnight is one interval). Ends after the search horizon with no open hours.
    /// </summary>
    internal IEnumerable<(long Start, long End)> IntervalsFrom(DateOnly firstDate)
    {
        long? curStart = null, curEnd = null;
        var emptyDays = 0;
        for (var date = firstDate; ; date = date.AddDays(1))
        {
            var hours = HoursOn(date);
            if (hours.Count == 0)
            {
                if (++emptyDays > SearchHorizonDays) break;
                continue;
            }
            emptyDays = 0;
            var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            foreach (var range in hours)
            {
                var s = ToUtcTicks(midnight + range.Start);
                var e = ToUtcTicks(midnight + range.End);
                if (e <= s) continue; // swallowed entirely by a daylight-saving gap
                if (curEnd is { } ce && s <= ce)
                {
                    curEnd = Math.Max(ce, e); // touches the previous interval: extend it
                    // A calendar open around the clock would merge forever; hand out a week at a time.
                    if (curEnd - curStart!.Value >= MaxMergedTicks)
                    {
                        yield return (curStart.Value, curEnd.Value);
                        curStart = curEnd = null;
                    }
                    continue;
                }
                if (curStart is { } cs) yield return (cs, curEnd!.Value);
                curStart = s;
                curEnd = e;
            }
        }
        if (curStart is { } last) yield return (last, curEnd!.Value);
    }

    private DateOnly LocalDate(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TimeZone).DateTime);

    private DateTimeOffset ToLocal(long utcTicks) => TimeZoneInfo.ConvertTime(new DateTimeOffset(utcTicks, TimeSpan.Zero), TimeZone);

    /// <summary>Wall-clock time in this zone → UTC ticks, resolving daylight-saving gaps and overlaps.</summary>
    internal long ToUtcTicks(DateTime local)
    {
        if (TimeZone.IsInvalidTime(local))
        {
            // In the skipped hour: use the offset from before the jump, which lands the same distance past the gap.
            var before = TimeZone.GetUtcOffset(local.AddHours(-6));
            return (local - before).Ticks;
        }
        if (TimeZone.IsAmbiguousTime(local))
        {
            // In the repeated hour: the first occurrence is the one with the larger offset.
            var offsets = TimeZone.GetAmbiguousTimeOffsets(local);
            return (local - offsets.Max()).Ticks;
        }
        return (local - TimeZone.GetUtcOffset(local)).Ticks;
    }

    private static InvalidOperationException NoOpenHours() =>
        new($"The calendar has no open hours within {SearchHorizonDays / 366} years of the requested time.");

    internal sealed record DateRule(IReadOnlyList<TimeRange> Hours, string? Name);
}
