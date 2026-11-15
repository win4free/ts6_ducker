@echo off
dotnet publish TS6Ducker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
if errorlevel 1 exit /b 1
copy /y *.txt publish\ >nul
start /wait "" publish\TS6Ducker.exe --self-test "%CD%\test-results.txt"
type test-results.txt
rem To build the installer, install Inno Setup, then run: ISCC.exe installer.iss
