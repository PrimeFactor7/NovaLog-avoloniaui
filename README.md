# NovaLog (Avalonia)

Windows log viewer for rolling Winston-style logs (including `.audit.json`), with large-file virtualization, JSON/SQL/stack-trace highlighting, merge view, and dark/light themes.

- **Runtime:** .NET 10, Avalonia UI (cross-platform UI)
- **Config:** Portable (next to exe) or `%APPDATA%\NovaLog`

## Build & run

From repo root:

```powershell
.\scripts\run.ps1
# or
dotnet run --project NovaLog.Avalonia\NovaLog.Avalonia.csproj
```

## Tests

```powershell
dotnet test NovaLog.Tests\NovaLog.Tests.csproj
```

## Projects

- **NovaLog.Avalonia** — main Avalonia app
- **NovaLog.Core** — shared core (models, services)
- **NovaLog.Tests** — xUnit + FlaUI UI tests
- **DragDropTest** — Avalonia drag/drop test harness

## Publish (single-file exe)

```powershell
dotnet publish NovaLog.Avalonia\NovaLog.Avalonia.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained
# Output: NovaLog.Avalonia\bin\Release\net10.0\win-x64\publish\NovaLog.Avalonia.exe
```

## Docs

See the WinForms **NovaLog** repo for architecture and design docs; this Avalonia port shares the same core behavior and config.
