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
set "PROJECT_FILE=%REPO_ROOT%LidGuard\LidGuard.csproj"
set "PACKAGE_DIR=%REPO_ROOT%artifacts\packages"

cd /d "%REPO_ROOT%" || (
    set "EXIT_CODE=1"
    goto finalize
)

:retry_loop
echo Packing local LidGuard packages. Attempt !CURRENT_ATTEMPT! of %MAX_ATTEMPT_COUNT%.
call :run_once
if errorlevel 1 (
    if !CURRENT_ATTEMPT! geq %MAX_ATTEMPT_COUNT% (
        set "EXIT_CODE=1"
        goto finalize
    )

    set /a CURRENT_ATTEMPT+=1
    echo.
    echo Previous pack attempt failed. Retrying attempt !CURRENT_ATTEMPT! of %MAX_ATTEMPT_COUNT%.
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
if not exist "%PROJECT_FILE%" (
    echo LidGuard project file was not found: "%PROJECT_FILE%"
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet CLI was not found on PATH.
    exit /b 1
)

echo Detected system architecture: %NATIVE_ARCH% ^(packing win-%TOOL_ARCH% package^)

echo Removing stale %PACKAGE_VERSION% package outputs...
if exist "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg" del /f /q "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg"
if exist "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg" del /f /q "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg"
if exist "%REPO_ROOT%LidGuard\obj\Release\lidguard.%PACKAGE_VERSION%.nuspec" del /f /q "%REPO_ROOT%LidGuard\obj\Release\lidguard.%PACKAGE_VERSION%.nuspec"
if exist "%REPO_ROOT%LidGuard\obj\Release\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nuspec" del /f /q "%REPO_ROOT%LidGuard\obj\Release\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nuspec"

echo Packing lidguard %PACKAGE_VERSION%...
dotnet pack ".\LidGuard\LidGuard.csproj" -c Release
if errorlevel 1 exit /b 1

echo Packing lidguard.win-%TOOL_ARCH% %PACKAGE_VERSION%...
dotnet pack ".\LidGuard\LidGuard.csproj" -c Release -r "win-%TOOL_ARCH%"
if errorlevel 1 exit /b 1

if not exist "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg" (
    echo Expected package was not created: "%PACKAGE_DIR%\lidguard.%PACKAGE_VERSION%.nupkg"
    exit /b 1
)

if not exist "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg" (
    echo Expected package was not created: "%PACKAGE_DIR%\lidguard.win-%TOOL_ARCH%.%PACKAGE_VERSION%.nupkg"
    exit /b 1
)

exit /b 0
