@echo off
setlocal

set "VSDEVCMD=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
set "CMAKE=C:\Program Files\CMake\bin\cmake.exe"
set "NINJA=C:/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe"

if not exist "%VSDEVCMD%" exit /b 1
if not exist "%CMAKE%" exit /b 1
call "%VSDEVCMD%" -arch=x86 -host_arch=x64 >nul
if errorlevel 1 exit /b 1

"%CMAKE%" -S "%~dp0." -B "%~dp0out-ninja" -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release "-DCMAKE_MAKE_PROGRAM=%NINJA%"
if errorlevel 1 exit /b 1
"%CMAKE%" --build "%~dp0out-ninja" --config Release
if errorlevel 1 exit /b 1

"%~dp0out-ninja\physx333_trigger_marker.exe" %*
exit /b %errorlevel%
