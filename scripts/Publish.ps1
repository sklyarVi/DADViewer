[CmdletBinding()]
param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected release version.' }
$flavor = if ($FrameworkDependent) { 'framework-dependent' } else { 'portable' }
$name = "DADViewer-$version-win-x64-$flavor"
$staging = Join-Path $repo ('artifacts/releases/staging-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $staging $name
New-Item -ItemType Directory -Path $publish -Force | Out-Null
$contained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish (Join-Path $repo 'DADViewer.csproj') -c Release -r win-x64 --self-contained $contained -o $publish -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
# Preserve the license texts shipped with the exact runtime packs used in this build.
if (!$FrameworkDependent) {
    $config = Get-Content -LiteralPath (Join-Path $publish 'DADViewer.runtimeconfig.json') -Raw | ConvertFrom-Json
    $assets = Get-Content -LiteralPath (Join-Path $repo 'obj/project.assets.json') -Raw | ConvertFrom-Json
    foreach ($framework in $config.runtimeOptions.includedFrameworks) {
        $relative = $framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version
        $package = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$package) { throw "Runtime package not found for license collection: $relative" }
        $notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE(\..*)?|THIRD.?PARTY.*NOTICES.*)$' })
        if (!($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Runtime license missing: $relative" }
        $licenseDirectory = Join-Path $publish ('licenses/' + $framework.name + '-' + $framework.version)
        New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $licenseDirectory }
    }
}
$requirements = if ($FrameworkDependent) { '.NET 10 Desktop Runtime x64 must be installed.' } else { '.NET is included; no separate runtime installation is required.' }
@"
DAD Viewer $version for Windows x64

Extract the entire folder and run DADViewer.exe. $requirements
Open a DAD file with Open DAD, drag and drop, or a command-line path.
Time is in minutes and wavelength in nm, according to the original task.
Intensity units are not specified. Input data is never modified.

Click a plot to select a point. Wheel zooms; right/middle drag pans.
Shift + left drag selects a zoom area; double click resets one plot.
Reset all plots resets the 2D map and both slices.
CSV exports the complete selected slice with round-trip numeric precision.
PNG exports the visible plot at 192 dpi with source, selection and scale.
Viridis, Jet and Grayscale support 2 to 256 color levels.
3D uses adaptive sampling (up to 128 samples per axis), retaining the global
minimum and maximum at their measured coordinates. Smaller local peaks and
peak widths may still be approximated; use slices/CSV for measurements.

Analyze / Compare opens numerical analysis of a primary slice and an optional
reference file. Choose wavelength for a chromatogram or time for a spectrum.
Set the baseline, minimum local prominence (file units) and minimum distance,
then Analyze. The graph and calculation cover the full slice, independent of zoom.
Height is relative to the baseline; area is integrated between adjacent valleys.
Negative peaks use positive height and signed negative area. Unresolved widths
are blank. This first version does not deconvolve overlapping peaks or identify
substances. Review the baseline and integration bounds before using results.
Comparison uses the nearest measured reference slice and linear interpolation
only within shared axis coverage; no alignment or normalization is performed.
Export peaks CSV includes parameters; comparison CSV includes paired raw values
and differences. No AI service or network connection is required.

Settings and recent file paths: %LOCALAPPDATA%\DADViewer\settings.json
Error logs: %LOCALAPPDATA%\DADViewer\diagnostics.log
Uncheck Reopen last file on startup to start with an empty workspace.
To reset all preferences, close the app and rename settings.json.

Limits: 8 million intensity values; the map resamples at 1000 x 500.
Zoom reveals more detail; CSV retains all points in a selected slice.
Licensed under MIT; see LICENSE. The release is unsigned.
Third-party components retain their licenses; see ThirdPartyNotices.txt and runtime notices.
"@ | Set-Content -LiteralPath (Join-Path $publish 'START-HERE.txt') -Encoding utf8
$zip = Join-Path $repo "artifacts/releases/$name.zip"
Compress-Archive -LiteralPath $publish -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $name.zip" | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output "Release: $zip"
Write-Output "Unpacked: $publish"
Write-Output "SHA256: $hash"
