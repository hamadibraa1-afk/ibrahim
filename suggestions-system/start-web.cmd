@echo off
chcp 65001 >nul
title Suggestions System - Web (http://localhost:4200)
cd /d "%~dp0web"
if not exist node_modules (
  echo Installing packages - first run only, takes a few minutes...
  call npm install
)
echo Starting web on http://localhost:4200 ...
start "" http://localhost:4200
call npx ng serve --port 4200
pause
