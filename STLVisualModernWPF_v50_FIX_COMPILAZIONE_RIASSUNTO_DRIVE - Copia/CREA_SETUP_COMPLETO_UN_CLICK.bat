@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title STL Visual Modern WPF - Build completo + Installer v48

echo ======================================================
echo  STL Visual Modern WPF - CREA SETUP COMPLETO v48
echo  Un solo doppio clic: compila + crea installer Inno
echo  Windows x64 / x86 / ARM64 - self contained .NET 8
echo ======================================================
echo.

echo 1^) Chiudo eventuale programma aperto, cosi' il setup puo' sovrascrivere i file...
taskkill /IM STLVisualModernWPF.exe /F >nul 2>nul

echo.
echo 2^) Pulizia vecchie cartelle publish e vecchi installer...
if exist "STLVisualModernWPF\publish" rmdir /s /q "STLVisualModernWPF\publish"
if exist "STLVisualModernWPF\publish-win-x64" rmdir /s /q "STLVisualModernWPF\publish-win-x64"
if exist "STLVisualModernWPF\publish-win-x86" rmdir /s /q "STLVisualModernWPF\publish-win-x86"
if exist "STLVisualModernWPF\publish-win-arm64" rmdir /s /q "STLVisualModernWPF\publish-win-arm64"
if exist "Output" rmdir /s /q "Output"
mkdir "Output" >nul 2>nul

cd STLVisualModernWPF

echo.
echo 3^) Ripristino pacchetti NuGet...
dotnet restore STLVisualModernWPF.csproj
if %errorlevel% neq 0 goto ERRORE_RESTORE

echo.
echo 4^) Compilo versione Windows x64 self-contained...
dotnet publish STLVisualModernWPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish-win-x64
if %errorlevel% neq 0 goto ERRORE_BUILD

echo.
echo 5^) Compilo versione Windows x86 self-contained...
dotnet publish STLVisualModernWPF.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish-win-x86
if %errorlevel% neq 0 goto ERRORE_BUILD

echo.
echo 6^) Compilo versione Windows ARM64 self-contained...
dotnet publish STLVisualModernWPF.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o publish-win-arm64
if %errorlevel% neq 0 goto ERRORE_BUILD

cd ..

echo.
echo 7^) Verifico che le tre cartelle publish siano state create...
if not exist "STLVisualModernWPF\publish-win-x64\STLVisualModernWPF.exe" goto ERRORE_PUBLISH
if not exist "STLVisualModernWPF\publish-win-x86\STLVisualModernWPF.exe" goto ERRORE_PUBLISH
if not exist "STLVisualModernWPF\publish-win-arm64\STLVisualModernWPF.exe" goto ERRORE_PUBLISH

echo.
echo 8^) Creo automaticamente il setup con Inno Setup...
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not defined ISCC goto ERRORE_INNO_MANCANTE

echo Uso Inno Setup: %ISCC%
"%ISCC%" "setup_inno.iss"
if %errorlevel% neq 0 goto ERRORE_INNO

echo.
echo ======================================================
echo  FATTO. INSTALLER CREATO.
echo  File da distribuire:
echo  Output\STLVisualModernWPF_Setup_v48_UNIVERSALE_CORRETTO.exe
echo ======================================================
echo.
explorer "%cd%\Output"
pause
exit /b 0

:ERRORE_RESTORE
echo.
echo ERRORE: restore .NET fallito.
echo Installa .NET 8 SDK e controlla la connessione Internet.
pause
exit /b 1

:ERRORE_BUILD
echo.
echo ERRORE: build .NET fallita. Controlla il codice e .NET 8 SDK.
pause
exit /b 1

:ERRORE_PUBLISH
echo.
echo ERRORE: non sono state create correttamente le cartelle publish-win-x64 / publish-win-x86 / publish-win-arm64.
echo Controlla che .NET 8 SDK sia installato e che non ci siano errori sopra.
pause
exit /b 1

:ERRORE_INNO_MANCANTE
echo.
echo ERRORE: Inno Setup non trovato.
echo Installa Inno Setup 6, poi rilancia questo file BAT.
echo Non devi aprire setup_inno.iss manualmente.
pause
exit /b 1

:ERRORE_INNO
echo.
echo ERRORE: creazione setup Inno fallita.
pause
exit /b 1
