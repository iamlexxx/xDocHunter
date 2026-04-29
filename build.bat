@echo off
dotnet build src/xDocHunter/xDocHunter.csproj
if %errorlevel% == 0 (
    start "" "src\xDocHunter\bin\Debug\net8.0-windows\win-x64\xDocHunter.exe"
)
pause

