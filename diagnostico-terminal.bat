@echo off
REM ============================================================================
REM  Diagnostico do auto-update do TERMINAL - CPE VideoWall
REM
REM  Rode no mini-PC da TELA, como ADMINISTRADOR. Ele coleta tudo que explica
REM  por que uma tela nao esta se atualizando e salva em
REM  "diagnostico-terminal.txt", ao lado deste arquivo.
REM
REM  Nao muda nada no computador. So no fim ele PERGUNTA se voce quer tentar
REM  instalar a atualizacao que ja esteja baixada.
REM ============================================================================
setlocal
chcp 1252 >nul 2>&1

set "REL=%~dp0diagnostico-terminal.txt"
set "BASE=C:\ProgramData\CPE\VideoWall"
set "UPD=%BASE%\update"
set "APP=%ProgramFiles%\CPE\VideoWall Terminal\VideoWall.Viewer.exe"
set "TAREFA=CPE VideoWall Update"

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo  Execute este arquivo como ADMINISTRADOR.
  echo  ^(botao direito no arquivo ^> "Executar como administrador"^)
  echo.
  pause
  exit /b 1
)

echo Coletando... aguarde.

> "%REL%" echo ============================================================
>> "%REL%" echo  DIAGNOSTICO DO TERMINAL - CPE VideoWall
>> "%REL%" echo  Maquina : %COMPUTERNAME%
>> "%REL%" echo  Data    : %date% %time%
>> "%REL%" echo ============================================================
>> "%REL%" echo.

>> "%REL%" echo [1] VERSAO INSTALADA DO TERMINAL
if exist "%APP%" (
  for /f "delims=" %%v in ('powershell -NoProfile -Command "(Get-Item '%APP%').VersionInfo.FileVersion" 2^>nul') do >> "%REL%" echo     %%v
) else (
  >> "%REL%" echo     NAO ENCONTRADO em "%APP%"
)
>> "%REL%" echo.

>> "%REL%" echo [2] ENERGIA  ^(2 = na tomada, 1 = na bateria, vazio = desktop sem bateria^)
>> "%REL%" echo     A tarefa do Windows NAO INICIA na bateria, com a configuracao padrao.
for /f "delims=" %%b in ('powershell -NoProfile -Command "(Get-CimInstance Win32_Battery).BatteryStatus" 2^>nul') do >> "%REL%" echo     BatteryStatus = %%b
>> "%REL%" echo.

>> "%REL%" echo [3] PASTA DE TROCA  (instalador baixado esperando para ser aplicado)
if exist "%UPD%" (
  dir "%UPD%" >> "%REL%" 2>&1
) else (
  >> "%REL%" echo     PASTA NAO EXISTE: %UPD%
)
>> "%REL%" echo.

>> "%REL%" echo [4] REGISTRO DA ULTIMA INSTALACAO  (existe a partir da versao 1.50)
if exist "%UPD%\ultima-instalacao.txt" (
  type "%UPD%\ultima-instalacao.txt" >> "%REL%" 2>&1
) else (
  >> "%REL%" echo     ainda nao existe
)
>> "%REL%" echo.

>> "%REL%" echo [5] TAREFA AGENDADA "%TAREFA%"
schtasks /query /tn "%TAREFA%" /v /fo list >> "%REL%" 2>&1
>> "%REL%" echo.

>> "%REL%" echo [6] SCRIPT QUE A TAREFA EXECUTA
if exist "%BASE%\atualizar.cmd" (
  type "%BASE%\atualizar.cmd" >> "%REL%" 2>&1
) else (
  >> "%REL%" echo     NAO EXISTE: %BASE%\atualizar.cmd
)
>> "%REL%" echo.

>> "%REL%" echo [7] ERROS REGISTRADOS PELO TERMINAL  (ultimas linhas)
for /f "delims=" %%u in ('powershell -NoProfile -Command "$p=Join-Path $env:LOCALAPPDATA 'CPE Tecnologia\VideoWall\erros.log'; if (Test-Path $p) { (Get-Content $p -Tail 25) -join [Environment]::NewLine } else { 'sem arquivo de erros' }" 2^>nul') do >> "%REL%" echo     %%u
>> "%REL%" echo.

>> "%REL%" echo [8] O TERMINAL ESTA RODANDO?
tasklist /fi "IMAGENAME eq VideoWall.Viewer.exe" >> "%REL%" 2>&1
>> "%REL%" echo.

type "%REL%"
echo.
echo ============================================================
echo  Relatorio salvo em: %REL%
echo ============================================================
echo.

if not exist "%UPD%\setup-terminal.exe" goto :fim

echo  Ha um instalador baixado esperando na pasta de troca.
echo.
echo  Posso executar agora o MESMO script que a tarefa executa, para ver se
echo  a instalacao funciona quando disparada na mao. O terminal vai FECHAR
echo  e reabrir ja atualizado.
echo.
set /p RESP=  Tentar instalar agora? (S/N):
if /i not "%RESP%"=="S" goto :fim

echo.
echo  Executando o script da tarefa...
call cmd /c "%BASE%\atualizar.cmd"
echo.
echo  Resultado (codigo %ERRORLEVEL%). Conteudo da pasta agora:
dir "%UPD%"
echo.
echo  Se o instalador sumiu da pasta, a instalacao ocorreu.

:fim
echo.
pause
endlocal
