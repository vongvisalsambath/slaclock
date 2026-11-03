// The examples from the README, runnable: dotnet run --project samples/QuickStart
using SlaClock;

var tz = TimeSpan.FromHours(7);

// 1. Describe when you're open
var calendar = BusinessCalendar.Create("Asia/Phnom_Penh")
    .Weekdays("08:00-12:00", "13:00-17:00")                  // an hour for lunch
    .On(DayOfWeek.Saturday, "08:00-12:00")
    .Closed(new DateOnly(2026, 11, 9), "Independence Day")
    .ClosedEveryYear(1, 1, "International New Year")
    .Hours(new DateOnly(2026, 12, 31), "New Year's Eve", ["08:00-12:00"])
    .Build();

// 2. Deadlines and elapsed business time
var created = new DateTimeOffset(2026, 11, 6, 15, 30, 0, tz);  // Friday 15:30
Console.WriteLine($"Due (16 business hours): {calendar.Add(created, TimeSpan.FromHours(16)):ddd yyyy-MM-dd HH:mm zzz}");
Console.WriteLine($"Open on Monday 9 Nov?    {calendar.IsOpen(new DateTimeOffset(2026, 11, 9, 10, 0, 0, tz))} ({calendar.DayName(new DateOnly(2026, 11, 9))})");
Console.WriteLine($"Next open after Fri 17:00: {calendar.NextOpen(new DateTimeOffset(2026, 11, 6, 17, 0, 0, tz)):ddd HH:mm}");
Console.WriteLine($"Business time Fri 15:30 → Tue 10:00: {calendar.Elapsed(created, new DateTimeOffset(2026, 11, 10, 10, 0, 0, tz))}");

// 3. An SLA timer that pauses while you wait on the customer
var sla = new SlaTimer(calendar, target: TimeSpan.FromHours(16), atRiskAt: 0.8);
sla.Start(created);
sla.Pause(new DateTimeOffset(2026, 11, 7, 9, 0, 0, tz), "Waiting on customer");
sla.Resume(new DateTimeOffset(2026, 11, 10, 14, 0, 0, tz));

var now = new DateTimeOffset(2026, 11, 11, 11, 0, 0, tz);
var s = sla.StatusAt(now);
Console.WriteLine();
Console.WriteLine($"At {now:ddd HH:mm}: {s.State}, used {s.Elapsed}, left {s.Remaining}, due {s.DueAt:ddd HH:mm}, at risk: {s.IsAtRisk}, breached: {s.IsBreached}");
