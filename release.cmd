@echo off
rem Universe release: builds Universe.exe and its Windows installer, using only what comes with Windows (its C# compiler
rem and PowerShell's zip). No outside tools.
rem   release.cmd   ->  release\Universe-Setup-<version>.exe  and  release\Universe-<version>-windows.zip
setlocal
set VER=0.1.1
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
call "%~dp0build.cmd" release || exit /b 1
set STAGE=%~dp0release\stage
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"
copy /y "%~dp0build\release\Universe.exe" "%STAGE%\Universe.exe" > nul
xcopy /e /i /q /y "%~dp0content" "%STAGE%\content" > nul
copy /y "%~dp0docs\README.txt" "%STAGE%\README.txt" > nul
copy /y "%~dp0LICENSE.txt" "%STAGE%\LICENSE.txt" > nul
copy /y "%~dp0PRIVACY.txt" "%STAGE%\PRIVACY.txt" > nul
mkdir "%STAGE%\docs"
copy /y "%~dp0README.md" "%STAGE%\docs\README.md" > nul
copy /y "%~dp0docs\HOW-TO-BUILD.md" "%STAGE%\docs\HOW-TO-BUILD.md" > nul
copy /y "%~dp0docs\GUIDE.md" "%STAGE%\docs\GUIDE.md" > nul
copy /y "%~dp0docs\LINUX.md" "%STAGE%\docs\LINUX.md" > nul
copy /y "%~dp0linux\run.sh" "%STAGE%\run-on-linux.sh" > nul
copy /y "%~dp0CREDITS.md" "%STAGE%\docs\CREDITS.md" > nul
if exist "%~dp0release\payload.zip" del "%~dp0release\payload.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%~dp0release\payload.zip' -Force" || exit /b 1
copy /y "%~dp0release\payload.zip" "%~dp0release\Universe-%VER%-windows.zip" > nul
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /define:UNIVERSE /win32icon:"%~dp0installer\universe.ico" /out:"%~dp0release\Universe-Setup-%VER%.exe" /resource:"%~dp0release\payload.zip",payload.zip /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\System.IO.Compression.FileSystem.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0installer\Setup.cs" || exit /b 1
rmdir /s /q "%STAGE%"
del "%~dp0release\payload.zip"
echo Built release\Universe-Setup-%VER%.exe and release\Universe-%VER%-windows.zip
