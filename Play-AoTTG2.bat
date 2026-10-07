@echo off
rem Starts AoTTG2 directly (not through its launcher, which could update the game or remove
rem BepInEx). Installs the newest Attack on Elden Ring plugin first; the plugin keeps AoTTG2 offline.
rem AoTTG2's folder (the one with Aottg2.exe) comes from AOTTG2_DIR or .local\aottg2_dir.txt.
setlocal
set "GAME=%AOTTG2_DIR%"
if "%GAME%"=="" if exist "%~dp0.local\aottg2_dir.txt" set /p GAME=<"%~dp0.local\aottg2_dir.txt"
if not exist "%GAME%\Aottg2.exe" (
  echo AoTTG2 not found. Put the path of the folder containing Aottg2.exe in .local\aottg2_dir.txt
  pause
  exit /b 1
)
if exist "%~dp0dist\guest\AoerBridge.dll" (
  if not exist "%GAME%\BepInEx\plugins\AoerBridge" mkdir "%GAME%\BepInEx\plugins\AoerBridge"
  copy /y "%~dp0dist\guest\AoerBridge.dll" "%GAME%\BepInEx\plugins\AoerBridge\AoerBridge.dll" >nul || echo Could not update the plugin: is AoTTG2 already running?
)
cd /d "%GAME%"
start "" Aottg2.exe
