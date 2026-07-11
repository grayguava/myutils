@echo off
REM Compile both kdbx-backup tools using the built-in .NET Framework C# compiler.
REM No project file, no dotnet CLI, no NuGet.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

echo Building kdbxWatch...
"%CSC%" /nologo /target:winexe /out:backgroundWatcher\bin\kdbxWatch.exe backgroundWatcher\src\watcher.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Building kdbxPushToRemote...
"%CSC%" /nologo /target:winexe /out:pushToRemote\bin\kdbxPushToRemote.exe pushToRemote\src\push.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo All builds succeeded.
