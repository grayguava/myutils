@echo off
REM Compiles src\wallswitch.cs into bin\wallswitch.exe using the built-in
REM .NET Framework C# compiler. No project file, no dotnet CLI, no NuGet.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo csc.exe not found at expected path: %CSC%
    echo Check your .NET Framework installation.
    exit /b 1
)

"%CSC%" /nologo /optimize+ /target:winexe /out:bin\wallswitch.exe src\wallswitch.cs

if %ERRORLEVEL% EQU 0 (
    echo Build succeeded: bin\wallswitch.exe
) else (
    echo Build failed.
)
