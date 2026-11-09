@echo off
setlocal
rem ============================================================================
rem  KeywordClassifier build script
rem
rem  Finds a C# compiler automatically (no Visual Studio required), compiles all
rem  sources into bin\KeywordClassifier.exe and embeds assets\app.ico.
rem
rem  Usage:
rem    build.bat              build only
rem    build.bat selftest     build, then run the 43 core self-tests
rem    build.bat run          build, then start the program
rem    build.bat q [...]      same, but never wait for a key press (CI friendly)
rem
rem  Optional environment variable:
rem    WCT_CSC=full\path\to\csc.exe   force a specific compiler
rem ============================================================================

set "ROOT=%~dp0"
:strip_tail
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%" & goto :strip_tail

set "QUIET="
set "ACTION=%~1"
if /i "%ACTION%"=="q" set "QUIET=1"
if /i "%ACTION%"=="q" set "ACTION=%~2"

echo ============================================
echo   KeywordClassifier - build
echo ============================================
echo.

set "CSC="
set "CSCNAME="

rem ---- 1) explicit override -------------------------------------------------
if defined WCT_CSC if exist "%WCT_CSC%" (
  set "CSC=%WCT_CSC%"
  set "CSCNAME=from WCT_CSC environment variable"
)

rem ---- 2) csc.exe already on PATH (e.g. Developer Command Prompt) ----------
if not defined CSC (
  for /f "delims=" %%F in ('where csc.exe 2^>nul') do (
    if not defined CSC set "CSC=%%F"
  )
)
if defined CSC if not defined CSCNAME set "CSCNAME=from PATH"

rem ---- 3) Roslyn shipped with any Visual Studio 2017-2022 edition ----------
if not defined CSC call :find_roslyn "%ProgramFiles%\Microsoft Visual Studio"
if not defined CSC call :find_roslyn "%ProgramFiles(x86)%\Microsoft Visual Studio"

rem ---- 4) compiler that ships with .NET Framework (C# 5) -------------------
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
  set "CSCNAME=inbox .NET Framework csc (supports C# 5)"
)
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
  set "CSCNAME=inbox .NET Framework csc (32-bit, supports C# 5)"
)

if not defined CSC (
  echo [ERROR] No C# compiler found.
  echo         Install .NET Framework 4.x ^(ships with Windows^) or any Visual Studio,
  echo         or set WCT_CSC to a csc.exe path.
  echo.
  if not defined QUIET pause
  exit /b 1
)

echo Compiler : %CSCNAME%
echo Path     : %CSC%
echo.

if not exist "%ROOT%\bin"    mkdir "%ROOT%\bin"
if not exist "%ROOT%\assets" mkdir "%ROOT%\assets"

rem ---------- step 1: icon (optional; generated from code if missing) -------
set "ICONFILE="
if exist "%ROOT%\assets\app.ico" set "ICONFILE=%ROOT%\assets\app.ico"

if not defined ICONFILE (
  echo [1/3] Generating application icon from tools\IconMaker.cs ...
  set "ICONRSP=%ROOT%\bin\icon.rsp"
  > "%ROOT%\bin\icon.rsp" echo /nologo
  >>"%ROOT%\bin\icon.rsp" echo /target:exe
  >>"%ROOT%\bin\icon.rsp" echo /platform:anycpu
  >>"%ROOT%\bin\icon.rsp" echo /out:"%ROOT%\bin\IconMaker.exe"
  >>"%ROOT%\bin\icon.rsp" echo /r:System.dll
  >>"%ROOT%\bin\icon.rsp" echo /r:System.Drawing.dll
  >>"%ROOT%\bin\icon.rsp" echo "%ROOT%\tools\IconMaker.cs"
  "%CSC%" "@%ROOT%\bin\icon.rsp" >nul 2>nul
  if exist "%ROOT%\bin\IconMaker.exe" (
    "%ROOT%\bin\IconMaker.exe" "%ROOT%\assets\app.ico" >nul
    del "%ROOT%\bin\IconMaker.exe" >nul 2>nul
    if exist "%ROOT%\assets\app.ico" set "ICONFILE=%ROOT%\assets\app.ico"
  )
  del "%ROOT%\bin\icon.rsp" >nul 2>nul
) else (
  echo [1/3] Icon already exists, skipped.
)

