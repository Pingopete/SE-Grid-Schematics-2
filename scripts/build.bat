@echo off
rem Build both assemblies and deploy them to DeployDir (see Directory.Build.props).
rem The logic dll hot-reloads into a running game within ~2s; the bootstrap
rem requires a game restart.
cd /d "%~dp0.."
dotnet build src\GridProbe\GridProbe.csproj -c Release
dotnet build src\GridProbe.Logic\GridProbe.Logic.csproj -c Release
pause
