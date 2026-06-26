@echo off
echo Build publish .NET 8 WPF...
dotnet publish STLVisualModernWPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
pause
