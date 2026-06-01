@echo off
setlocal enabledelayedexpansion

:: =============================================================================
::  build.bat
::  Virtuart4D - Navisworks Datasmith Exporter Plugin Build Automation
::  Pipeline: Clean build/ -> Compile (Debug with symbols) -> Assemble Bundle -> Logs
:: =============================================================================

title Virtuart4D - Build Automation

set "PROJECT_DIR=%~dp0"
set "BUILD_DIR=%PROJECT_DIR%build"
set "BUNDLE_DIR=%BUILD_DIR%\Virtuart4DNavisworks.bundle"
set "LOGS_DIR=%PROJECT_DIR%logs"

:: Default configuration is now DEBUG to preserve PDB symbols for active debugging
set "CONFIG=Debug"

:: Process arguments (allows setting CONFIG to Release if needed)
for %%A in (%*) do (
    set "ARG=%%A"
    if /i "!ARG!"=="/release" set "CONFIG=Release"
    if /i "!ARG!"=="/debug" set "CONFIG=Debug"
)

:: Create logs folder if it doesn't exist
if not exist "%LOGS_DIR%" mkdir "%LOGS_DIR%" 2>nul

:: Generate a robust, locale-independent timestamp using PowerShell
for /f "usebackq tokens=*" %%i in (`powershell -NoProfile -Command "Get-Date -Format 'yyyyMMdd_HHmmss'"`) do set "TIMESTAMP=%%i"
set "LOG_FILE=%LOGS_DIR%\build_%TIMESTAMP%.log"

echo ============================================================ > "%LOG_FILE%"
echo  Virtuart4D - Navisworks Datasmith Exporter Build >> "%LOG_FILE%"
echo  Configuration: %CONFIG% >> "%LOG_FILE%"
echo  Date/Time: %TIMESTAMP% >> "%LOG_FILE%"
echo ============================================================ >> "%LOG_FILE%"

echo.
echo ============================================================
echo  Virtuart4D - Navisworks Datasmith Exporter Build
echo  Pipeline: Clean -^> Compile (%CONFIG% with Symbols) -^> Logs
echo ============================================================
echo.

:: -------------------------------------------------------
:: 1. Verify dotnet SDK
:: -------------------------------------------------------
echo [1/4] Checking dotnet SDK...
(echo [1/4] Checking dotnet SDK...) >> "%LOG_FILE%"
where dotnet >nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] dotnet SDK not found. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
    goto :error
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do (
    echo        Found: dotnet %%v | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
)

:: -------------------------------------------------------
:: 2. Clean build folder
:: -------------------------------------------------------
echo.
echo [2/4] Cleaning build folder...
(echo. && echo [2/4] Cleaning build folder...) >> "%LOG_FILE%"
if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
    if !errorlevel! neq 0 (
        echo [ERROR] Failed to clean build folder. Check if files are locked. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
        goto :error
    )
)
mkdir "%BUILD_DIR%" 2>nul
echo        Cleaned and prepared build folder. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"

:: -------------------------------------------------------
:: 3. Compile project
:: -------------------------------------------------------
echo.
echo [3/4] Compiling Virtuart4DNavisworks in %CONFIG% x64...
(echo. && echo [3/4] Compiling Virtuart4DNavisworks in %CONFIG% x64...) >> "%LOG_FILE%"

:: Build project and pipe output to both console and log file live
dotnet build "%PROJECT_DIR%Virtuart4DNavisworks.csproj" ^
    -c %CONFIG% ^
    -p:PlatformTarget=x64 ^
    --output "%BUNDLE_DIR%\Contents\v22" ^
    --nologo ^
    -v minimal 2>&1 | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"

if !errorlevel! neq 0 (
    echo.
    echo [ERROR] Compilation failed. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
    goto :error
)
echo        Compilation succeeded. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"

:: -------------------------------------------------------
:: 4. Assemble Bundle (Copy PackageContents.xml)
:: -------------------------------------------------------
echo.
echo [4/4] Assembling Autodesk Application Bundle...
(echo. && echo [4/4] Assembling Autodesk Application Bundle...) >> "%LOG_FILE%"
if not exist "%PROJECT_DIR%PackageContents.xml" (
    echo [ERROR] PackageContents.xml not found in root directory. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
    goto :error
)
copy "%PROJECT_DIR%PackageContents.xml" "%BUNDLE_DIR%\" >nul
if !errorlevel! neq 0 (
    echo [ERROR] Failed to copy PackageContents.xml to bundle folder. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"
    goto :error
)
echo        PackageContents.xml copied to bundle. | powershell -NoProfile -Command "$input | Tee-Object -Append -FilePath '%LOG_FILE%'"

:: -------------------------------------------------------
:: Success Output
:: -------------------------------------------------------
echo.
echo ============================================================
echo  BUILD COMPLETED SUCCESSFULLY!
echo ============================================================
(echo. && echo ============================================================ && echo  BUILD COMPLETED SUCCESSFULLY! && echo ============================================================) >> "%LOG_FILE%"
echo.
echo  Bundle Location: %BUNDLE_DIR%
echo  Build Log saved to: %LOG_FILE%
echo.
echo  You can copy "Virtuart4DNavisworks.bundle" directly to:
echo  %%APPDATA%\Autodesk\ApplicationPlugins\
echo  to load the plugin in Navisworks Manage 2025.
echo.
goto :success

:error
echo.
echo [FAIL] Build failed. Please check details above or in: %LOG_FILE%
(echo. && echo [FAIL] Build failed.) >> "%LOG_FILE%"
pause
exit /b 1

:success
pause
exit /b 0
