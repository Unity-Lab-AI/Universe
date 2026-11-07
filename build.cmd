@echo off
rem Universe build: our own C# source, compiled by the C# compiler that ships with Windows. No outside tools or packages.
rem   build.cmd        builds build\Universe.exe
rem   build.cmd NAME   builds build\NAME\Universe.exe (a separate copy, so a running one is never touched)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Windows' built-in C# compiler was not found at %CSC%
  exit /b 1
)
set OUT=build\Universe.exe
if not "%1"=="" set OUT=build\%1\Universe.exe
if not exist build mkdir build
if not "%1"=="" if not exist build\%1 mkdir build\%1
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /unsafe /warnaserror+ /define:PREVIEW;UNIVERSE /out:%OUT% /resource:README.md,universe-readme.md /resource:docs\HOW-TO-BUILD.md,how-to-build.md /recurse:src\*.cs
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
xcopy /e /i /q /y content "%~dp0%OUT%\..\content" > nul
echo Built %OUT%
