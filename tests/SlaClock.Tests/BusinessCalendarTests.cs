namespace SlaClock.Tests;

public class BusinessCalendarTests
{
    // Phnom Penh is UTC+7 all year. Mon–Fri 08:00–12:00 and 13:00–17:00 (an hour for lunch).
    private static readonly TimeSpan PP = TimeSpan.FromHours(7);
    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, PP);

    private static readonly BusinessCalendar Office = BusinessCalendar.Create("Asia/Phnom_Penh")
        .Weekdays("08:00-12:00", "13:00-17:00")
        .Closed(new DateOnly(2026, 11, 9), "Independence Day")          // a Monday
        .Hours(new DateOnly(2026, 12, 31), "New Year's Eve", ["08:00-12:00"])
        .ClosedEveryYear(1, 1, "International New Year")
        .Build();

    // 2026-11-02 is a Monday.

    [Theory]
    [InlineData(2, 8, 0, true)]    // opening time is open
    [InlineData(2, 11, 59, true)]
    [InlineData(2, 12, 0, false)]  // lunch
    [InlineData(2, 12, 30, false)]
    [InlineData(2, 13, 0, true)]
    [InlineData(2, 17, 0, false)]  // closing time is closed (ranges are end-exclusive)
    [InlineData(7, 10, 0, false)]  // Saturday
    [InlineData(9, 10, 0, false)]  // holiday
    public void IsOpen(int day, int hour, int minute, bool expected) =>
        Assert.Equal(expected, Office.IsOpen(Local(2026, 11, day, hour, minute)));

    [Fact]
    public void IsOpen_takes_any_offset() =>
        Assert.True(Office.IsOpen(new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero))); // 10:00 in Phnom Penh

    [Fact]
    public void NextOpen_returns_the_same_moment_when_open() =>
        Assert.Equal(Local(2026, 11, 2, 9, 15), Office.NextOpen(Local(2026, 11, 2, 9, 15)));

    [Fact]
    public void NextOpen_skips_lunch() =>
        Assert.Equal(Local(2026, 11, 2, 13), Office.NextOpen(Local(2026, 11, 2, 12, 20)));

    [Fact]
    public void NextOpen_skips_the_weekend_and_a_holiday() =>
        Assert.Equal(Local(2026, 11, 10, 8), Office.NextOpen(Local(2026, 11, 6, 17))); // Fri close → Tue (Mon is a holiday)

    [Fact]
    public void Add_skips_lunch() =>
        Assert.Equal(Local(2026, 11, 2, 14), Office.Add(Local(2026, 11, 2, 11), TimeSpan.FromHours(2)));

    [Fact]
    public void Add_carries_over_the_weekend()
    {
        // Fri 16:00 + 8h: 1h on Friday, then Tue (Mon is a holiday) 08–12 (4h) and 13–16 (3h)
        Assert.Equal(Local(2026, 11, 10, 16), Office.Add(Local(2026, 11, 6, 16), TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Add_landing_on_closing_time_returns_closing_time() =>
        Assert.Equal(Local(2026, 11, 2, 17), Office.Add(Local(2026, 11, 2, 16), TimeSpan.FromHours(1)));

    [Fact]
    public void Add_from_closed_time_starts_at_the_next_opening() =>
        Assert.Equal(Local(2026, 11, 2, 9), Office.Add(Local(2026, 11, 2, 6), TimeSpan.FromHours(1)));

    [Fact]
    public void Add_zero_returns_the_start_even_when_closed() =>
        Assert.Equal(Local(2026, 11, 7, 10), Office.Add(Local(2026, 11, 7, 10), TimeSpan.Zero));

    [Fact]
    public void Add_rejects_negative_time() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Office.Add(Local(2026, 11, 2, 9), TimeSpan.FromHours(-1)));

    [Fact]
    public void Results_are_in_the_calendars_offset()
    {
        var due = Office.Add(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1)); // 08:00 local
        Assert.Equal(PP, due.Offset);
        Assert.Equal(Local(2026, 11, 2, 9), due);
    }

    [Fact]
    public void Elapsed_counts_only_open_time() =>
        Assert.Equal(TimeSpan.FromHours(8), Office.Elapsed(Local(2026, 11, 2, 0), Local(2026, 11, 3, 0)));

    [Fact]
    public void Elapsed_over_a_week_skips_weekend_and_holiday() =>
        Assert.Equal(TimeSpan.FromHours(32), Office.Elapsed(Local(2026, 11, 7, 0), Local(2026, 11, 14, 0))); // Tue–Fri

    [Fact]
    public void Elapsed_is_negative_when_reversed() =>
        Assert.Equal(TimeSpan.FromHours(-2), Office.Elapsed(Local(2026, 11, 2, 11), Local(2026, 11, 2, 9)));

    [Fact]
    public void Special_hours_replace_the_usual_day()
    {
        var eve = new DateOnly(2026, 12, 31);
        Assert.Equal([new TimeRange(8, 0, 12, 0)], Office.HoursOn(eve));
        Assert.Equal("New Year's Eve", Office.DayName(eve));
        Assert.False(Office.IsOpen(Local(2026, 12, 31, 14)));
    }

    [Fact]
    public void Yearly_holidays_repeat()
    {
        Assert.Empty(Office.HoursOn(new DateOnly(2027, 1, 1)));   // a Friday
        Assert.Equal("International New Year", Office.DayName(new DateOnly(2030, 1, 1)));
        Assert.Equal(Local(2027, 1, 4, 8), Office.NextOpen(Local(2026, 12, 31, 12)));
    }

    [Fact]
    public void Special_hours_override_a_yearly_holiday()
    {
        var cal = BusinessCalendar.Create("Asia/Phnom_Penh").Weekdays("09:00-17:00")
            .ClosedEveryYear(1, 1).Hours(new DateOnly(2027, 1, 1), "09:00-11:00").Build();
        Assert.Equal(TimeSpan.FromHours(2), cal.Elapsed(Local(2027, 1, 1, 0), Local(2027, 1, 2, 0)));
    }

    [Fact]
    public void Ranges_given_as_strings_are_never_read_as_a_name()
    {
        var date = new DateOnly(2027, 2, 1);
        var cal = BusinessCalendar.Create("Asia/Phnom_Penh").Weekdays("09:00-17:00")
            .Hours(date, "09:00-11:00", "13:00-15:00").Build();
        Assert.Equal([new TimeRange(9, 0, 11, 0), new TimeRange(13, 0, 15, 0)], cal.HoursOn(date));
        Assert.Null(cal.DayName(date));
    }

    [Fact]
    public void Closed_range_includes_both_ends()
    {
        var cal = BusinessCalendar.Create("Asia/Phnom_Penh").EveryDay("09:00-17:00")
            .Closed(new DateOnly(2027, 4, 14), new DateOnly(2027, 4, 16), "Khmer New Year").Build();
        Assert.Equal(Local(2027, 4, 17, 9), cal.NextOpen(Local(2027, 4, 13, 18)));
        Assert.Equal("Khmer New Year", cal.DayName(new DateOnly(2027, 4, 16)));
    }

    [Fact]
    public void A_shift_across_midnight_is_one_open_period()
    {
        var night = BusinessCalendar.Create("Asia/Phnom_Penh")
            .On(DayOfWeek.Monday, "22:00-24:00").On(DayOfWeek.Tuesday, "00:00-06:00").Build();
        Assert.Equal(Local(2026, 11, 3, 1), night.Add(Local(2026, 11, 2, 23), TimeSpan.FromHours(2)));
        var periods = night.OpenPeriods(Local(2026, 11, 2, 0), Local(2026, 11, 4, 0)).ToList();
        Assert.Single(periods);
        Assert.Equal((Local(2026, 11, 2, 22), Local(2026, 11, 3, 6)), periods[0]);
    }

    [Fact]
    public void OpenPeriods_are_clipped_to_the_window()
    {
        var periods = Office.OpenPeriods(Local(2026, 11, 2, 10), Local(2026, 11, 2, 15)).ToList();
        Assert.Equal([(Local(2026, 11, 2, 10), Local(2026, 11, 2, 12)), (Local(2026, 11, 2, 13), Local(2026, 11, 2, 15))], periods);
    }

    [Fact]
    public void AlwaysOpen_is_wall_clock_time()
    {
        var start = new DateTimeOffset(2026, 11, 7, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal(start.AddHours(30), BusinessCalendar.AlwaysOpen.Add(start, TimeSpan.FromHours(30)));
        Assert.Equal(TimeSpan.FromDays(20), BusinessCalendar.AlwaysOpen.Elapsed(start, start.AddDays(20)));
    }

    // ------------------------------------------------------------------ daylight saving (New York)

    private static readonly BusinessCalendar NewYork24 = BusinessCalendar.Create("America/New_York").EveryDay("00:00-24:00").Build();

    [Fact]
    public void Spring_forward_day_has_23_hours()
    {
        // 2026-03-08: 02:00 EST jumps to 03:00 EDT
        var start = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(TimeSpan.FromHours(23), NewYork24.Elapsed(start, end));
    }

    [Fact]
    public void Fall_back_day_has_25_hours()
    {
        // 2026-11-01: 02:00 EDT falls back to 01:00 EST
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var end = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(TimeSpan.FromHours(25), NewYork24.Elapsed(start, end));
    }

    [Fact]
    public void Hours_in_the_skipped_hour_move_forward()
    {
        // 01:00–04:00 on the spring-forward night really lasts 2 hours (02:00–03:00 never happens)
        var cal = BusinessCalendar.Create("America/New_York").On(DayOfWeek.Sunday, "01:00-04:00").Build();
        Assert.Equal(TimeSpan.FromHours(2), cal.Elapsed(
            new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.FromHours(-4))));
        // an opening time inside the gap (02:30) moves to 03:30 EDT
        var gap = BusinessCalendar.Create("America/New_York").On(DayOfWeek.Sunday, "02:30-05:00").Build();
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4)),
            gap.NextOpen(new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void Office_hours_across_daylight_saving_stay_on_the_wall_clock()
    {
        var cal = BusinessCalendar.Create("Europe/London").Weekdays("09:00-17:00").Build();
        // Fri 2026-10-23 16:00 BST + 2h → Mon 2026-10-26 10:00 GMT (clocks went back on Sunday)
        var due = cal.Add(new DateTimeOffset(2026, 10, 23, 16, 0, 0, TimeSpan.FromHours(1)), TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), due);
        Assert.Equal(TimeSpan.Zero, due.Offset);
    }

    // ------------------------------------------------------------------ properties

    [Fact]
    public void Add_and_Elapsed_agree_on_random_inputs()
    {
        var rnd = new Random(20261103);
        var calendars = new[] { Office, NewYork24, BusinessCalendar.Create("Europe/London").Weekdays("09:00-12:30", "13:30-17:00").On(DayOfWeek.Saturday, "10:00-14:00").Build() };
        for (var i = 0; i < 3000; i++)
        {
            var cal = calendars[i % calendars.Length];
            var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rnd.Next(0, 60 * 24 * 730));
            var amount = TimeSpan.FromMinutes(rnd.Next(1, 60 * 200));
            var due = cal.Add(from, amount);
            Assert.True(due > from);
            Assert.Equal(amount, cal.Elapsed(from, due));
            // nothing open is skipped: the business time from `from` to just before `due` is less
            Assert.True(cal.Elapsed(from, due.AddMinutes(-1)) < amount);
        }
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    public void Overlapping_hours_are_rejected()
    {
        var e = Assert.Throws<InvalidOperationException>(() =>
            BusinessCalendar.Create("Asia/Phnom_Penh").Weekdays("08:00-12:00", "11:00-17:00").Build());
        Assert.Contains("overlap", e.Message);
        Assert.Contains("Monday", e.Message);
    }

    [Fact]
    public void A_calendar_with_no_hours_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => BusinessCalendar.Create("Asia/Phnom_Penh").Build());

    [Fact]
    public void Unknown_time_zone_is_rejected() =>
        Assert.Throws<ArgumentException>(() => BusinessCalendar.Create("Mars/Olympus_Mons"));

    [Theory]
    [InlineData("17:00-08:00")]
    [InlineData("08:00")]
    [InlineData("08:00-25:00")]
    [InlineData("8am-5pm")]
    [InlineData("09:00-09:00")]
    public void Bad_ranges_are_rejected(string text) => Assert.Throws<FormatException>(() => TimeRange.Parse(text));

    [Fact]
    public void Ranges_print_as_they_are_written()
    {
        Assert.Equal("08:00-17:00", TimeRange.Parse("8:00-17:00").ToString());
        Assert.Equal("22:00-24:00", TimeRange.Parse("22:00-24:00").ToString());
    }

    [Fact]
    public void A_calendar_closed_for_good_reports_it_instead_of_looping()
    {
        var cal = BusinessCalendar.Create("Asia/Phnom_Penh").Weekdays("09:00-17:00")
            .Closed(new DateOnly(2026, 1, 1), new DateOnly(2037, 1, 1)).Build();
        Assert.Throws<InvalidOperationException>(() => cal.NextOpen(Local(2026, 6, 1, 9)));
    }
}
