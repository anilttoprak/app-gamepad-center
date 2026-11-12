@echo off
if exist "%~dp0dist\ControllerHub-v1.0.0-win-x64\ControllerHub.exe" (
    start "" "%~dp0dist\ControllerHub-v1.0.0-win-x64\ControllerHub.exe"
) else (
    start "" "%~dp0src\ControllerHub.App\bin\Debug\net8.0-windows10.0.19041.0\ControllerHub.App.exe"
)
