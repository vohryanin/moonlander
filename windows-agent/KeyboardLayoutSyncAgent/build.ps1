$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $projectDir 'bin'
$outFile = Join-Path $outDir 'KeyboardLayoutSyncAgent.exe'
$sourceFile = Join-Path $projectDir 'Program.cs'

$cscCandidates = @(
  "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
  throw 'The built-in .NET Framework compiler csc.exe was not found.'
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc `
  /nologo `
  /target:winexe `
  /out:$outFile `
  /reference:System.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  $sourceFile

Write-Host "Built $outFile"
