@echo off
rem Starts AoTTG2 directly (not through its launcher, which could update the game or remove
rem BepInEx). Installs the newest Attack on Elden Ring plugin first; the plugin keeps AoTTG2 offline.
set "GAME=C:\Users\<you>\Downloads\Aottg2Launcher\Release\MainApp"
if exist "%~dp0dist\guest\AoerBridge.dll" (
  if not exist "%GAME%\BepInEx\plugins\AoerBridge" mkdir "%GAME%\BepInEx\plugins\AoerBridge"
  copy /y "%~dp0dist\guest\AoerBridge.dll" "%GAME%\BepInEx\plugins\AoerBridge\AoerBridge.dll" >nul || echo Could not update the plugin: is AoTTG2 already running?
)
cd /d "%GAME%"
start "" Aottg2.exe
