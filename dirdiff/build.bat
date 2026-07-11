@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

"%CSC%" /nologo /optimize+ /reference:System.Windows.Forms.dll /target:exe /out:bin\dirdiff.exe src\dirdiff.cs

if %ERRORLEVEL% EQU 0 (
    echo Build succeeded: bin\dirdiff.exe
) else (
    echo Build failed.
)
