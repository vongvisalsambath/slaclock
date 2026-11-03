namespace SlaClock;

/// <summary>
/// Describes a <see cref="BusinessCalendar"/>. Start with <see cref="BusinessCalendar.Create(string)"/>,
/// set weekly hours, add holidays and special days, then call <see cref="Build"/>, which checks everything.
/// </summary>
/// <example>
/// <code>
/// var calendar = BusinessCalendar.Create("Asia/Phnom_Penh")
///     .Weekdays("08:00-12:00", "13:00-17:00")
///     .Closed(new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 16), "Khmer New Year")
///     .ClosedEveryYear(1, 1, "International New Year")
///     .Hours(new DateOnly(2026, 12, 31), "08:00-12:00")
///     .Build();
/// </code>
/// </example>
public sealed class BusinessCalendarBuilder
{
    private readonly TimeZoneInfo _zone;
    private readonly List<TimeRange>[] _weekly = Enumerable.Range(0, 7).Select(_ => new List<TimeRange>()).ToArray();
    private readonly Dictionary<DateOnly, BusinessCalendar.DateRule> _dates = [];
    private readonly Dictionary<(int, int), string?> _yearly = [];

    internal BusinessCalendarBuilder(TimeZoneInfo zone) => _zone = zone;

    /// <summary>Adds opening hours on one day of the week. Call again (or pass several ranges) for split shifts.</summary>
    public BusinessCalendarBuilder On(DayOfWeek day, params TimeRange[] hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        _weekly[(int)day].AddRange(hours);
        return this;
    }

    /// <summary>Adds the same opening hours on several days of the week.</summary>
    public BusinessCalendarBuilder On(IEnumerable<DayOfWeek> days, params TimeRange[] hours)
    {
        ArgumentNullException.ThrowIfNull(days);
        foreach (var day in days) On(day, hours);
        return this;
    }

    /// <summary>Adds opening hours Monday to Friday.</summary>
    public BusinessCalendarBuilder Weekdays(params TimeRange[] hours) =>
        On([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], hours);

    /// <summary>Adds opening hours on all seven days.</summary>
    public BusinessCalendarBuilder EveryDay(params TimeRange[] hours) => On(Enum.GetValues<DayOfWeek>(), hours);

    /// <summary>Closed all day on a date (a public holiday, an office closure).</summary>
    public BusinessCalendarBuilder Closed(DateOnly date, string? name = null) => SetDate(date, [], name);

    /// <summary>Closed every day from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public BusinessCalendarBuilder Closed(DateOnly from, DateOnly to, string? name = null)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), to, $"The last day must not be before the first ({from}).");
        for (var d = from; d <= to; d = d.AddDays(1)) Closed(d, name);
        return this;
    }

    /// <summary>Closed on the same date every year, such as 1 January.</summary>
    public BusinessCalendarBuilder ClosedEveryYear(int month, int day, string? name = null)
    {
        // validate against a leap year so 29 February is allowed
        _ = new DateOnly(2024, month, day);
        _yearly[(month, day)] = name;
        return this;
    }

    /// <summary>
    /// Special opening hours on one date, replacing the usual hours and any yearly holiday: a half day, a weekend
    /// opening. Use <see cref="Closed(DateOnly, string?)"/> for a full closure.
    /// </summary>
    public BusinessCalendarBuilder Hours(DateOnly date, params TimeRange[] hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        if (hours.Length == 0) throw new ArgumentException("Give at least one range, or use Closed() for a closed day.", nameof(hours));
        return SetDate(date, hours, null);
    }

    /// <summary>
    /// Special opening hours on one date, with a name: <c>.Hours(date, "New Year's Eve", ["08:00-12:00"])</c>.
    /// (The ranges are a collection so a name can never be mistaken for a range.)
    /// </summary>
    public BusinessCalendarBuilder Hours(DateOnly date, string name, IEnumerable<TimeRange> hours)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(hours);
        var list = hours.ToArray();
        if (list.Length == 0) throw new ArgumentException("Give at least one range, or use Closed() for a closed day.", nameof(hours));
        return SetDate(date, list, name);
    }

    /// <summary>Checks the description and creates the calendar.</summary>
    /// <exception cref="InvalidOperationException">
    /// Ranges overlap on a day, or no day of the week has opening hours.
    /// </exception>
    public BusinessCalendar Build()
    {
        var weekly = new IReadOnlyList<TimeRange>[7];
        for (var i = 0; i < 7; i++) weekly[i] = Normalise(_weekly[i], ((DayOfWeek)i).ToString());
        if (weekly.All(w => w.Count == 0))
            throw new InvalidOperationException("The calendar has no weekly opening hours. Add some with Weekdays(), On() or EveryDay().");

        var dates = _dates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value with { Hours = Normalise(kv.Value.Hours, kv.Key.ToString("yyyy-MM-dd")) });
        return new BusinessCalendar(_zone, weekly, dates, new Dictionary<(int, int), string?>(_yearly));
    }

    private BusinessCalendarBuilder SetDate(DateOnly date, IReadOnlyList<TimeRange> hours, string? name)
    {
        _dates[date] = new BusinessCalendar.DateRule(hours.ToArray(), name);
        return this;
    }

    private static TimeRange[] Normalise(IEnumerable<TimeRange> ranges, string where)
    {
        var sorted = ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i - 1].Overlaps(sorted[i]))
                throw new InvalidOperationException($"Opening hours overlap on {where}: {sorted[i - 1]} and {sorted[i]}.");
        }
        return sorted;
    }
}
