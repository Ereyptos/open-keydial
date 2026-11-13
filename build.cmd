@echo off
rem Builds bin\OpenKeydial.exe (a single portable file), or <folder>\OpenKeydial.exe with: build.cmd <folder>
rem Needs the C# compiler of Visual Studio 2019+ or of the Build Tools for Visual Studio
rem (the .NET Framework 4.8 it targets is part of Windows 10 and 11).
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%bin"
if not "%~1"=="" set "OUT=%~f1"
set "ICON=%ROOT%assets\OpenKeydial.ico"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "CSC="
if exist "%VSWHERE%" for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -find MSBuild\**\Bin\Roslyn\csc.exe`) do if not defined CSC set "CSC=%%i"
if not defined CSC (
  echo C# compiler not found: install Visual Studio or the Build Tools for Visual Studio.
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

rem First build ever: make the icon file with the program itself, then build again with it.
if not exist "%ICON%" (
  call :compile "" || exit /b 1
  "%OUT%\OpenKeydial.exe" --make-icon "%ICON%" || exit /b 1
)
call :compile "/win32icon:%ICON%" || exit /b 1
echo Built: %OUT%\OpenKeydial.exe
exit /b 0

:compile
"%CSC%" /nologo /langversion:latest /platform:x64 /target:winexe /optimize+ %~1 ^
  /out:"%OUT%\OpenKeydial.exe" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  /resource:"%ROOT%src\default.ini",OpenKeydial.default.ini ^
  /resource:"%ROOT%assets\fonts\Montserrat-Light.ttf",OpenKeydial.fonts.Montserrat-Light.ttf ^
  /resource:"%ROOT%assets\fonts\Montserrat-Regular.ttf",OpenKeydial.fonts.Montserrat-Regular.ttf ^
  /resource:"%ROOT%assets\fonts\Montserrat-LightItalic.ttf",OpenKeydial.fonts.Montserrat-LightItalic.ttf ^
  "%ROOT%src\*.cs"
exit /b %errorlevel%
