# Changelog

## 0.1.0 (unreleased)

First version.

- `BusinessCalendar`: weekly hours with split shifts, holidays (single, ranges, every year), special hours on dates, any time zone with daylight-saving handling. `IsOpen`, `NextOpen`, `Add`, `Elapsed`, `OpenPeriods`, `HoursOn`, `DayName`, `AlwaysOpen`.
- `SlaTimer`: start, pause, resume, stop; `StatusAt` with elapsed, remaining, due time, at-risk and breach; `FromEvents` to rebuild from storage.
