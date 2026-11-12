@echo off
rem Builds libSoundStageNative.so for arm64-v8a and armeabi-v7a with the NDK that
rem ships with Unity, and copies them to Assets\Plugins\Android\libs\<abi>\.
rem Usage:  build_plugin.cmd                (auto-detects Unity 2022.3.62f3's NDK)
rem         build_plugin.cmd "D:\path\to\NDK"
rem Close Unity before running so it imports the new libraries cleanly.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_plugin.ps1" %*
if errorlevel 1 (echo. & echo BUILD FAILED & pause & exit /b 1)
pause
