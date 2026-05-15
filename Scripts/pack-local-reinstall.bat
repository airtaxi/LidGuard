@echo off
setlocal EnableExtensions

set "NO_PAUSE=0"
set "SCRIPT_DIR=%~dp0"

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
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"
set "PACK_SCRIPT=%SCRIPT_DIR%pack-local.bat"
set "REINSTALL_SCRIPT=%SCRIPT_DIR%reinstall-local.bat"

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

call :clean_build_output_directories
if errorlevel 1 (
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

:clean_build_output_directories
echo Removing build output directories...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$repoRoot = [System.IO.Path]::GetFullPath($env:REPO_ROOT); foreach ($relativePath in @('LidGuard\bin', 'LidGuard\obj')) { $target = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath)); if (-not $target.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { Write-Error ('Refusing to remove unexpected build output path: ' + $target); exit 1 }; if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force } }"
if errorlevel 1 exit /b 1
exit /b 0
