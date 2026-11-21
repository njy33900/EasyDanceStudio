@echo off
rem H3-02: builds MMDStudio\Native\EdsMedia\EdsMedia.cpp into the Unity plugin folder.
rem Static CRT (/MT): the DLL needs no Visual C++ redistributable. Links Media Foundation only.
setlocal
set ROOT=%~dp0..
set OUT=%ROOT%\MMDStudio\Assets\MMDStudio\Plugins\x86_64
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
if not defined VS (echo Visual Studio C++ tools not found & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%OUT%" mkdir "%OUT%"
set TMPOBJ=%TEMP%\EdsMediaBuild
if not exist "%TMPOBJ%" mkdir "%TMPOBJ%"
cl /nologo /O2 /MT /EHsc /W3 /LD /DUNICODE /D_UNICODE "%ROOT%\MMDStudio\Native\EdsMedia\EdsMedia.cpp" /Fo"%TMPOBJ%\\" /Fe"%OUT%\EdsMedia.dll" /link /NOLOGO /IMPLIB:"%TMPOBJ%\EdsMedia.lib" || exit /b 1
del "%OUT%\EdsMedia.exp" 2>nul
echo BUILT %OUT%\EdsMedia.dll
