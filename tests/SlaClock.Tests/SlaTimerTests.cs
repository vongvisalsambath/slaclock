namespace SlaClock.Tests;

public class SlaTimerTests
{
    private static readonly TimeSpan PP = TimeSpan.FromHours(7);
    private static DateTimeOffset Local(int d, int h, int mi = 0) => new(2026, 11, d, h, mi, 0, PP); // November 2026

    // Mon–Fri 08:00–12:00, 13:00–17:00 in Phnom Penh. 2026-11-02 is a Monday.
    private static readonly BusinessCalendar Office = BusinessCalendar.Create("Asia/Phnom_Penh")
        .Weekdays("08:00-12:00", "13:00-17:00").Build();

    [Fact]
    public void Not_started()
    {
        var s = new SlaTimer(Office, TimeSpan.FromHours(8)).StatusAt(Local(2, 9));
        Assert.Equal(SlaState.NotStarted, s.State);
        Assert.Equal(TimeSpan.Zero, s.Elapsed);
        Assert.Equal(TimeSpan.FromHours(8), s.Remaining);
        Assert.Null(s.DueAt);
        Assert.False(s.IsBreached);
    }

    [Fact]
    public void Running_counts_business_time_and_knows_the_deadline()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        sla.Start(Local(2, 10));
        var s = sla.StatusAt(Local(2, 14));                    // 10–12 and 13–14
        Assert.Equal(SlaState.Running, s.State);
        Assert.Equal(TimeSpan.FromHours(3), s.Elapsed);
        Assert.Equal(TimeSpan.FromHours(5), s.Remaining);
        Assert.Equal(Local(3, 10), s.DueAt);                   // 14–17 today (3h), then 08–10 tomorrow
        Assert.False(s.IsAtRisk);
    }

    [Fact]
    public void Pauses_do_not_count_and_hide_the_deadline()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        sla.Start(Local(2, 8));
        sla.Pause(Local(2, 10), "Waiting on customer");
        var paused = sla.StatusAt(Local(4, 16));              // two days later
        Assert.Equal(SlaState.Paused, paused.State);
        Assert.Equal(TimeSpan.FromHours(2), paused.Elapsed);
        Assert.Null(paused.DueAt);

        sla.Resume(Local(5, 9));
        var resumed = sla.StatusAt(Local(5, 9));
        Assert.Equal(SlaState.Running, resumed.State);
        Assert.Equal(TimeSpan.FromHours(2), resumed.Elapsed);
        Assert.Equal(Local(5, 16), resumed.DueAt);            // 6h left: 09–12, 13–16
    }

    [Fact]
    public void Breach_reports_when_and_by_how_much()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(4));
        sla.Start(Local(2, 9));
        var s = sla.StatusAt(Local(2, 15));                    // 09–12 (3h) + 13–15 (2h) = 5h
        Assert.True(s.IsBreached);
        Assert.Equal(Local(2, 14), s.BreachedAt);
        Assert.Equal(Local(2, 14), s.DueAt);
        Assert.Equal(TimeSpan.FromHours(1), s.Overdue);
        Assert.Equal(TimeSpan.Zero, s.Remaining);
        Assert.False(s.IsAtRisk);
    }

    [Fact]
    public void Breach_moment_is_found_across_pauses()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(4));
        sla.Start(Local(2, 8));
        sla.Pause(Local(2, 11));                               // 3h used
        sla.Resume(Local(3, 15));
        var s = sla.StatusAt(Local(3, 17));
        Assert.Equal(Local(3, 16), s.BreachedAt);               // the 4th hour ends at 16:00 on Tuesday
        Assert.Equal(TimeSpan.FromHours(1), s.Overdue);
    }

    [Fact]
    public void Exactly_on_target_is_not_a_breach()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(2));
        sla.Start(Local(2, 9));
        var s = sla.StatusAt(Local(2, 11));
        Assert.False(s.IsBreached);
        Assert.Equal(TimeSpan.Zero, s.Remaining);
        Assert.Equal(Local(2, 11), s.DueAt);
    }

    [Fact]
    public void At_risk_from_the_threshold()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(10), atRiskAt: 0.8);
        sla.Start(Local(2, 8));
        Assert.False(sla.StatusAt(Local(2, 16, 59)).IsAtRisk); // 7h59m (08–12, 13–16:59)
        Assert.True(sla.StatusAt(Local(2, 17)).IsAtRisk);      // 8h
    }

    [Fact]
    public void Stopped_keeps_its_outcome()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        sla.Start(Local(2, 8));
        sla.Stop(Local(2, 11), "Resolved");
        var later = sla.StatusAt(Local(20, 9));
        Assert.Equal(SlaState.Stopped, later.State);
        Assert.Equal(TimeSpan.FromHours(3), later.Elapsed);
        Assert.False(later.IsBreached);
        Assert.False(later.IsAtRisk);
        Assert.Null(later.DueAt);
    }

    [Fact]
    public void Status_in_the_past_ignores_later_events()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        sla.Start(Local(2, 8));
        sla.Pause(Local(2, 10));
        var before = sla.StatusAt(Local(2, 9));
        Assert.Equal(SlaState.Running, before.State);
        Assert.Equal(TimeSpan.FromHours(1), before.Elapsed);
    }

    [Fact]
    public void Can_be_rebuilt_from_stored_events()
    {
        var events = new[]
        {
            new SlaEvent(Local(2, 8), SlaEventKind.Started),
            new SlaEvent(Local(2, 9), SlaEventKind.Paused, "Waiting on customer"),
            new SlaEvent(Local(2, 13), SlaEventKind.Resumed),
        };
        var sla = SlaTimer.FromEvents(Office, TimeSpan.FromHours(8), events);
        Assert.Equal(TimeSpan.FromHours(2), sla.StatusAt(Local(2, 14)).Elapsed);
        Assert.Equal("Waiting on customer", sla.Events[1].Reason);
    }

    [Fact]
    public void Invalid_transitions_are_rejected()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        Assert.Throws<InvalidOperationException>(() => sla.Pause(Local(2, 8)));
        sla.Start(Local(2, 8));
        Assert.Throws<InvalidOperationException>(() => sla.Start(Local(2, 9)));
        Assert.Throws<InvalidOperationException>(() => sla.Resume(Local(2, 9)));
        sla.Stop(Local(2, 10));
        Assert.Throws<InvalidOperationException>(() => sla.Resume(Local(2, 11)));
    }

    [Fact]
    public void Events_out_of_order_are_rejected()
    {
        var sla = new SlaTimer(Office, TimeSpan.FromHours(8));
        sla.Start(Local(2, 10));
        Assert.Throws<InvalidOperationException>(() => sla.Pause(Local(2, 9)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Target_must_be_positive(int hours) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlaTimer(Office, TimeSpan.FromHours(hours)));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void At_risk_share_must_be_between_0_and_1(double share) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlaTimer(Office, TimeSpan.FromHours(1), share));
}
