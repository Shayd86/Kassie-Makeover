@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "APPID=kassie-makeover"
set "VERSION=0.4.1"
set "EXE=Kassie-Makeover.exe"
set "ROOT=%~dp0"
set "PROJECT=%ROOT%src\Kassie.Makeover\Kassie.Makeover.csproj"
set "WORK=%TEMP%\KassieMakeover-v041-%RANDOM%%RANDOM%"
set "PUBLISH=%WORK%\publish"
set "PACKAGE=%WORK%\package"
set "OUTZIP=%USERPROFILE%\Downloads\Kassie-Makeover-v%VERSION%.zip"

echo ============================================================
echo   Kassie Makeover v%VERSION% - Native C# Hub Update Builder
echo ============================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET SDK was not found in PATH.
  echo Install the .NET SDK and run this builder again.
  pause
  exit /b 1
)

if not exist "%PROJECT%" (
  echo ERROR: Project not found:
  echo %PROJECT%
  pause
  exit /b 1
)

for /f "delims=" %%V in ('dotnet --version') do set "DOTNETVER=%%V"
echo .NET SDK: %DOTNETVER%
echo.

mkdir "%WORK%" >nul 2>nul
mkdir "%PUBLISH%" >nul 2>nul
mkdir "%PACKAGE%" >nul 2>nul

echo [1/5] Restoring C# project...
dotnet restore "%PROJECT%" -r win-x64
if errorlevel 1 goto :failed

echo [2/5] Publishing self-contained Windows x64 EXE...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -o "%PUBLISH%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:Version=%VERSION% -p:FileVersion=%VERSION%.0 -p:AssemblyVersion=%VERSION%.0
if errorlevel 1 goto :failed

if not exist "%PUBLISH%\%EXE%" (
  echo ERROR: Published EXE was not created.
  goto :failed
)

echo [3/5] Verifying version and hashing EXE...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe='%PUBLISH%\%EXE%'; $v=(Get-Item -LiteralPath $exe).VersionInfo.FileVersion; if(-not $v.StartsWith('%VERSION%')){ throw ('EXE FileVersion mismatch. Expected %VERSION%, got '+$v) }; $h=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant(); $m=[ordered]@{app='%APPID%';version='%VERSION%';exe='%EXE%';sha256=$h}|ConvertTo-Json; Set-Content -LiteralPath '%PACKAGE%\kassie-release.json' -Value $m -Encoding UTF8; Copy-Item -LiteralPath $exe -Destination '%PACKAGE%\%EXE%' -Force; Write-Host ('EXE version: '+$v); Write-Host ('SHA-256: '+$h)"
if errorlevel 1 goto :failed

echo [4/5] Creating Hub update ZIP...
if exist "%OUTZIP%" del /q "%OUTZIP%" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -LiteralPath '%PACKAGE%\%EXE%','%PACKAGE%\kassie-release.json' -DestinationPath '%OUTZIP%' -Force"
if errorlevel 1 goto :failed

echo [5/5] Validating final Hub package...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$zip='%OUTZIP%'; Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[IO.Compression.ZipFile]::OpenRead($zip); try { $names=@($z.Entries | ForEach-Object FullName); if($names.Count -ne 2 -or $names -notcontains '%EXE%' -or $names -notcontains 'kassie-release.json'){ throw ('Unexpected ZIP contents: '+($names -join ', ')) } } finally { $z.Dispose() }; Write-Host ('Validated: '+$zip)"
if errorlevel 1 goto :failed

rmdir /s /q "%WORK%" >nul 2>nul

echo.
echo ============================================================
echo DONE
echo ============================================================
echo Your Hub update is now in Downloads:
echo %OUTZIP%
echo.
echo Leave the ZIP there and do not extract it.
echo Kassie Hub can install it from Downloads.
echo.
pause
exit /b 0

:failed
echo.
echo ============================================================
echo BUILD FAILED
echo ============================================================
echo Scroll up for the compiler or packaging error.
echo Temporary files were left at:
echo %WORK%
echo.
pause
exit /b 1
