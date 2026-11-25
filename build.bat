@echo off
rem Rebuild WeChatTabName.exe from source.
rem Requires only the .NET Framework 4.x that ships with Windows.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" goto nocsc

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:"src\app.ico" /win32manifest:"src\app.manifest" /out:"WeChatTabName.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "src\WeChatTabName.cs" "src\AssemblyInfo.cs"

if errorlevel 1 goto failed
echo [ok] WeChatTabName.exe
exit /b 0

:nocsc
echo [x] csc.exe not found, please install .NET Framework 4.x
exit /b 1

:failed
echo [x] build failed
exit /b 1
