[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'SampleTestData' }
if (!('DADViewer.Samples.SyntheticSamples' -as [type])) {
    Add-Type -Path (Join-Path $repo 'SampleTestData/SyntheticSamples.cs')
}
[DADViewer.Samples.SyntheticSamples]::WriteAll([IO.Path]::GetFullPath($OutputDirectory))
Write-Output "Generated three synthetic DAD examples in $OutputDirectory"
