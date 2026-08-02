@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title AWSR Portable Preloader Patcher Build

set "PROJECT=AutomaticWorldSpawnReroll.Patcher.csproj"
set "DLL_NAME=AutomaticWorldSpawnReroll.Patcher.dll"
set "RELEASE_NAME=Automatic_World_Spawn_Reroll_AWSR_v1.1.0_Patcher.zip"
set "LOG_FILE=%CD%\AWSR_PATCHER_BUILD_LOG.txt"

call :BUILD_ALL > "%LOG_FILE%" 2>&1
set "BUILD_RESULT=%ERRORLEVEL%"

type "%LOG_FILE%"
echo.
if "%BUILD_RESULT%"=="0" (
    echo ============================================================
    echo  AWSR PATCHER BUILD SUCCEEDED
    echo ============================================================
    echo.
    echo Built patcher DLL:
    echo   %CD%\dist\%DLL_NAME%
    echo.
    echo Ready-to-install ZIP:
    echo   %CD%\dist\%RELEASE_NAME%
) else (
    echo ============================================================
    echo  AWSR PATCHER BUILD FAILED - ERROR %BUILD_RESULT%
    echo ============================================================
    echo.
    echo The complete output was saved to:
    echo   %LOG_FILE%
)

echo.
pause
exit /b %BUILD_RESULT%

:BUILD_ALL
echo ============================================================
echo  Automatic World Spawn Reroll - Portable Preloader Patcher
echo  Version 1.1.0
echo ============================================================
echo Working folder:
echo   %CD%
echo.
echo This build does not locate, read, modify, or install into Monsterpatch.
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: The .NET SDK command "dotnet" was not found.
    echo Install a current .NET SDK, reopen this folder, and run this CMD again.
    exit /b 10
)

dotnet --version
if errorlevel 1 exit /b 11

if not exist "lib\Mono.Cecil.dll" (
    echo ERROR: Portable compile reference missing: lib\Mono.Cecil.dll
    exit /b 20
)

for %%D in (bin obj dist) do (
    if exist "%%D" rmdir /s /q "%%D"
)

echo.
echo Restoring project...
dotnet restore "%PROJECT%" --ignore-failed-sources --nologo --verbosity:minimal
if errorlevel 1 exit /b 30

echo.
echo Building AWSR preloader patcher...
dotnet build "%PROJECT%" -c Release --no-restore --nologo --verbosity:minimal
if errorlevel 1 exit /b 40

set "BUILT_DLL=%CD%\bin\Release\%DLL_NAME%"
if not exist "%BUILT_DLL%" (
    echo ERROR: Build reported success, but the patcher DLL was not found:
    echo   %BUILT_DLL%
    exit /b 41
)

mkdir "dist" >nul 2>nul
copy /y "%BUILT_DLL%" "dist\%DLL_NAME%" >nul
if errorlevel 1 exit /b 42

mkdir "dist\release\BepInEx\patchers\AutomaticWorldSpawnReroll" >nul 2>nul
copy /y "%BUILT_DLL%" "dist\release\BepInEx\patchers\AutomaticWorldSpawnReroll\%DLL_NAME%" >nul
copy /y "README_FIRST.txt" "dist\release\BepInEx\patchers\AutomaticWorldSpawnReroll\README_AWSR.txt" >nul
copy /y "CHANGELOG.txt" "dist\release\BepInEx\patchers\AutomaticWorldSpawnReroll\CHANGELOG_AWSR.txt" >nul
copy /y "Docs\TEST_CHECKLIST.txt" "dist\release\BepInEx\patchers\AutomaticWorldSpawnReroll\TEST_CHECKLIST_AWSR.txt" >nul
copy /y "Docs\REMOVE_OLD_PLUGIN.txt" "dist\release\REMOVE_OLD_AWSR_PLUGIN_FIRST.txt" >nul

if exist "dist\%RELEASE_NAME%" del /q "dist\%RELEASE_NAME%"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -Path 'dist\release\*' -DestinationPath 'dist\%RELEASE_NAME%' -Force"
if errorlevel 1 (
    echo WARNING: The DLL built successfully, but PowerShell could not create the optional ZIP.
)

rmdir /s /q "dist\release" >nul 2>nul

echo.
echo Portable patcher build complete.
exit /b 0
