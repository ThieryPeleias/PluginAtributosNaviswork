@echo off
setlocal enabledelayedexpansion

:: =============================================================================
::  build.bat
::  Virtuart4D - Navisworks Datasmith Exporter Plugin Build Automation
::  Pipeline: Clean build/ folder -> Compile (Release) -> Prepare Bundle
:: =============================================================================

title Virtuart4D - Build Automation

set "PROJECT_DIR=%~dp0"
set "BUILD_DIR=%PROJECT_DIR%build"
set "BUNDLE_DIR=%BUILD_DIR%\Virtuart4DNavisworks.bundle"

echo.
echo ============================================================
echo  Virtuart4D - Navisworks Datasmith Exporter Build
echo  Pipeline: Clean -^> Compile (Release) -^> Assemble Bundle
echo ============================================================
echo.

:: -------------------------------------------------------
:: 1. Verify dotnet SDK
:: -------------------------------------------------------
echo [1/4] Checking dotnet SDK...
where dotnet >nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] dotnet SDK not found.
    echo         Please download and install it from: https://dotnet.microsoft.com/download
    goto :error
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do echo        Found: dotnet %%v

:: -------------------------------------------------------
:: 2. Clean build folder
:: -------------------------------------------------------
echo.
echo [2/4] Cleaning build folder...
if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
    if !errorlevel! neq 0 (
        echo [ERROR] Failed to clean build folder. Check if files are locked.
        goto :error
    )
)
mkdir "%BUILD_DIR%" 2>nul
echo        Cleaned and prepared build folder.

:: -------------------------------------------------------
:: 3. Compile in Release configuration
:: -------------------------------------------------------
echo.
echo [3/4] Compiling Virtuart4DNavisworks in Release x64...
dotnet build "%PROJECT_DIR%Virtuart4DNavisworks.csproj" ^
    -c Release ^
    -p:PlatformTarget=x64 ^
    --output "%BUNDLE_DIR%\Contents\v22" ^
    --nologo ^
    -v minimal

if !errorlevel! neq 0 (
    echo [ERROR] Compilation failed.
    goto :error
)
echo        Compilation succeeded.

:: -------------------------------------------------------
:: 4. Assemble Bundle (Copy PackageContents.xml)
:: -------------------------------------------------------
echo.
echo [4/4] Assembling Autodesk Application Bundle...
if not exist "%PROJECT_DIR%PackageContents.xml" (
    echo [ERROR] PackageContents.xml not found in root directory.
    goto :error
)
copy "%PROJECT_DIR%PackageContents.xml" "%BUNDLE_DIR%\" >nul
if !errorlevel! neq 0 (
    echo [ERROR] Failed to copy PackageContents.xml to bundle folder.
    goto :error
)
echo        PackageContents.xml copied to bundle.

:: -------------------------------------------------------
:: Success Output
:: -------------------------------------------------------
echo.
echo ============================================================
echo  BUILD COMPLETED SUCCESSFULLY!
echo ============================================================
echo.
echo  Bundle Location: %BUNDLE_DIR%
echo.
echo  You can copy "Virtuart4DNavisworks.bundle" directly to:
echo  %%APPDATA%%\Autodesk\ApplicationPlugins\
echo  to load the plugin in Navisworks Manage 2025.
echo.
goto :success

:error
echo.
echo [FAIL] Build failed. Please check details above.
pause
exit /b 1

:success
pause
exit /b 0
