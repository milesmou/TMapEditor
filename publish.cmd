@echo off
setlocal
cd /d "%~dp0"

set "OUTPUT_DIR=%CD%\Release"

if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%"
)

dotnet publish TMapEditor.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%OUTPUT_DIR%" ^
    /p:PublishSingleFile=true ^
    /p:PublishTrimmed=true ^
    /p:TrimMode=partial ^
    /p:IncludeNativeLibrariesForSelfExtract=true

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b 1
)

echo.
echo Published to "%OUTPUT_DIR%".
