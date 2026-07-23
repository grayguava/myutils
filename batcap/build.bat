@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

echo Building batcap...
"%CSC%" /nologo /optimize+ /reference:System.Management.dll /target:winexe /out:bin\batcap.exe src\*.cs
if %ERRORLEVEL% EQU 0 (
    echo Build succeeded: bin\batcap.exe
) else (
    echo Build failed.
    exit /b 1
)
