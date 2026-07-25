@echo off
rem Launch SE2 with the bootstrap plugin via Keen's own -plugins: argument.
rem Steam must be running. Build first: scripts\build.bat
set PLUGIN_DLL=%~dp0..\src\GridProbe\bin\Release\GridProbe.dll
if not exist "%PLUGIN_DLL%" (
  echo Plugin DLL not found: %PLUGIN_DLL%
  echo Run scripts\build.bat first.
  pause
  exit /b 1
)
cd /d "D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2"
start "" SpaceEngineers2.exe "-plugins:%PLUGIN_DLL%"
echo Launched. Watch output\probe.log for activity.
rem If the log never appears, Steam may have relaunched the exe without args:
rem put -plugins:<full path to GridProbe.dll> in Steam -> SE2 -> Properties ->
rem Launch Options and start from Steam instead.
