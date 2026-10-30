# Builds one GitHub release:
#   dist\uemm\UEModManager.exe   the app
#   dist\release\*.zip          its zip, ready to upload
# The exe is a single file, framework dependent (needs the .NET 8 Desktop Runtime). The publish
# checks it (CheckPublishedExe in UEManager.UI.csproj).
param(
    [string]$Configuration = "Release",
    [string]$GameProfile = "phantom-blade-zero"
)

$ErrorActionPreference = "Stop"
$dist = Join-Path $PSScriptRoot "dist"
$project = Join-Path $PSScriptRoot "src\UEManager.UI\UEManager.UI.csproj"
$version = ([xml](Get-Content (Join-Path $PSScriptRoot "src\Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

dotnet test (Join-Path $PSScriptRoot "src\UEManager.slnx") -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$release = Join-Path $dist "release"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force $release | Out-Null

# The zip name is part of the update check: the app finds its zip in a release by this name.
$out = Join-Path $dist "uemm"
# The app keeps its data in a Data folder next to the exe when it is run from here: never delete that.
if (Test-Path $out) { Get-ChildItem $out -Exclude "Data" | Remove-Item -Recurse -Force }
dotnet publish $project -c $Configuration -o $out "-p:GameProfile=$GameProfile" "-p:DebugType=none"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Compress-Archive -Path (Join-Path $out "UEModManager.exe") -DestinationPath (Join-Path $release "uemm_${GameProfile}_$version.zip")

Write-Host "Published to $dist; the exe passed its check."
Get-ChildItem $release | ForEach-Object { Write-Host "  $($_.Name)" }
