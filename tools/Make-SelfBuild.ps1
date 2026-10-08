param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repo "Kassie-Makeover-v$Version-SelfBuild.cmd"
}

$temp = Join-Path $env:TEMP ("KassieMakeover-SelfBuild-" + [Guid]::NewGuid().ToString("N"))
$zip = Join-Path $temp "source.zip"
New-Item -ItemType Directory -Path $temp -Force | Out-Null

try {
    Push-Location $repo
    try {
        & git archive --format=zip --output="$zip" HEAD
        if ($LASTEXITCODE -ne 0) { throw "git archive failed." }
    }
    finally {
        Pop-Location
    }

    if (!(Test-Path $zip)) { throw "Source ZIP was not created." }

    $compact = $Version.Replace(".", "")
    $header = @"
@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "SELF=%~f0"
set "WORK=%TEMP%\KassieMakeover-v$compact-%RANDOM%%RANDOM%"
set "PAYLOADZIP=%WORK%\source.zip"
set "SOURCE=%WORK%\source"

cls
echo ============================================================
echo   Kassie Makeover v$Version - Native C# Local Builder
echo ============================================================
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET SDK was not found in PATH.
  pause
  exit /b 1
)
for /f "delims=" %%V in ('dotnet --version') do set "DOTNETVER=%%V"
echo .NET SDK: %DOTNETVER%
echo.
mkdir "%WORK%" >nul 2>nul
mkdir "%SOURCE%" >nul 2>nul
echo [1/3] Unpacking Kassie Makeover v$Version source...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=[IO.File]::ReadAllText($env:SELF); $m=('###KASSIE_'+'PAYLOAD###'); $i=$p.IndexOf($m); if($i -lt 0){throw 'Embedded payload not found'}; $s=$p.Substring($i+$m.Length); $bytes=[Convert]::FromBase64String(($s -replace '\s','')); [IO.File]::WriteAllBytes($env:PAYLOADZIP,$bytes); Expand-Archive -LiteralPath $env:PAYLOADZIP -DestinationPath $env:SOURCE -Force"
if errorlevel 1 goto :failed
echo [2/3] Building native C# EXE and Hub package...
call "%SOURCE%\BUILD-HUB-UPDATE.cmd"
if errorlevel 1 goto :failed
echo [3/3] Cleaning temporary build files...
rmdir /s /q "%WORK%" >nul 2>nul
echo.
echo ============================================================
echo DONE
echo ============================================================
echo Your Hub update is now in Downloads:
echo %USERPROFILE%\Downloads\Kassie-Makeover-v$Version.zip
echo.
echo Leave the ZIP there and do not extract it.
echo.
pause
exit /b 0
:failed
echo.
echo Build failed. Scroll up for the real compiler or packaging error.
echo Temporary files were left at:
echo %WORK%
echo.
pause
exit /b 1
###KASSIE_PAYLOAD###
"@

    $payload = [Convert]::ToBase64String([IO.File]::ReadAllBytes($zip))
    $lines = for ($i = 0; $i -lt $payload.Length; $i += 76) {
        $payload.Substring($i, [Math]::Min(76, $payload.Length - $i))
    }

    $full = $header + ($lines -join [Environment]::NewLine) + [Environment]::NewLine
    [IO.File]::WriteAllText($OutputPath, $full, [Text.UTF8Encoding]::new($false))

    Write-Host "Created $OutputPath"
    Write-Host ("Size: {0:N0} bytes" -f (Get-Item $OutputPath).Length)
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