rem ---------- step 2: main program -----------------------------------------
rem  A response file is used because this folder path may contain spaces.
echo [2/3] Compiling main program ...
set "RSP=%ROOT%\bin\build_app.rsp"
> "%RSP%" echo /nologo
>>"%RSP%" echo /target:winexe
>>"%RSP%" echo /platform:anycpu
>>"%RSP%" echo /optimize+
>>"%RSP%" echo /warn:4
>>"%RSP%" echo /out:"%ROOT%\bin\KeywordClassifier.exe"
if defined ICONFILE >>"%RSP%" echo /win32icon:"%ICONFILE%"
>>"%RSP%" echo /r:System.dll
>>"%RSP%" echo /r:System.Core.dll
>>"%RSP%" echo /r:System.Drawing.dll
>>"%RSP%" echo /r:System.Windows.Forms.dll
>>"%RSP%" echo /r:System.Web.Extensions.dll
>>"%RSP%" echo /r:System.IO.Compression.dll
>>"%RSP%" echo /r:System.IO.Compression.FileSystem.dll
>>"%RSP%" echo /r:System.Xml.dll
>>"%RSP%" echo "%ROOT%\src\Ui.cs"
>>"%RSP%" echo "%ROOT%\src\Program.cs"
>>"%RSP%" echo "%ROOT%\src\MainForm.cs"
>>"%RSP%" echo "%ROOT%\src\CategoryEditor.cs"
>>"%RSP%" echo "%ROOT%\src\Keywords.cs"
>>"%RSP%" echo "%ROOT%\src\Matcher.cs"
>>"%RSP%" echo "%ROOT%\src\Processor.cs"
>>"%RSP%" echo "%ROOT%\src\TextReaders.cs"
>>"%RSP%" echo "%ROOT%\src\Settings.cs"
>>"%RSP%" echo "%ROOT%\src\SelfTest.cs"

"%CSC%" "@%RSP%"
set "CSLEXIT=%ERRORLEVEL%"
del "%RSP%" >nul 2>nul

if not "%CSLEXIT%"=="0" goto :failed
if not exist "%ROOT%\bin\KeywordClassifier.exe" goto :failed

echo [3/3] Done.
echo.
echo ============================================
for %%F in ("%ROOT%\bin\KeywordClassifier.exe") do echo   Output: bin\KeywordClassifier.exe   %%~zF bytes
echo ============================================
echo.

if /i "%ACTION%"=="selftest" (
  echo Core algorithm self-test:
  echo --------------------------------------------
  "%ROOT%\bin\KeywordClassifier.exe" --selftest
  echo --------------------------------------------
)

if /i "%ACTION%"=="run" (
  echo Starting program ...
  start "" "%ROOT%\bin\KeywordClassifier.exe"
)

if not defined QUIET (
  echo Double-click bin\KeywordClassifier.exe to run.
  echo.
  pause
)
exit /b 0

:failed
echo.
echo [ERROR] Compilation failed. See the messages above.
echo.
if not defined QUIET pause
exit /b 1

rem ---------------------------------------------------------------------------
rem :find_roslyn <Visual Studio root>
rem   Scans <root>\<year>\<edition>\MSBuild\Current\Bin\Roslyn\csc.exe
rem ---------------------------------------------------------------------------
:find_roslyn
if not exist "%~1" exit /b 0
for /d %%Y in ("%~1\*") do (
  for /d %%E in ("%%Y\*") do (
    if not defined CSC if exist "%%E\MSBuild\Current\Bin\Roslyn\csc.exe" (
      set "CSC=%%E\MSBuild\Current\Bin\Roslyn\csc.exe"
      set "CSCNAME=Roslyn from %%E"
    )
  )
)
exit /b 0
