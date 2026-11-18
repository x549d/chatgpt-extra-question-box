@echo off
rem Context Notes SessionStart hook. Starts the single-instance helper and returns at once.
rem Writes nothing to stdout: SessionStart stdout would be added to the model context.
setlocal
if not defined PLUGIN_ROOT set "PLUGIN_ROOT=%~dp0.."
if exist "%PLUGIN_ROOT%\bin\SelectionProbe.exe" "%PLUGIN_ROOT%\bin\SelectionProbe.exe" --launch >nul 2>&1
exit /b 0
