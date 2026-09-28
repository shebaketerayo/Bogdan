@echo off
rem builds bin\DesktopCat.exe from src\*.cs with the c# 5 compiler that ships with windows (.net framework 4.8)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
set ICON=
if exist src\cat.ico set ICON=/win32icon:src\cat.ico
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /warn:4 /nowarn:1591 ^
  /out:bin\DesktopCat.exe /win32manifest:src\app.manifest %ICON% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: bin\DesktopCat.exe
exit /b 0
