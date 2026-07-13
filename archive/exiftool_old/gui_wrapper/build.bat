@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

echo Building clean...
"%CSC%" /nologo /optimize+ /reference:System.Windows.Forms.dll /target:winexe /out:bin\clean.exe src\clean\core.cs src\clean\ui.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Build succeeded.
