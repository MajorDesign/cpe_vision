@echo off
REM ============================================================
REM  Publica o VideoWall CPE (framework-dependent) e gera os
REM  instaladores (Inno Setup):
REM    dist\Controlador / dist\Terminal      -> binarios publicados
REM    instaladores\saida\setup-*.exe        -> instaladores finais
REM ============================================================
setlocal
set DOTNET="C:\Program Files\dotnet\dotnet.exe"
if not exist %DOTNET% set DOTNET=dotnet
set "ROOT=%~dp0"
set "CTRL=%ROOT%dist\Controlador"
set "TERM=%ROOT%dist\Terminal"

echo [1/3] Publicando Controlador (framework-dependent)...
%DOTNET% publish "%ROOT%src\VideoWall\VideoWall.csproj" -c Release -r win-x64 --self-contained false -o "%CTRL%"
if errorlevel 1 goto erro

echo.
echo [2/3] Publicando Terminal (framework-dependent, em pasta - necessario para o VLC)...
%DOTNET% publish "%ROOT%src\VideoWall.Viewer\VideoWall.Viewer.csproj" -c Release -r win-x64 --self-contained false -o "%TERM%"
if errorlevel 1 goto erro

echo.
echo Copiando binario do terminal para o central servir (auto-update LAN)...
if not exist "%CTRL%\terminal-update" mkdir "%CTRL%\terminal-update"
copy /Y "%TERM%\VideoWall.Viewer.exe" "%CTRL%\terminal-update\" >nul

echo.
echo [3/3] Gerando instaladores (Inno Setup)...
set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" (
  "%ISCC%" "%ROOT%instaladores\setup-terminal.iss"
  "%ISCC%" "%ROOT%instaladores\setup-controlador.iss"
  echo Instaladores em: "%ROOT%instaladores\saida"
) else (
  echo [aviso] Inno Setup nao encontrado - pulei a geracao dos instaladores.
)

echo.
echo Concluido.
pause
exit /b 0

:erro
echo.
echo FALHA na publicacao.
pause
exit /b 1
