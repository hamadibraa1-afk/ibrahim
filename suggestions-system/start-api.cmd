@echo off
chcp 65001 >nul
title Suggestions System - API (http://localhost:5199)
cd /d "%~dp0api\ProposalSystem.Api"
echo Starting API on http://localhost:5199 ...
echo (first run creates the SQLite database and demo accounts)
dotnet run --launch-profile http
pause
