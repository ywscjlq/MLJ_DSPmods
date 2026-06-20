@echo off
REM 构建 MLJ_DSPmods（Debug）
cd /d "D:\project\csharp\DSP MOD\MLJ_DSPmods"

echo ========================================
echo   MLJ_DSPmods Debug Build
echo ========================================

"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" MLJ_DSPmods.sln /t:Build /p:Configuration=Debug

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Build succeeded.
    echo.
    echo Starting AfterBuildEvent...
    wt.exe -d "D:\project\csharp\DSP MOD\MLJ_DSPmods\AfterBuildEvent\bin\win\Debug" "D:\project\csharp\DSP MOD\MLJ_DSPmods\AfterBuildEvent\bin\win\Debug\AfterBuildEvent.exe"
) else (
    echo.
    echo [FAIL] Build failed with exit code %ERRORLEVEL%
)
pause