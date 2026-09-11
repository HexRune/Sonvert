@echo off
REM ============================================================
REM SenseVoiceService build script.
REM
REM Usage: run this from inside Sonvert.SenseVoiceService, either by
REM double-clicking or from a terminal: build.bat
REM
REM Deliberately written in plain ASCII only (no Chinese characters).
REM Batch files are very sensitive to the console code page - on a
REM Chinese Windows install (code page 936/GBK), a UTF-8 file with
REM Chinese text gets misread and garbled bytes get parsed as if they
REM were command tokens, which is exactly what happened on the first
REM version of this script. Sticking to ASCII sidesteps the whole
REM problem instead of fighting chcp / file encoding.
REM
REM Every run wipes the previous build-only venv and rebuilds it from
REM scratch, so it can never get polluted by whatever got installed
REM into it last time - that's what keeps the packaged output clean.
REM This venv is completely separate from the "env" folder you use
REM for day-to-day development in Visual Studio; that one is never
REM touched by this script.
REM ============================================================

setlocal

set VENV_DIR=venv
set DIST_DIR=dist\SonvertSenseVoice
set PIP_INDEX_URL=https://pypi.tuna.tsinghua.edu.cn/simple

echo [1/6] Cleaning up old build venv and previous output...
if exist %VENV_DIR% rmdir /s /q %VENV_DIR%
if exist build rmdir /s /q build
if exist dist rmdir /s /q dist

echo [2/6] Creating a clean build-only virtualenv (Python 3.11)...
py -3.11 -m venv %VENV_DIR%
if errorlevel 1 (
    echo.
    echo [FAILED] Could not create the virtualenv. Common causes:
    echo   - Python 3.11 is not installed, or was installed without
    echo     "Add python.exe to PATH"
    echo   - the py launcher can't find 3.11 - run "py -0" to check
    echo     which versions it sees
    pause
    exit /b 1
)

echo [3/6] Activating virtual environment...
call %VENV_DIR%\Scripts\activate.bat
if errorlevel 1 (
    echo [FAILED] Could not activate virtual environment
    pause
    exit /b 1
)

echo [4/6] Upgrading pip and configuring mirror source...
python.exe -m pip install --upgrade pip
pip config set global.index-url %PIP_INDEX_URL%
pip config set global.trusted-host pypi.tuna.tsinghua.edu.cn

echo [5/6] Installing dependencies (using Tsinghua mirror, clean cache)...
pip cache purge
pip install -r requirements.txt --no-cache-dir
if errorlevel 1 (
    echo.
    echo [FAILED] Dependency install failed - trying with fallback mirror...
    echo.
    echo Retrying with Aliyun mirror...
    pip install -r requirements.txt -i https://mirrors.aliyun.com/pypi/simple/ --trusted-host mirrors.aliyun.com --no-cache-dir
    if errorlevel 1 (
        echo.
        echo [FAILED] Dependency install failed with both mirrors.
        echo Scroll up in this window to see the actual pip error.
        pause
        exit /b 1
    )
)

echo Installing PyInstaller...
pip install pyinstaller --no-cache-dir
if errorlevel 1 (
    echo [FAILED] PyInstaller install failed
    pause
    exit /b 1
)

echo [6/6] Running PyInstaller (SonvertSenseVoice.spec, onedir mode)...
pyinstaller SonvertSenseVoice.spec --noconfirm
if errorlevel 1 (
    echo.
    echo [FAILED] PyInstaller build failed - scroll up for the error.
    echo If it's a ModuleNotFoundError, add the missing module to the
    echo hiddenimports list in SonvertSenseVoice.spec and re-run this
    echo script.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo [SUCCESS] Build complete. Output is in %DIST_DIR%
echo ============================================================
echo.
echo Note: the models\ folder is intentionally NOT part of this output.
echo Sonvert.App points each service at a models folder under the
echo install directory via a setting (SenseVoiceModelsDirectory), so
echo you don't have to re-run this whole build just to update a model
echo file.
echo.
echo Suggested manual check before wiring this into Sonvert.App:
echo   1. In %DIST_DIR%, create a service_config.json like:
echo      {"port": 8878, "host": "127.0.0.1", "resource_dir": "C:\\path\\to\\your\\models"}
echo   2. Run %DIST_DIR%\SonvertSenseVoice.exe
echo   3. Open http://127.0.0.1:8878/health in a browser and confirm
echo      you get a 200 response
echo.
echo ============================================================

call %VENV_DIR%\Scripts\deactivate.bat
endlocal

echo.
echo Press any key to close this window...
pause >nul