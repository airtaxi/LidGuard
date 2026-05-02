@echo off
setlocal EnableExtensions

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
set "REPO_ROOT=%~dp0"
set "PACK_SCRIPT=%REPO_ROOT%pack-local.bat"
set "REINSTALL_SCRIPT=%REPO_ROOT%reinstall-local.bat"

cd /d "%REPO_ROOT%" || (
    set "EXIT_CODE=1"
    goto finalize
)

if not exist "%PACK_SCRIPT%" (
    echo Pack script was not found: "%PACK_SCRIPT%"
    set "EXIT_CODE=1"
    goto finalize
)

if not exist "%REINSTALL_SCRIPT%" (
    echo Reinstall script was not found: "%REINSTALL_SCRIPT%"
    set "EXIT_CODE=1"
    goto finalize
)

echo Running local pack step...
call "%PACK_SCRIPT%" --no-pause
if errorlevel 1 (
    set "EXIT_CODE=1"
    goto finalize
)

echo.
echo Running local reinstall step...
call "%REINSTALL_SCRIPT%" --no-pause
if errorlevel 1 (
    set "EXIT_CODE=1"
    goto finalize
)

set "EXIT_CODE=0"
goto finalize

:finalize
if "%EXIT_CODE%"=="0" (
    echo Done.
) else (
    echo Failed.
)

if "%NO_PAUSE%"=="0" pause

exit /b %EXIT_CODE%
