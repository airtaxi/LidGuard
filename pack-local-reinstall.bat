@echo off
setlocal EnableExtensions

set "PACKAGE_VERSION=0.1.0"
set "NATIVE_ARCH=%PROCESSOR_ARCHITECTURE%"
if defined PROCESSOR_ARCHITEW6432 set "NATIVE_ARCH=%PROCESSOR_ARCHITEW6432%"

if /I "%NATIVE_ARCH%"=="AMD64" set "TOOL_ARCH=x64"
if /I "%NATIVE_ARCH%"=="X64" set "TOOL_ARCH=x64"
if /I "%NATIVE_ARCH%"=="ARM64" set "TOOL_ARCH=arm64"
if /I "%NATIVE_ARCH%"=="X86" set "TOOL_ARCH=x86"

if not defined TOOL_ARCH (
    echo Unsupported processor architecture: "%NATIVE_ARCH%"
    exit /b 1
)
set "REPO_ROOT=%~dp0"
set "PROJECT_FILE=%REPO_ROOT%LidGuard\LidGuard.csproj"
set "PACKAGE_DIR=%REPO_ROOT%artifacts\packages"
set "TEMP_CONFIG_DIR=%TEMP%\lidguard-local-source-%RANDOM%%RANDOM%"
set "NUGET_CONFIG=%TEMP_CONFIG_DIR%\NuGet.Config"

cd /d "%REPO_ROOT%" || exit /b 1

if not exist "%PROJECT_FILE%" (
    echo LidGuard project file was not found: "%PROJECT_FILE%"
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet CLI was not found on PATH.
    exit /b 1
)

echo Stopping running LidGuard processes...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$processes = @(Get-Process -Name lidguard,LidGuard -ErrorAction SilentlyContinue); if ($processes.Count -gt 0) { $processes | Stop-Process -Force; Start-Sleep -Milliseconds 500 }; $remainingProcesses = @(Get-Process -Name lidguard,LidGuard -ErrorAction SilentlyContinue); if ($remainingProcesses.Count -gt 0) { Write-Error 'Unable to stop all LidGuard processes.'; exit 1 }"
if errorlevel 1 goto fail

echo Detected system architecture: %NATIVE_ARCH% ^(installing --arch %TOOL_ARCH%^)

echo Removing stale 0.1.0 package outputs...
if exist "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg" del /f /q "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg"
if exist "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg" del /f /q "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg"
if exist "%REPO_ROOT%LidGuard\obj\Release\lidguard.%PACKAGE_VERSION%.nuspec" del /f /q "%REPO_ROOT%LidGuard\obj\Release\lidguard.%PACKAGE_VERSION%.nuspec"
if exist "%REPO_ROOT%LidGuard\obj\Release\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nuspec" del /f /q "%REPO_ROOT%LidGuard\obj\Release\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nuspec"

echo Packing lidguard %PACKAGE_VERSION%...
dotnet pack ".\LidGuard\LidGuard.csproj" -c Release
if errorlevel 1 goto fail

echo Packing lidguard.win-%TOOL_ARCH% %PACKAGE_VERSION%...
dotnet pack ".\LidGuard\LidGuard.csproj" -c Release -r "win-%TOOL_ARCH%"
if errorlevel 1 goto fail

if not exist "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg" (
    echo Expected package was not created: "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg"
    goto fail
)

if not exist "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg" (
    echo Expected package was not created: "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg"
    goto fail
)

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

lidguard --help
if errorlevel 1 goto fail

echo Done.
goto cleanup_success

:fail
echo Failed.
set "EXIT_CODE=1"
goto cleanup

:cleanup_success
set "EXIT_CODE=0"
goto cleanup

:cleanup
if exist "%TEMP_CONFIG_DIR%" rmdir /s /q "%TEMP_CONFIG_DIR%"
exit /b %EXIT_CODE%

pause
