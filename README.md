# AppTime

A desktop application library and usage tracker, built with C# and Windows Forms
(.NET 8)

AppTime lets you keep your regularly-used desktop applications (Blender, Photoshop,
VS Code, etc.) in one library, launch them from there, and - eventually - see how
much time you actually spend in each one.

## Status

Early work in progress. The current build establishes the application shell and
library view only:

- Three-column layout: top bar, sidebar navigation, application library
- Sidebar filtering by category
- Basic name search
- Add applications by browsing to an `.exe`, naming it and assigning a category
- Application cards showing name, category and usage time
- Double-click a card to launch its application
- Library persists between runs (saved as JSON in `%AppData%\AppTime`)

Not implemented yet: editing/removing applications, process detection, time
tracking, and usage history/statistics. These are planned for later stages.

## Built With

- C# / .NET 8
- Windows Forms

## Getting Started

1. Open this folder in VS Code (with the C# Dev Kit / C# extension installed)
2. `dotnet build` to build, `dotnet run` to launch