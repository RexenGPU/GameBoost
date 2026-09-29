# GameBoost

A local Windows assistant that analyzes and optimizes your PC for gaming.
Everything runs **without an account, without a network connection, without any data leaving your machine** — all measurements are real, read from your hardware.

## Download and run (no build needed)

Get the ready-to-use build from the [Releases page](https://github.com/RexenCPU/GameBoost/releases):

1. Download **GameBoost-1.0.0-win-x64.zip**
2. Unzip it anywhere (keep all files together)
3. Double-click **GameBoost.exe**

The build is self-contained: no installation, no .NET runtime to install.
SmartScreen may warn the first time (the executable is not code-signed): click "More info" then "Run anyway".

## What GameBoost does

- **My PC**: complete hardware sheet (CPU, memory, motherboard, displays, DirectX, peripherals).
- **GPU**: driver, driver age, temperatures and supported technologies (DLSS, FSR, ray tracing…).
- **Storage**: free space, SMART health, temperatures, disk type, measured throughput.
- **Games**: automatic detection of installed games (Steam, Epic, Ubisoft, Xbox, GOG, Battle.net, Riot, custom folders) with real icons.
- **Profiles**: per-game graphics settings, saved and re-applied (141 reversible settings tested).
- **Applications**: running processes, sorted by usage, targeted close (critical processes are protected).
- **Analysis**: 18 checks of your configuration with explanation, impact and fix; automatic corrections only when they are safe.
- **Boost**: a 3-step wizard (configuration, summary, execution). Every change is explained
  before being applied, backed up, and **fully reversible in one click**
  ("End session and restore").
- **Monitoring**: real FPS (from Windows presentation events), frame time, CPU, GPU, RAM, temperatures, in-game overlay.
- **History**: sessions recorded locally, A/B comparison of two sessions.
- **Reports**: HTML export readable in any browser (printable to PDF).

## Principles

- No invented numbers: if a value cannot be measured, the interface says "Not available".
- Nothing is changed without a summary and your confirmation.
- Every modification is reversible and backed up in `%LOCALAPPDATA%\GameBoost\backups`.
- Defender, the firewall, system files and critical processes are never touched.
- No data ever leaves your PC (the database and logs are local).

## Languages

The interface language is chosen in **Settings → Interface language** and applied instantly (no restart).

- Automatic (follows the Windows language)
- English
- Français
- Deutsch
- Español

Adding a language: duplicate the `.resx` files in `src\GameBoost.App\Resources\`,
name the copies `Strings_<Group>.<culture>.resx` (e.g. `Strings_Shell.de.resx`),
translate the values, and add the culture to the list in `src\GameBoost.Core\Localization\Loc.cs`.
Missing keys automatically fall back to English.

## Requirements

- Windows 10 or 11, 64-bit.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) if you use the
  compiled build; nothing to install if you build with the SDK.

## Build

With the script:

```powershell
.\build.ps1
```

Or directly:

```powershell
dotnet build GameBoost.slnx -c Release
```

The executable is produced in `artifacts\build\GameBoost.App\release\GameBoost.exe`
(or `src\GameBoost.App\bin\Release\net10.0-windows\GameBoost.exe` depending on the method).

To publish a self-contained build into `publish\`:

```powershell
.\build.ps1 -Publish
```

## Administrator rights

GameBoost starts as a normal user. Without administrator rights everything works **except**:
- CPU temperatures and some hardware sensors;
- the full SMART health of disks.

Two options (Settings page):
- "Relaunch as administrator now";
- "Start as administrator" (the app then relaunches elevated at the next start,
  Windows shows the usual authorization prompt).

## Local data

Everything is stored in `%LOCALAPPDATA%\GameBoost`:

| Folder / file | Content |
|---|---|
| `gameboost.db` | games, profiles, sessions, analyses, overlay settings |
| `settings.json` | application preferences |
| `logs\` | detailed log (useful when troubleshooting) |
| `exports\` | generated HTML reports |
| `backups\` | backups made before each system change |
| `cache\` | game and process icons |

To delete everything: close GameBoost and remove that folder.

## Known limitations (stated honestly in the application too)

- FPS is only measurable for a game that is **running** and tracked by the monitoring page;
  without a tracked game the page shows "—".
- The overlay does not appear over some exclusive-fullscreen games.
- Hardware sensors depend on your motherboard; some values may be missing
  even with administrator rights.
- The analysis engine currently produces its detail texts (check explanations, boost step
  descriptions, HTML report content) in French only; the entire interface itself is localized.

## Code layout

```
src\GameBoost.Core      business logic (hardware, disks, monitoring, processes,
                        games, profiles, analysis, boost, history, reports, overlay)
src\GameBoost.App       WPF interface (12 pages, light/dark theme, FPS overlay)
tools\make-icon.ps1     regenerates the application icon
tools\dbinspect         small utility to inspect the local database
```

Personal project: use, modify and build freely.
