@echo off
REM Applies any migrations that have not run yet on this machine.
setlocal
cd /d "%~dp0..\src\FieldAttendance.Api"
dotnet tool install --global dotnet-ef >nul 2>&1
dotnet ef database update || exit /b 1
echo.
echo Database is up to date.
