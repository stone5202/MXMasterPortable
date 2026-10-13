$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $projectRoot 'src\Program.cs'
$outputDirectory = Join-Path $projectRoot 'build'
$outputPath = Join-Path $outputDirectory 'MXMasterWIN.exe'
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw 'Windows .NET Framework C# compiler (csc.exe) was not found.'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$iconPath = Join-Path $projectRoot 'src\app.ico'
$logoPath = Join-Path $projectRoot 'src\app.png'
if (-not (Test-Path -LiteralPath $iconPath) -or -not (Test-Path -LiteralPath $logoPath)) {
    & (Join-Path $projectRoot 'tools\make-icon.ps1')
}

& $compiler `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /warn:4 `
    "/win32icon:$iconPath" `
    "/resource:$iconPath,MXMasterWIN.app.ico" `
    "/resource:$logoPath,MXMasterWIN.app.png" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$outputPath" `
    $sourcePath

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed. csc.exe exit code: $LASTEXITCODE"
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath
Write-Host "Build succeeded: $outputPath"
Write-Host "SHA-256: $($hash.Hash)"
