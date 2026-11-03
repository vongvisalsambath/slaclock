using System.Globalization;

namespace SlaClock;

/// <summary>
/// A span of opening hours within one day, in local (wall-clock) time: <c>08:00-17:00</c>.
/// The end may be <c>24:00</c> to mean midnight at the end of the day. For a shift that runs past
/// midnight, add one range to each day (<c>22:00-24:00</c> and <c>00:00-06:00</c>); they join up.
/// </summary>
public readonly record struct TimeRange
{
    private static readonly TimeSpan DayLength = TimeSpan.FromDays(1);

    /// <summary>Opening time, from the start of the day.</summary>
    public TimeSpan Start { get; }

    /// <summary>Closing time, from the start of the day (at most 24:00). Exclusive.</summary>
    public TimeSpan End { get; }

    /// <summary>Creates a range. Requires <c>00:00 &lt;= start &lt; end &lt;= 24:00</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty, reversed, or outside the day.</exception>
    public TimeRange(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || start >= DayLength)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start must be between 00:00 and 23:59:59.");
        if (end <= start || end > DayLength)
            throw new ArgumentOutOfRangeException(nameof(end), end, $"End must be after start ({Format(start)}) and no later than 24:00.");
        Start = start;
        End = end;
    }

    /// <summary>Creates a range from hours and minutes: <c>new TimeRange(8, 0, 17, 0)</c>.</summary>
    public TimeRange(int startHour, int startMinute, int endHour, int endMinute)
        : this(new TimeSpan(startHour, startMinute, 0), new TimeSpan(endHour, endMinute, 0)) { }

    /// <summary>The length of the range.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Parses <c>"HH:mm-HH:mm"</c>, for example <c>"08:00-12:00"</c> or <c>"22:00-24:00"</c>.</summary>
    /// <exception cref="FormatException">The text is not in that form.</exception>
    public static TimeRange Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseTime(parts[0], out var start) || !TryParseTime(parts[1], out var end))
            throw new FormatException($"Expected a range like \"08:00-17:00\", got \"{text}\".");
        try
        {
            return new TimeRange(start, end);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new FormatException($"\"{text}\" is not a valid range: {e.Message}", e);
        }
    }

    /// <summary>Lets you write ranges as strings: <c>.Weekdays("08:00-12:00", "13:00-17:00")</c>.</summary>
    public static implicit operator TimeRange(string text) => Parse(text);

    /// <summary>Formats as <c>"HH:mm-HH:mm"</c>.</summary>
    public override string ToString() => $"{Format(Start)}-{Format(End)}";

    internal bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    private static string Format(TimeSpan t) =>
        t == DayLength ? "24:00" : t.ToString(t.Seconds == 0 ? @"hh\:mm" : @"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string s, out TimeSpan value)
    {
        if (s == "24:00") { value = DayLength; return true; }
        return TimeSpan.TryParseExact(s, [@"h\:mm", @"hh\:mm", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out value)
               && value >= TimeSpan.Zero && value < DayLength;
    }
}
