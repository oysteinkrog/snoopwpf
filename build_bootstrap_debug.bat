@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
if errorlevel 1 (
    echo vcvarsall.bat failed
    exit /b 1
)
msbuild "C:\work\snoopwpf\SnoopWPF.Agent.Bootstrap\SnoopWPF.Agent.Bootstrap.vcxproj" /p:Configuration=Debug /p:Platform=x64 /t:Build /v:m
exit /b %errorlevel%
