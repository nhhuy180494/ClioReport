# ClioReport

Band-based report builder library for .NET (MIT).

> Status: phase 1 - engine only, no UI. Skeleton, not implemented yet.

## Layout

- `src/ClioReport.Core` - versioned JSON model, band/group/aggregate layout, expression sandbox, `IDataContext`
- `src/ClioReport.Pdf` - PDF renderer (separate package)
- `tests/ClioReport.Tests`
- `samples/PhieuXuatKho` - sample delivery-note report
- `docs`

## Build

```
dotnet build ClioReport.sln
dotnet test
```
