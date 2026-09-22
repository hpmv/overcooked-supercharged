@echo off
setlocal
rem This is an offline source-built regression suite, not a Unity test or a
rem claim of complete PhysX parity. Stop at the first failing fixture.

call "%~dp0harness\Build-Harness.cmd" --joined-replay-probe
if errorlevel 1 exit /b 1

call "%~dp0partial_contacts\Build-Check.cmd" --subset-probe
if errorlevel 1 exit /b 1
set "PARTIAL_EXE=%~dp0partial_contacts\out-ninja\physx333_partial_contacts.exe"

"%PARTIAL_EXE%" --subset-warm-probe
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --mixed-baseline
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --mixed-subset-probe
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --mixed-warm-subset-probe
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --capsule-mixed-baseline
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --capsule-mixed-subset-probe
if errorlevel 1 exit /b 1
"%PARTIAL_EXE%" --capsule-mixed-warm-subset-probe
if errorlevel 1 exit /b 1

call "%~dp0trigger_marker\Build-Check.cmd"
if errorlevel 1 exit /b 1
call "%~dp0arena_snapshot\Build-Check.cmd"
if errorlevel 1 exit /b 1

echo PASS current offline PhysX rewind and baseline gates
exit /b 0
