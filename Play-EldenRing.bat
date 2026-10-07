@echo off
rem Starts Elden Ring OFFLINE through me3 with the Attack on Elden Ring profile.
rem Steam must be running. Uses its own save file (ER0000.aoer.sl2).
chcp 65001 >nul
cd /d "%~dp0"
".tools\me3\bin\me3.exe" launch -p "%~dp0me3\aoer.me3"
pause
