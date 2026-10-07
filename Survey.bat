@echo off
rem Milestone 1: records how far Elden Ring has collision while you play. Start this after
rem your character has loaded. Press Ctrl+C in this window to stop.
cd /d "%~dp0"
python tools\erctl.py status
python tools\erctl.py survey
pause
