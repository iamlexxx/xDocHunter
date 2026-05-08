@echo off
taskkill /f /im xDocHunter.exe >nul 2>&1

echo.
echo  [1] Debug build + launch
echo  [2] Publish build (single-file release)
echo.
set /p choice=" Select: "

if "%choice%"=="1" goto debug
if "%choice%"=="2" goto publish
echo Invalid choice.
goto end

:debug
dotnet build src/xDocHunter/xDocHunter.csproj
if %errorlevel% == 0 (
    start "" "src\xDocHunter\bin\Debug\net8.0-windows\win-x64\xDocHunter.exe"
) else (
    pause
)
goto end

:publish
dotnet publish src/xDocHunter/xDocHunter.csproj -c Release
if %errorlevel% == 0 (
    echo.
    echo  Done. Opening publish folder...
    explorer "src\xDocHunter\bin\Release\net8.0-windows\win-x64\publish"
) else (
    pause
)

:end
