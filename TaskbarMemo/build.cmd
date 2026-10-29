@echo off
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" echo ERROR: csc.exe not found. .NET Framework 4.x is required.
if not exist "%CSC%" pause
if not exist "%CSC%" exit /b 1
echo Compiler: %CSC%
echo Building TaskbarMemo.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:TaskbarMemo.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll src\*.cs
if errorlevel 1 echo.
if errorlevel 1 echo BUILD FAILED - please copy the error lines above.
if errorlevel 1 pause
if errorlevel 1 exit /b 1

rem Strip the downloaded-file mark (Mark of the Web / Zone.Identifier) so Windows
rem stops asking "do you want to run this" every time. A file produced by "copy"
rem carries no alternate data streams, so: copy out, delete the original, move the
rem clean copy back. Guarded so the exe is never deleted before the copy exists.
if exist TaskbarMemo.exe copy /y TaskbarMemo.exe TaskbarMemo.clean.exe >nul 2>nul
if exist TaskbarMemo.clean.exe del /f /q TaskbarMemo.exe >nul 2>nul
if exist TaskbarMemo.clean.exe move /y TaskbarMemo.clean.exe TaskbarMemo.exe >nul 2>nul

echo.
echo BUILD OK: TaskbarMemo.exe
pause
