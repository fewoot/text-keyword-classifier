@echo off
setlocal
rem ============================================================================
rem  KeywordClassifier - run all automated tests
rem
rem    43  core algorithm self-test   (src\SelfTest.cs, runs inside the exe)
rem    23  end-to-end file processing  (tools\E2ETest.cs)
rem    14  UI structure smoke test     (tools\UiSmoke.cs)
rem    32  config save / restore       (tools\ConfigTest.cs)
rem    40  layout regression, 5 sizes  (tools\LayoutTest.cs)
rem    15  DPI scaling 100/125/150%%    (tools\DpiTest.cs)
rem   ---
rem   167  assertions
rem
rem  Usage:  test-all.bat        run and wait for a key press
rem          test-all.bat q      run and exit (CI friendly)
rem ============================================================================

set "ROOT=%~dp0"
:strip_tail
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%" & goto :strip_tail

set "QUIET="
if /i "%~1"=="q" set "QUIET=1"

set "CSC="
if defined WCT_CSC if exist "%WCT_CSC%" set "CSC=%WCT_CSC%"
if not defined CSC (
  for /f "delims=" %%F in ('where csc.exe 2^>nul') do (
    if not defined CSC set "CSC=%%F"
  )
)
if not defined CSC call :find_roslyn "%ProgramFiles%\Microsoft Visual Studio"
if not defined CSC call :find_roslyn "%ProgramFiles(x86)%\Microsoft Visual Studio"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not defined CSC (
  echo [ERROR] no C# compiler found. Run build.bat once to see details.
  if not defined QUIET pause
  exit /b 1
)

if not exist "%ROOT%\bin\KeywordClassifier.exe" (
  echo [ERROR] bin\KeywordClassifier.exe not found. Run build.bat first.
  if not defined QUIET pause
  exit /b 1
)

set "SRC=%ROOT%\src"
set "TOOLS=%ROOT%\tools"
set "WORK=%ROOT%\bin"
set "EXE=%ROOT%\bin\KeywordClassifier.exe"
set "FAILED=0"

echo ################################################################
echo  1/6  core algorithm self-test               43 checks
echo ################################################################
"%EXE%" --selftest
if errorlevel 1 set "FAILED=1"
echo.

echo ################################################################
echo  2/6  end-to-end file processing test        23 checks
echo ################################################################
set "RSP=%WORK%\e2e.rsp"
> "%RSP%" echo /nologo
>>"%RSP%" echo /target:exe
>>"%RSP%" echo /platform:anycpu
>>"%RSP%" echo /out:"%WORK%\e2e.exe"
>>"%RSP%" echo /r:System.dll
>>"%RSP%" echo /r:System.Core.dll
>>"%RSP%" echo /r:System.Web.Extensions.dll
>>"%RSP%" echo /r:System.IO.Compression.dll
>>"%RSP%" echo /r:System.IO.Compression.FileSystem.dll
>>"%RSP%" echo /r:System.Xml.dll
>>"%RSP%" echo "%SRC%\Keywords.cs"
>>"%RSP%" echo "%SRC%\Matcher.cs"
>>"%RSP%" echo "%SRC%\Processor.cs"
>>"%RSP%" echo "%SRC%\TextReaders.cs"
>>"%RSP%" echo "%SRC%\Settings.cs"
>>"%RSP%" echo "%SRC%\Ui.cs"
>>"%RSP%" echo "%TOOLS%\E2ETest.cs"
"%CSC%" "@%RSP%"
del "%RSP%" >nul 2>nul
if exist "%WORK%\e2e.exe" (
  "%WORK%\e2e.exe"
  if errorlevel 1 set "FAILED=1"
) else (
  echo [ERROR] failed to build e2e test
  set "FAILED=1"
)
echo.

echo ################################################################
echo  3/6  user interface smoke test              14 checks
echo ################################################################
set "RSP2=%WORK%\ui.rsp"
> "%RSP2%" echo /nologo
>>"%RSP2%" echo /target:exe
>>"%RSP2%" echo /platform:anycpu
>>"%RSP2%" echo /out:"%WORK%\ui.exe"
>>"%RSP2%" echo /r:System.dll
>>"%RSP2%" echo /r:System.Drawing.dll
>>"%RSP2%" echo /r:System.Windows.Forms.dll
>>"%RSP2%" echo "%TOOLS%\UiSmoke.cs"
"%CSC%" "@%RSP2%"
del "%RSP2%" >nul 2>nul
if exist "%WORK%\ui.exe" (
  "%WORK%\ui.exe" "%EXE%"
  if errorlevel 1 set "FAILED=1"
) else (
  echo [ERROR] failed to build ui smoke test
  set "FAILED=1"
)
echo.

