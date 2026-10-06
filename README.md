# AppTime

A desktop application library and usage tracker, built with C# and Windows Forms
(.NET 8).

AppTime keeps your regularly used apps in one library, launches them from there,
and tracks how long you actively use each one based on the **foreground window**
(not merely whether the process is running).

## Features

### Library
- Browse apps by category (All Apps, Creative, Development, Games, Utilities)
- Search the library by name
- Add applications manually or from **Suggested Applications** (running apps not
  yet in the library, filtered to user-facing software)
- Application cards with icon, category, usage total, and Start / Stop
- Edit or remove apps from the library (context menu)
- Open an app profile for details, launch control, and recent sessions

### Tracking
- Usage is credited only to the library app that owns the **active Windows window**
- Switching apps ends the previous session and starts a new one
- Background processes do not accumulate time
- Sessions and totals persist under `%AppData%\AppTime`

### Overview
- Today / This Week / Sessions summary cards
- **Today’s timeline** - 24-hour view of focused usage by category
- **Recent / Most Used** app cards
- **Top apps today** ranked by focused time
- **Pinned apps** placeholder (coming later)

### Sessions & Insights
- Sessions list with period tabs and app/category filters
- Insights with period stats, usage charts, and most-used apps

## Status

Actively developed. Core library, foreground tracking, sessions, insights, and
overview timeline are in place. Pinned apps and idle detection are not implemented
yet.

## Built With

- C# / .NET 8
- Windows Forms

## Getting Started

1. Open the project in Visual Studio or VS Code (C# extension / Dev Kit)
2. `dotnet build` to build, `dotnet run --project AppTime` to launch

Data is stored in:

```
%AppData%\AppTime\
```