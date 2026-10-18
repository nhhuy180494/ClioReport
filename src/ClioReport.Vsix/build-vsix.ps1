# Builds ClioReport.Vsix.vsix. Needs Visual Studio 2022 or 2026 (with the "Visual Studio extension development" workload) and the .NET 8 SDK.
#   .\build-vsix.ps1            -> bin\Release\ClioReport.Vsix.vsix
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# 1. The designer host (framework-dependent: the machine running Visual Studio needs the .NET 8 runtime).
Remove-Item Host -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish ..\ClioReport.Designer\ClioReport.Designer.csproj -c Release -o Host --no-self-contained
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# 2. MSBuild from Visual Studio (the VSIX targets do not run under "dotnet build").
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild) {
    # vswhere is missing or found nothing: look in the usual install folders (2022, 2026 = folder 18; 64-bit Program Files).
    $msbuild = Get-ChildItem (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe') -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $msbuild) {
    throw 'Visual Studio 2022 or later (MSBuild) was not found on this machine. Install it with the "Visual Studio extension development" workload, then run this script again.'
}
Write-Host "Using $msbuild"

& $msbuild ClioReport.Vsix.csproj /t:Restore /p:Configuration=Release /v:m
if ($LASTEXITCODE -ne 0) { throw 'restore failed' }

# 3. WebView2Loader.dll (native) comes from the NuGet package; the VSIX carries it under webview2\<arch>\.
Remove-Item webview2 -Recurse -Force -ErrorAction SilentlyContinue
$package = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2') -Directory | Sort-Object Name | Select-Object -Last 1
foreach ($arch in 'x64', 'x86', 'arm64') {
    $source = Join-Path $package.FullName "runtimes\win-$arch\native\WebView2Loader.dll"
    if (Test-Path $source) {
        New-Item -ItemType Directory -Force "webview2\$arch" | Out-Null
        Copy-Item $source "webview2\$arch\"
    }
}

# 4. Build (the Host\ and webview2\ folders now exist, so the project picks them up).
# Item template: Visual Studio loads extension templates from a .zip (the folder holds the template sources).
$tpl = Join-Path $PSScriptRoot 'ItemTemplates\CSharp\ClioReport'
Remove-Item (Join-Path $tpl 'ClioReport.zip') -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $tpl 'ClioReport.vstemplate'), (Join-Path $tpl 'Blank.clio') -DestinationPath (Join-Path $tpl 'ClioReport.zip')

# VSSDK targets ship with Visual Studio; SDK-style projects import them explicitly.
$msbuildRoot = Split-Path (Split-Path (Split-Path (Split-Path $msbuild)))
$vssdk = Get-ChildItem (Join-Path $msbuildRoot 'MSBuild\Microsoft\VisualStudio') -Recurse -Filter Microsoft.VsSDK.targets -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $vssdk) { throw "Microsoft.VsSDK.targets not found under $msbuildRoot. In Visual Studio Installer add the workload 'Visual Studio extension development'." }
Write-Host "VSSDK targets: $($vssdk.FullName)"
& $msbuild ClioReport.Vsix.csproj /p:Configuration=Release /p:VsSdkTargetsFile="$($vssdk.FullName)" /v:m
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$vsix = Get-ChildItem $PSScriptRoot -Recurse -Filter *.vsix | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $vsix) { throw 'Build ran but no .vsix was produced (see build output above).' }
Write-Host "`nBuilt: $($vsix.FullName)"