echo ################################################################
echo  4/6  config save / restore test             32 checks
echo ################################################################
set "RSP3=%WORK%\cfg.rsp"
> "%RSP3%" echo /nologo
>>"%RSP3%" echo /target:exe
>>"%RSP3%" echo /platform:anycpu
>>"%RSP3%" echo /out:"%WORK%\cfgtest.exe"
>>"%RSP3%" echo /r:System.dll
>>"%RSP3%" echo /r:System.Core.dll
>>"%RSP3%" echo /r:System.Drawing.dll
>>"%RSP3%" echo /r:System.Windows.Forms.dll
>>"%RSP3%" echo "%TOOLS%\ConfigTest.cs"
"%CSC%" "@%RSP3%"
del "%RSP3%" >nul 2>nul
if exist "%WORK%\cfgtest.exe" (
  "%WORK%\cfgtest.exe" "%EXE%" "%WORK%\_cfgwork"
  if errorlevel 1 set "FAILED=1"
) else (
  echo [ERROR] failed to build config test
  set "FAILED=1"
)
echo.

echo ################################################################
echo  5/6  layout regression test                 40 checks
echo ################################################################
set "RSP4=%WORK%\layout.rsp"
> "%RSP4%" echo /nologo
>>"%RSP4%" echo /target:exe
>>"%RSP4%" echo /platform:anycpu
>>"%RSP4%" echo /out:"%WORK%\layouttest.exe"
>>"%RSP4%" echo /r:System.dll
>>"%RSP4%" echo /r:System.Core.dll
>>"%RSP4%" echo /r:System.Drawing.dll
>>"%RSP4%" echo /r:System.Windows.Forms.dll
>>"%RSP4%" echo "%TOOLS%\LayoutTest.cs"
"%CSC%" "@%RSP4%"
del "%RSP4%" >nul 2>nul
if exist "%WORK%\layouttest.exe" (
  "%WORK%\layouttest.exe" "%EXE%"
  if errorlevel 1 set "FAILED=1"
) else (
  echo [ERROR] failed to build layout test
  set "FAILED=1"
)
echo.

echo ################################################################
echo  6/6  DPI scaling test                      15 checks
echo ################################################################
set "RSP5=%WORK%\dpi.rsp"
> "%RSP5%" echo /nologo
>>"%RSP5%" echo /target:exe
>>"%RSP5%" echo /platform:anycpu
>>"%RSP5%" echo /define:CONSOLE
>>"%RSP5%" echo /out:"%WORK%\dpitest.exe"
>>"%RSP5%" echo /r:System.dll
>>"%RSP5%" echo /r:System.Core.dll
>>"%RSP5%" echo /r:System.Drawing.dll
>>"%RSP5%" echo /r:System.Windows.Forms.dll
>>"%RSP5%" echo "%TOOLS%\DpiTest.cs"
"%CSC%" "@%RSP5%"
del "%RSP5%" >nul 2>nul
if exist "%WORK%\dpitest.exe" (
  "%WORK%\dpitest.exe" "%EXE%" "%WORK%\shots"
  if errorlevel 1 set "FAILED=1"
) else (
  echo [ERROR] failed to build dpi test
  set "FAILED=1"
)
echo.

del "%WORK%\e2e.exe" "%WORK%\ui.exe" "%WORK%\cfgtest.exe" "%WORK%\layouttest.exe" "%WORK%\dpitest.exe" >nul 2>nul
rmdir /s /q "%WORK%\_cfgwork" >nul 2>nul

echo ################################################################
echo  Finished.  Total 167 checks.
echo ################################################################
echo.
if "%FAILED%"=="1" (
  echo [RESULT] SOME TESTS FAILED - see the output above.
  if not defined QUIET pause
  exit /b 1
)
echo [RESULT] All tests passed.
if not defined QUIET pause
exit /b 0

:find_roslyn
if not exist "%~1" exit /b 0
for /d %%Y in ("%~1\*") do (
  for /d %%E in ("%%Y\*") do (
    if not defined CSC if exist "%%E\MSBuild\Current\Bin\Roslyn\csc.exe" (
      set "CSC=%%E\MSBuild\Current\Bin\Roslyn\csc.exe"
    )
  )
)
exit /b 0
