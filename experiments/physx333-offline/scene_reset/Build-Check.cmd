@echo off
setlocal

set "VSDEVCMD=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
set "CMAKE=C:\Program Files\CMake\bin\cmake.exe"
set "NINJA=C:/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe"

if not exist "%VSDEVCMD%" exit /b 1
if not exist "%CMAKE%" exit /b 1
call "%VSDEVCMD%" -arch=x86 -host_arch=x64 >nul
if errorlevel 1 exit /b 1

"%CMAKE%" -S "%~dp0..\level_graph" -B "%~dp0..\level_graph\out-ninja" -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release "-DCMAKE_MAKE_PROGRAM=%NINJA%"
if errorlevel 1 exit /b 1
"%CMAKE%" --build "%~dp0..\level_graph\out-ninja" --config Release --target physx333_scene_reset
if errorlevel 1 exit /b 1

"%~dp0..\level_graph\out-ninja\physx333_scene_reset.exe" %*
exit /b %errorlevel%
