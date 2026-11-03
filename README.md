# SlaClock

**Business-hours deadlines and pausable SLA timers for .NET.**

Every helpdesk, ticketing and approval system ends up needing the same thing: *"respond within 16 business hours — not counting lunch, weekends, public holidays, or the time we spent waiting on the customer."* It looks simple and is easy to get subtly wrong. SlaClock does it once, carefully, with tests.

```csharp
var calendar = BusinessCalendar.Create("Asia/Phnom_Penh")
    .Weekdays("08:00-12:00", "13:00-17:00")
    .Closed(new DateOnly(2026, 11, 9), "Independence Day")
    .Build();

var due = calendar.Add(ticket.CreatedAt, TimeSpan.FromHours(16));
```

- **Weekly hours with split shifts**: lunch breaks, Saturday mornings, night shifts that run past midnight.
- **Holidays and special days**: single dates, date ranges, every-year dates, and half days that replace the usual hours.
- **Time zones done properly**: hours are wall-clock times in the calendar's zone; inputs can be in any offset; results come back in local time. Daylight-saving changes are handled (the business day really is an hour shorter or longer).
- **Pausable SLA timers**: start, pause ("waiting on customer"), resume, stop; then ask for elapsed, remaining, due time, at-risk and breach — now or at any past moment.
- **Fails loudly**: overlapping hours, empty calendars, unknown time zones and out-of-order events are errors, not wrong answers.
- No dependencies. .NET 8 and .NET 10. Immutable, thread-safe calendars. AOT-compatible.

## Install

```
dotnet add package SlaClock
```

## Calendars

```csharp
using SlaClock;

var calendar = BusinessCalendar.Create("Asia/Phnom_Penh")   // IANA or Windows id
    .Weekdays("08:00-12:00", "13:00-17:00")                  // Mon–Fri, an hour for lunch
    .On(DayOfWeek.Saturday, "08:00-12:00")
    .Closed(new DateOnly(2026, 11, 9), "Independence Day")   // one day
    .Closed(new DateOnly(2027, 4, 14), new DateOnly(2027, 4, 16), "Khmer New Year")  // a range, inclusive
    .ClosedEveryYear(1, 1, "International New Year")
    .Hours(new DateOnly(2026, 12, 31), "New Year's Eve", ["08:00-12:00"])            // a half day
    .Build();
```

| Question | Call |
|---|---|
| Is it open now? | `calendar.IsOpen(at)` |
| When does it next open? | `calendar.NextOpen(at)` |
| What's the deadline for *n* business hours? | `calendar.Add(from, TimeSpan.FromHours(n))` |
| How much business time passed? | `calendar.Elapsed(from, to)` |
| Which open periods fall in a window? | `calendar.OpenPeriods(from, to)` |
| What are the hours on a date? Is it a holiday? | `calendar.HoursOn(date)`, `calendar.DayName(date)` |
| 24/7, wall-clock time | `BusinessCalendar.AlwaysOpen` |

Running [`samples/QuickStart`](samples/QuickStart/Program.cs) with the calendar above:

```
Due (16 business hours): Wed 2026-11-11 10:30 +07:00
Open on Monday 9 Nov?    False (Independence Day)
Next open after Fri 17:00: Sat 08:00
Business time Fri 15:30 → Tue 10:00: 07:30:00
```

A few rules worth knowing:

- Ranges are start-inclusive and end-exclusive: open at 08:00, closed at 17:00.
- A deadline that lands exactly on closing time is returned as closing time ("due by 17:00 Friday"), not as the next morning.
- For a shift past midnight, give each day its part: `.On(Monday, "22:00-24:00").On(Tuesday, "00:00-06:00")`. They join into one period.
- Daylight saving: opening hours inside the skipped hour move forward by the length of the gap; in the repeated hour, the first occurrence is used.

## SLA timers

```csharp
var sla = new SlaTimer(calendar, target: TimeSpan.FromHours(16), atRiskAt: 0.8);

sla.Start(ticket.CreatedAt);
sla.Pause(askedCustomerAt, "Waiting on customer");
sla.Resume(customerRepliedAt);
// ... later
sla.Stop(resolvedAt, "Resolved");

SlaStatus s = sla.StatusAt(DateTimeOffset.UtcNow);
// s.State       Running / Paused / Stopped / NotStarted
// s.Elapsed     business time counted (pauses and closed hours excluded)
// s.Remaining   time left before the target
// s.DueAt       the deadline if it keeps running (null while paused)
// s.IsAtRisk    past 80 % of the target and still open
// s.IsBreached, s.BreachedAt, s.Overdue
```

From the sample (16-hour target, created Friday 15:30, paused Saturday 09:00 to Tuesday 14:00):

```
At Wed 11:00: Running, used 08:30:00, left 07:30:00, due Thu 10:30, at risk: False, breached: False
```

`StatusAt` ignores events after the moment you ask about, so it also answers "what did this look like on Monday?". To rebuild a timer from stored events, use `SlaTimer.FromEvents(calendar, target, events)`.

## Why another library?

There are other business-hours packages on NuGet. SlaClock exists because the ones I found were missing at least one of: real time-zone and daylight-saving handling, deadlines (not just elapsed time), pausable SLA clocks, half days, or validation of the configuration. If one of those already covers your case, use it.

## Development

```
dotnet test                                   # 62 tests, including daylight-saving cases and 3,000 random round trips
dotnet run --project samples/QuickStart
```

The core rule the tests enforce on random inputs: for any start and duration, `Elapsed(from, Add(from, d)) == d`, and nothing open is skipped.

## License

[MIT](LICENSE) © Vong Visalsambath
