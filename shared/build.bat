@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

if not exist bin\ md bin

echo Building dirdiff...
"%CSC%" /nologo /optimize+ /define:WINDOWS /reference:System.Windows.Forms.dll /target:exe /out:bin\dirdiff.exe src\dirdiff.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Building delcache...
"%CSC%" /nologo /optimize+ /target:exe /out:bin\delcache.exe src\delcache.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Building catsort...
"%CSC%" /nologo /optimize+ /target:exe /out:bin\catsort.exe src\catsort.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo Building reindex...
"%CSC%" /nologo /optimize+ /target:exe /out:bin\reindex.exe src\reindex.cs
if %ERRORLEVEL% NEQ 0 exit /b 1

echo All builds succeeded.
