@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM DWQuant Server one-click package
REM Usage:
REM   build_server.bat [Configuration] [Runtime] [SelfContained]
REM Example:
REM   build_server.bat
REM   build_server.bat Release win-x64 false

set "BUILD_CONFIG=%~1"
if "%BUILD_CONFIG%"=="" set "BUILD_CONFIG=Release"

set "BUILD_RUNTIME=%~2"
if "%BUILD_RUNTIME%"=="" set "BUILD_RUNTIME=win-x64"

set "SELF_CONTAINED=%~3"
if "%SELF_CONTAINED%"=="" set "SELF_CONTAINED=false"

set "SCRIPT_DIR=%~dp0"
set "SERVER_DIR=%SCRIPT_DIR%.."
set "PROJECT_FILE=%SERVER_DIR%\ServerTest.csproj"
set "OUTPUT_ROOT=%SCRIPT_DIR%output"

echo.
echo [INFO] Build Config   : %BUILD_CONFIG%
echo [INFO] Build Runtime  : %BUILD_RUNTIME%
echo [INFO] Self Contained : %SELF_CONTAINED%
echo [INFO] Project File   : %PROJECT_FILE%

if not exist "%PROJECT_FILE%" (
    echo [ERROR] Project file not found: %PROJECT_FILE%
    goto :FAIL
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet is not found in PATH
    goto :FAIL
)

if not exist "%OUTPUT_ROOT%" mkdir "%OUTPUT_ROOT%"

for /f %%i in ('powershell -NoProfile -Command "$root='%OUTPUT_ROOT%'; $max=0; $dirs=Get-ChildItem -Path $root -Directory -Name -ErrorAction SilentlyContinue; foreach($d in $dirs){ if($d -match '^v\d{4}$'){ $n=[int]$d.Substring(1); if($n -gt $max){ $max=$n } } }; if($max -le 0){ 'v0001' } else { 'v{0:D4}' -f ($max + 1) }"') do set "VERSION_DIR=%%i"
if "%VERSION_DIR%"=="" (
    echo [ERROR] Failed to resolve next version folder
    goto :FAIL
)

set "PACKAGE_ROOT=%OUTPUT_ROOT%\%VERSION_DIR%"
set "PUBLISH_DIR=%PACKAGE_ROOT%\publish"
set "ZIP_FILE=%PACKAGE_ROOT%\ServerTest_%VERSION_DIR%.zip"

if exist "%PACKAGE_ROOT%" rmdir /s /q "%PACKAGE_ROOT%"
mkdir "%PACKAGE_ROOT%"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "BUILD_TS=%%i"

echo.
echo [INFO] Package Version : %VERSION_DIR%
echo [STEP] Publishing server...
pushd "%SERVER_DIR%"
dotnet publish "%PROJECT_FILE%" ^
  -c %BUILD_CONFIG% ^
  -r %BUILD_RUNTIME% ^
  --self-contained %SELF_CONTAINED% ^
  -o "%PUBLISH_DIR%" ^
  /p:DebugType=None ^
  /p:DebugSymbols=false ^
  /p:PublishSingleFile=false
if errorlevel 1 (
    popd
    echo [ERROR] dotnet publish failed
    goto :FAIL
)
popd

if exist "%SERVER_DIR%\Config\server-role.local.json" (
    if not exist "%PUBLISH_DIR%\Config" mkdir "%PUBLISH_DIR%\Config"
    copy /y "%SERVER_DIR%\Config\server-role.local.json" "%PUBLISH_DIR%\Config\server-role.local.json" >nul
)

(
echo buildTime=%BUILD_TS%
echo buildConfig=%BUILD_CONFIG%
echo buildRuntime=%BUILD_RUNTIME%
echo selfContained=%SELF_CONTAINED%
) > "%PUBLISH_DIR%\build-info.txt"

if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%" >nul 2>&1
echo [STEP] Compressing package: %ZIP_FILE%
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_FILE%' -Force"
if errorlevel 1 (
    echo [ERROR] Compress-Archive failed
    goto :FAIL
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$root='%OUTPUT_ROOT%'; $dirs=@(Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d{4}$' } | Sort-Object { [int]$_.Name.Substring(1) }); if($dirs.Count -gt 5){ $removeCount=$dirs.Count-5; $dirs | Select-Object -First $removeCount | ForEach-Object { Remove-Item -Recurse -Force $_.FullName } }"
if errorlevel 1 (
    echo [WARN] Version cleanup failed. Please check folders under: %OUTPUT_ROOT%
) else (
    echo [STEP] Version cleanup done. Keep latest 5 versions.
)

echo.
echo [OK] Package completed
echo [OK] Package root  : %PACKAGE_ROOT%
echo [OK] Publish folder: %PUBLISH_DIR%
echo [OK] Zip package   : %ZIP_FILE%
echo.
exit /b 0

:FAIL
echo.
echo [FAIL] Package failed
echo.
exit /b 1
