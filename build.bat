@echo off
setlocal
cd /d "%~dp0"

rem Builds Brighter.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK, Visual Studio or download needed.

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x is required ^(preinstalled on Windows 10/11^).
    exit /b 1
)

if not exist bin mkdir bin

rem A running copy (it may be sitting in the tray) would lock the exe.
taskkill /im Brighter.exe /f >nul 2>&1

"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:bin\Brighter.exe ^
  /win32icon:assets\brighter.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Management.dll /r:System.Xaml.dll /r:System.Xml.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" ^
  /recurse:src\*.cs > build.log 2>&1

if errorlevel 1 (
    type build.log
    echo.
    echo [ERROR] Build failed. The messages above are also saved in build.log
    if not defined CI pause
    exit /b 1
)
type build.log

echo.
echo [OK] bin\Brighter.exe
endlocal
