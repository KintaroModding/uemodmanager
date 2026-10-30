# Builds one GitHub release: the app with and without the optional catalog (the openmods page).
#   dist\uemm-openmods\UEModManager.exe   with the catalog
#   dist\uemm\UEModManager.exe            without it
#   dist\release\*.zip                    one zip each, ready to upload
# Each exe is a single file, framework dependent (needs the .NET 8 Desktop Runtime). Each publish
# checks its exe (CheckPublishedExe in UEManager.UI.csproj): without the catalog, the exe must not
# contain the word "openmods" anywhere.
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

# The zip names are part of the update check: each build finds its own zip in a release by this name.
foreach ($build in @{ Name = "uemm-openmods"; Catalog = "true" }, @{ Name = "uemm"; Catalog = "false" }) {
    $out = Join-Path $dist $build.Name
    # The app keeps its data in a Data folder next to the exe when it is run from here: never delete that.
    if (Test-Path $out) { Get-ChildItem $out -Exclude "Data" | Remove-Item -Recurse -Force }
    dotnet publish $project -c $Configuration -o $out "-p:Catalog=$($build.Catalog)" "-p:GameProfile=$GameProfile" "-p:DebugType=none"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Compress-Archive -Path (Join-Path $out "UEModManager.exe") -DestinationPath (Join-Path $release "$($build.Name)_${GameProfile}_$version.zip")
}

Write-Host "Published to $dist; both exes passed their check."
Get-ChildItem $release | ForEach-Object { Write-Host "  $($_.Name)" }
