@echo off
setlocal

REM 在脚本所在目录执行，避免工作目录不对
cd /d "%~dp0"

dotnet run -c Debug -r win-x64 --project "GifRecorder.csproj"
