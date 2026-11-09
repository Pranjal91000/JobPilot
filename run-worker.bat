@echo off
title JobPilot Automation Worker
echo ====================================================
echo   Starting JobPilot Automation Worker (Visible GUI)
echo ====================================================
cd /d "%~dp0"
dotnet run --project src/JobPilot.AutomationWorker/JobPilot.AutomationWorker.csproj
pause
