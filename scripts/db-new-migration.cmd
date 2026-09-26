@echo off
REM Creates a migration after a model change, then applies it.
REM Usage: db-new-migration.cmd AddPayrollNotes
setlocal
if "%~1"=="" (
  echo Give the migration a name, e.g. db-new-migration.cmd AddPayrollNotes
  exit /b 1
)
cd /d "%~dp0..\src\FieldAttendance.Api"
dotnet tool install --global dotnet-ef >nul 2>&1
dotnet ef migrations add %1 --output-dir Data\Migrations || exit /b 1
dotnet ef database update || exit /b 1
echo.
echo Migration %1 created and applied.
