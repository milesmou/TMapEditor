@echo off
setlocal
cd /d "%~dp0"

set "OUTPUT_DIR=%CD%\Release"

dotnet publish TMapEditor.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%OUTPUT_DIR%" ^
    /p:PublishSingleFile=true ^
    /p:PublishAot=false ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:IncludeAllContentForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true ^
    /p:PublishTrimmed=true ^
    /p:TrimMode=partial ^
    /p:DebugType=none ^
    /p:DebugSymbols=false ^
    /p:CopyOutputSymbolsToPublishDirectory=false ^
    /p:MewUIBackend=Direct2D

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b 1
)

del /q "%OUTPUT_DIR%\*.pdb" 2>nul

echo.
echo Published to "%OUTPUT_DIR%".
