[CmdletBinding()]
param([string]$Archive)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Archive) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
    $Archive = Join-Path $repo ("artifacts/releases/DADViewer-" + $props.Project.PropertyGroup.Version + "-win-x64-portable.zip")
}
$archivePath = [IO.Path]::GetFullPath($Archive)
$expectedHash = (Get-Content -LiteralPath ($archivePath + '.sha256')).Split(' ')[0]
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Archive checksum mismatch.' }
$directory = Join-Path $repo ('artifacts/release-smoke-' + [Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archivePath -DestinationPath $directory
$exe = Get-ChildItem -LiteralPath $directory -Recurse -Filter DADViewer.exe | Select-Object -First 1
if (!$exe) { throw 'Executable not found in archive.' }
foreach ($name in @('coreclr.dll', 'PresentationFramework.dll', 'ThirdPartyNotices.txt', 'LICENSE', 'START-HERE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $exe.DirectoryName $name))) { throw "Required portable file missing: $name" }
}
& (Join-Path $PSScriptRoot 'GenerateSamples.ps1') -OutputDirectory (Join-Path $directory 'samples')
foreach ($number in 1..3) {
    $sample = Join-Path $directory "samples/TestData$number.DAD"
    $profile = Join-Path $directory "profile-$number"
    $process = Start-Process -FilePath $exe.FullName -ArgumentList ('"' + $sample + '"') -WindowStyle Hidden -PassThru -Environment @{ DADVIEWER_DATA_DIRECTORY = $profile }
    try {
        if (!$process.WaitForInputIdle(10000)) { throw 'Application did not become idle.' }
        Start-Sleep -Seconds 3
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before loading.' }
        if (!$process.CloseMainWindow()) { throw 'Could not close test window gracefully.' }
        if (!$process.WaitForExit(10000)) { throw 'Application did not exit after close.' }
        if ($process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
        $settings = Get-Content -LiteralPath (Join-Path $profile 'settings.json') -Raw | ConvertFrom-Json
        if ($settings.LastFile -ne $sample -or $settings.Files.Count -ne 1 -or $settings.Files[0].Length -ne (Get-Item -LiteralPath $sample).Length) { throw 'Loaded file was not recorded correctly.' }
        if (Test-Path -LiteralPath (Join-Path $profile 'diagnostics.log')) { throw 'Unexpected application diagnostic log.' }
        Write-Output "PASS: TestData$number.DAD opened and closed, profile isolated, no diagnostics."
    }
    finally {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
Write-Output "Verified extracted release: $($exe.DirectoryName)"
