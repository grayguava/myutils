@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

echo Building kdbxWatch...
"%CSC%" /nologo /target:winexe /out:bin\kdbxWatch.exe src\watcher.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Building kdbxPushToRemote...
"%CSC%" /nologo /target:winexe /out:bin\kdbxPushToRemote.exe src\push.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo All builds succeeded.
