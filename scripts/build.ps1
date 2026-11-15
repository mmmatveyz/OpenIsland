param(
    [switch]$Run,
    [switch]$SelfTest,
    [switch]$NoStop,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'desktop\OpenIsland\OpenIsland.csproj'
$output = Join-Path $root 'bin\OpenIsland'
$exe = Join-Path $output 'OpenIsland.exe'
$log = Join-Path $env:LOCALAPPDATA 'OpenIsland\logs\app.log'

function Resolve-Dotnet {
    $local = Join-Path $root '.dotnet\dotnet.exe'
    if (Test-Path $local) {
        & $local --version *> $null
        if ($LASTEXITCODE -eq 0) { return $local }
        Write-Host 'Local .NET SDK looks broken; falling back to PATH.' -ForegroundColor Yellow
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    Write-Host 'Local .NET SDK not found. Installing into .dotnet ...' -ForegroundColor Yellow
    $installer = Join-Path $root 'dotnet-install.ps1'
    if (-not (Test-Path $installer)) {
        throw "Missing $installer. Install .NET 8 SDK manually: https://dot.net/download"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 8.0 -InstallDir (Join-Path $root '.dotnet')
    if (-not (Test-Path $local)) { throw 'Failed to install .NET SDK.' }
    return $local
}

function Stop-RunningApp {
    $running = @(Get-Process OpenIsland -ErrorAction SilentlyContinue)
    foreach ($process in $running) {
        try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
        try { Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { }
    }
    if ($running.Count -gt 0) { Start-Sleep -Milliseconds 300 }
}

$dotnet = Resolve-Dotnet
Write-Host "SDK: $dotnet"

if ($NoStop -and (Get-Process OpenIsland -ErrorAction SilentlyContinue)) {
    Write-Host 'Warning: OpenIsland is running and -NoStop was passed; the build may fail on a locked output file.' -ForegroundColor Yellow
}
if (-not $NoStop) { Stop-RunningApp }

if ($Clean) {
    Remove-Item (Join-Path $root 'desktop\OpenIsland\obj') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $root 'desktop\OpenIsland\bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Building OpenIsland (Release) ...' -ForegroundColor Cyan
& $dotnet build $project -c Release -o $output -warnaserror --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

Write-Host "Ready: $exe" -ForegroundColor Green

if ($SelfTest) {
    Write-Host 'Running OpenCode self-test ...' -ForegroundColor Cyan
    $process = Start-Process -FilePath $exe -ArgumentList '--selftest' -WorkingDirectory $output -PassThru
    if (-not $process.WaitForExit(300000)) {
        try { $process.Kill() } catch { }
        try { $process.WaitForExit(5000) } catch { }
        Write-Host 'Self-test timed out.' -ForegroundColor Red
        if (Test-Path $log) { Get-Content $log -Tail 20 }
        exit 124
    }
    if (Test-Path $log) { Get-Content $log -Tail 20 }
    exit $process.ExitCode
}

if ($Run) {
    Start-Process -FilePath $exe -WorkingDirectory $output
    Write-Host 'OpenIsland started.' -ForegroundColor Green
}
