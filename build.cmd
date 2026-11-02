@echo off
rem Builds bin\NetMute.exe with the C# compiler that ships with Windows. No SDK needed.
setlocal
pushd "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework compiler csc.exe
  popd
  exit /b 1
)

if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /utf8output /target:winexe /platform:anycpu /optimize+ ^
  /out:bin\NetMute.exe /win32manifest:src\app.manifest /win32icon:src\app.ico ^
  /resource:src\app.ico,NetMute.app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
