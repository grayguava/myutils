@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

echo Building diskwatch...
"%CSC%" /nologo /optimize+ /reference:System.Windows.Forms.dll,System.Web.Extensions.dll /target:winexe /out:bin\diskwatch.exe src\*.cs
if %ERRORLEVEL% EQU 0 (
    echo Build succeeded: bin\diskwatch.exe
) else (
    echo Build failed.
    exit /b 1
)
