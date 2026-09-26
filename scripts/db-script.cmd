@echo off
REM Produces a SQL script for a DBA to review before touching production.
setlocal
cd /d "%~dp0..\src\FieldAttendance.Api"
dotnet tool install --global dotnet-ef >nul 2>&1
dotnet ef migrations script --idempotent --output "%~dp0..\migration.sql" || exit /b 1
echo.
echo Script written to migration.sql
