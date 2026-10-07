@echo off
rem Records which animations the Tarnished plays while you play Elden Ring normally (not linked).
cd /d "%~dp0"
python tools\erctl.py anim
pause
