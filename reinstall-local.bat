@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "NO_PAUSE=0"

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
    shift
    goto parse_args
)

echo Unknown option: %~1
set "EXIT_CODE=1"
goto finalize

:args_done
set "PACKAGE_VERSION=0.1.0"
set "MAX_ATTEMPT_COUNT=2"
set "CURRENT_ATTEMPT=1"
set "NATIVE_ARCH=%PROCESSOR_ARCHITECTURE%"
if defined PROCESSOR_ARCHITEW6432 set "NATIVE_ARCH=%PROCESSOR_ARCHITEW6432%"

if /I "%NATIVE_ARCH%"=="AMD64" set "TOOL_ARCH=x64"
if /I "%NATIVE_ARCH%"=="X64" set "TOOL_ARCH=x64"
if /I "%NATIVE_ARCH%"=="ARM64" set "TOOL_ARCH=arm64"
if /I "%NATIVE_ARCH%"=="X86" set "TOOL_ARCH=x86"

if not defined TOOL_ARCH (
    echo Unsupported processor architecture: "%NATIVE_ARCH%"
    set "EXIT_CODE=1"
    goto finalize
)

set "REPO_ROOT=%~dp0"
set "PACKAGE_DIR=%REPO_ROOT%artifacts\packages"
set "PACKAGE_FILE=%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg"
set "RID_PACKAGE_FILE=%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg"

cd /d "%REPO_ROOT%" || (
    set "EXIT_CODE=1"
    goto finalize
)

:retry_loop
echo Reinstalling local LidGuard tool. Attempt !CURRENT_ATTEMPT! of %MAX_ATTEMPT_COUNT%.
call :run_once
if errorlevel 1 (
    if !CURRENT_ATTEMPT! geq %MAX_ATTEMPT_COUNT% (
        set "EXIT_CODE=1"
        goto finalize
    )

    set /a CURRENT_ATTEMPT+=1
    echo.
    echo Previous reinstall attempt failed. Retrying attempt !CURRENT_ATTEMPT! of %MAX_ATTEMPT_COUNT%.
    echo.
    goto retry_loop
)

set "EXIT_CODE=0"
goto finalize

:finalize
if "%NO_PAUSE%"=="0" (
    if "%EXIT_CODE%"=="0" (
        echo Done.
    ) else (
        echo Failed.
    )
    pause
)

exit /b %EXIT_CODE%

:run_once
if not exist "%PACKAGE_FILE%" (
    echo Local package was not found: "%PACKAGE_FILE%"
    echo Run pack-local.bat first, then rerun reinstall-local.bat.
    exit /b 1
)

if not exist "%RID_PACKAGE_FILE%" (
    echo Local package was not found: "%RID_PACKAGE_FILE%"
    echo Run pack-local.bat first, then rerun reinstall-local.bat.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet CLI was not found on PATH.
    exit /b 1
)

set "TEMP_CONFIG_DIR=%TEMP%\lidguard-local-source-%RANDOM%%RANDOM%"
set "NUGET_CONFIG=%TEMP_CONFIG_DIR%\NuGet.Config"

echo Stopping running LidGuard processes...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$processes = @(Get-Process -Name lidguard,LidGuard -ErrorAction SilentlyContinue); if ($processes.Count -gt 0) { $processes | Stop-Process -Force; Start-Sleep -Milliseconds 500 }; $remainingProcesses = @(Get-Process -Name lidguard,LidGuard -ErrorAction SilentlyContinue); if ($remainingProcesses.Count -gt 0) { Write-Error 'Unable to stop all LidGuard processes.'; exit 1 }"
if errorlevel 1 goto fail

echo Detected system architecture: %NATIVE_ARCH% ^(installing --arch %TOOL_ARCH%^)

echo Creating temporary NuGet config...
mkdir "%TEMP_CONFIG_DIR%" >nul 2>nul
if errorlevel 1 goto fail

> "%NUGET_CONFIG%" (
    echo ^<?xml version="1.0" encoding="utf-8"?^>
    echo ^<configuration^>
    echo   ^<packageSources^>
    echo     ^<clear /^>
    echo     ^<add key="lidguard-local" value="%PACKAGE_DIR%" /^>
    echo     ^<add key="nuget.org" value="https://api.nuget.org/v3/index.json" /^>
    echo   ^</packageSources^>
    echo   ^<packageSourceMapping^>
    echo     ^<packageSource key="lidguard-local"^>
    echo       ^<package pattern="lidguard" /^>
    echo       ^<package pattern="lidguard.*" /^>
    echo     ^</packageSource^>
    echo     ^<packageSource key="nuget.org"^>
    echo       ^<package pattern="Microsoft.*" /^>
    echo     ^</packageSource^>
    echo   ^</packageSourceMapping^>
    echo ^</configuration^>
)
if errorlevel 1 goto fail

echo Removing local NuGet cache entries for lidguard %PACKAGE_VERSION%...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$version = $env:PACKAGE_VERSION; $architecture = $env:TOOL_ARCH; $globalPackagesLine = dotnet nuget locals global-packages --list; $globalPackagesRoot = [System.IO.Path]::GetFullPath(($globalPackagesLine -replace '^global-packages:\s*', '').Trim()); foreach ($relativePath in @('lidguard\' + $version, 'lidguard.win-' + $architecture + '\' + $version)) { $cacheTarget = [System.IO.Path]::GetFullPath((Join-Path $globalPackagesRoot $relativePath)); if ((Test-Path -LiteralPath $cacheTarget) -and $cacheTarget.StartsWith($globalPackagesRoot, [System.StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $cacheTarget -Recurse -Force } }"
if errorlevel 1 goto fail

dotnet tool list --global | findstr /R /C:"^lidguard[ ][ ]*" >nul 2>nul
if not errorlevel 1 (
    echo Uninstalling existing lidguard global tool...
    dotnet tool uninstall --global lidguard
    if errorlevel 1 goto fail
)

echo Installing lidguard %PACKAGE_VERSION% from local packages...
dotnet tool install --global lidguard --configfile "%NUGET_CONFIG%" --version "%PACKAGE_VERSION%" --arch "%TOOL_ARCH%"
if errorlevel 1 goto fail

echo Verifying lidguard command...
where lidguard
if errorlevel 1 goto fail

call lidguard --help
if errorlevel 1 goto fail

if exist "%TEMP_CONFIG_DIR%" rmdir /s /q "%TEMP_CONFIG_DIR%"
exit /b 0

:fail
if exist "%TEMP_CONFIG_DIR%" rmdir /s /q "%TEMP_CONFIG_DIR%"
exit /b 1
