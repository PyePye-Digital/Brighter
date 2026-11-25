@echo off
setlocal
cd /d "%~dp0"

rem Builds bin\Brighter.exe and then dist\Brighter-Setup-<version>.exe.
rem Needs Inno Setup 6: https://jrsoftware.org/isinfo.php

call build.bat
if errorlevel 1 exit /b 1

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" goto noinno

"%ISCC%" /Q installer\Brighter.iss
if errorlevel 1 (
    echo [ERROR] Installer build failed.
    if not defined CI pause
    exit /b 1
)
echo [OK] dist\
exit /b 0

:noinno
echo [ERROR] Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php
if not defined CI pause
exit /b 1
