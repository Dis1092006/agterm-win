<#
.SYNOPSIS
Publishes the two exes a user actually runs into out\ (or -OutDir), single-file and with nothing else beside them.

.DESCRIPTION
AgTerm.App is self-contained (WindowsPackageType=None, WindowsAppSDKSelfContained=true), so its ordinary
build output is ~230 files. PublishSingleFile packs them into one exe; DebugType=none drops the .pdb files
that would otherwise sit next to it. The result is AgTerm.App.exe plus agtermctl.exe and nothing more.

agtermctl publishes framework-dependent, so it stays ~1 MB and needs the .NET 8 runtime. Pass
-SelfContainedCli to make it standalone (~65 MB) for a machine without the runtime.

.PARAMETER Platform
x64 (default) or ARM64. AgTerm.App declares only these two, never AnyCPU.

.PARAMETER OutDir
Where to publish. Default: out\ in the repository root.

.PARAMETER SelfContainedCli
Bundle the .NET runtime into agtermctl.exe too, so it runs where .NET 8 is not installed.

.EXAMPLE
.\tools\publish.ps1
Publishes both exes to out\ for x64.

.EXAMPLE
.\tools\publish.ps1 -Platform ARM64 -OutDir C:\tools\agterm
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [string]$OutDir,

    [switch]$SelfContainedCli
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'out' }

# win-x64 / win-arm64: the runtime identifier the SDK wants, derived from the msbuild Platform name.
$rid = "win-$($Platform.ToLowerInvariant())"

# A stale exe left behind by an earlier run would look like a successful publish, so start clean.
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

Write-Host "Publishing AgTerm.App ($rid, single file) to $OutDir" -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src\AgTerm.App') -c Release -r $rid -p:Platform=$Platform `
    -p:PublishSingleFile=true -p:DebugType=none -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "AgTerm.App publish failed with exit code $LASTEXITCODE" }

$cliSelfContained = if ($SelfContainedCli) { 'true' } else { 'false' }
Write-Host "Publishing agtermctl ($rid, single file, self-contained=$cliSelfContained)" -ForegroundColor Cyan
dotnet publish (Join-Path $repo 'src\AgTerm.Cli') -c Release -r $rid --self-contained $cliSelfContained `
    -p:PublishSingleFile=true -p:DebugType=none -o $OutDir
if ($LASTEXITCODE -ne 0) { throw "agtermctl publish failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host "Published to $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir -Recurse -File |
    Select-Object Name, @{ Name = 'MB'; Expression = { '{0:N1}' -f ($_.Length / 1MB) } } |
    Format-Table -AutoSize
