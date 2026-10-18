# ClioReport Designer for Visual Studio 2022 / 2026

Opens ClioReport report files (`*.clio`, JSON inside) in the web designer inside Visual Studio, with PDF preview and RDLC import.

- **Tools > ClioReport Designer** opens an empty designer (Save asks for a file name).
- Double-click a `.clio` file in Solution Explorer (works in Folder View too), or right-click > **Open in ClioReport Designer**. Each file gets its own tab; `Ctrl+S` in the designer writes the file. A dot in the tab title marks unsaved changes; closing the tab does **not** ask, so save first.

- **Add New Item > ClioReport Report** creates a blank `.clio` in C# projects (not available in Folder View).

## How it works

Visual Studio runs on .NET Framework, so it cannot load the .NET 8 ClioReport libraries. The extension starts the designer host (`src/ClioReport.Designer`, shipped inside the extension in `Host\`) as a child process on a free local port and shows it in a WebView2 control. File reads and writes go through a small message bridge. Preview and RDLC import are served by that host.

## Requirements (on the machine that uses the extension)

- Visual Studio 2022 or 2026 (x64 or Arm64). The extension targets the 17.x API, which Visual Studio 2026 loads without changes.
- .NET 8 runtime (`dotnet` on the path, or in `Program Files\dotnet`)
- WebView2 runtime (already installed with current Windows and Microsoft Edge)

## Build

Needs Visual Studio 2022 or 2026 with the "Visual Studio extension development" workload and the .NET 8 SDK.

```powershell
cd src\ClioReport.Vsix
.\build-vsix.ps1        # -> bin\Release\net472\ClioReport.Vsix.vsix, double-click to install (uninstall the old version first)
```

The project is not part of `ClioReport.sln` on purpose (`dotnet test` / `dotnet pack` do not need the VSIX tooling).

To debug: open `ClioReport.Vsix.csproj` in Visual Studio after running the script once (it fills `Host\` and `webview2\`), set the project as startup project and press F5; it starts the experimental instance.
