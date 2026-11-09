@echo off
setlocal
rem ============================================================================
rem  KeywordClassifier - build with MSBuild (alternative to build.bat)
rem
rem  Usually build.bat is enough: it calls the C# compiler directly and does not
rem  need the .NET Framework targeting pack. Use this script only if you prefer
rem  a standard MSBuild build (e.g. the .NET Framework 4.8 Developer Pack is
rem  installed, or you are building inside Visual Studio).
rem
rem  Optional environment variable:
rem    WCT_MSBUILD=full\path\to\MSBuild.exe
rem ============================================================================

set "ROOT=%~dp0"
:strip_tail
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%" & goto :strip_tail

set "MSBUILD="
if defined WCT_MSBUILD if exist "%WCT_MSBUILD%" set "MSBUILD=%WCT_MSBUILD%"
if not defined MSBUILD (
  for /f "delims=" %%F in ('where msbuild.exe 2^>nul') do (
    if not defined MSBUILD set "MSBUILD=%%F"
  )
)
if not defined MSBUILD call :find_msbuild "%ProgramFiles%\Microsoft Visual Studio"
if not defined MSBUILD call :find_msbuild "%ProgramFiles(x86)%\Microsoft Visual Studio"
if not defined MSBUILD if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
  set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
)

if not defined MSBUILD (
  echo [ERROR] MSBuild.exe not found. Use build.bat instead.
  pause
  exit /b 1
)

echo ============================================
echo   KeywordClassifier - MSBuild
echo ============================================
echo MSBuild: %MSBUILD%
echo.

"%MSBUILD%" "%ROOT%\KeywordClassifier.sln" /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if errorlevel 1 (
  echo.
  echo [ERROR] MSBuild failed.
  echo         If you see MSB3644, the .NET Framework 4.8 targeting pack is missing;
  echo         use build.bat instead.
  echo.
  pause
  exit /b 1
)

echo.
echo [DONE] See the output folder reported above.
pause
exit /b 0

:find_msbuild
if not exist "%~1" exit /b 0
for /d %%Y in ("%~1\*") do (
  for /d %%E in ("%%Y\*") do (
    if not defined MSBUILD if exist "%%E\MSBuild\Current\Bin\MSBuild.exe" (
      set "MSBUILD=%%E\MSBuild\Current\Bin\MSBuild.exe"
    )
  )
)
exit /b 0
