; ============================================================
;  Instalador do Terminal (terminal burro) - CPE VideoWall
;  Inclui o runtime .NET 8 Desktop. Configura inicio automatico
;  e a tarefa agendada que aplica as atualizacoes sem UAC.
; ============================================================

#define AppName "CPE VideoWall Terminal"
; A versao vem do proprio executavel publicado (mesma do <Version> do csproj):
; e ela que o central compara para decidir se ha atualizacao.
#define AppVersion GetVersionNumbersString("..\dist\Terminal\VideoWall.Viewer.exe")
#define Publisher "CPE Tecnologia"
#define UpdateTask "CPE VideoWall Update"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\CPE\VideoWall Terminal
DefaultGroupName=CPE VideoWall
DisableProgramGroupPage=yes
OutputDir=.\saida
OutputBaseFilename=setup-terminal
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#AppName}
; Identidade visual CPE
SetupIconFile=..\assets\cpe.ico
UninstallDisplayIcon={app}\VideoWall.Viewer.exe
; Fecha o terminal em uso (auto-start do quiosque) para conseguir trocar o .exe.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[Dirs]
; Pasta de troca do auto-update: o terminal (usuario comum) precisa gravar aqui
; o instalador baixado; a tarefa agendada o executa como SYSTEM.
Name: "{commonappdata}\CPE\VideoWall"
Name: "{commonappdata}\CPE\VideoWall\update"; Permissions: users-modify

[Files]
Source: "..\dist\Terminal\VideoWall.Viewer.exe"; DestDir: "{app}"; Flags: ignoreversion
; Redistribuiveis (instalados silenciosamente se necessario)
Source: "redist\windowsdesktop-runtime-8-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsDotNet
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
; Inicio automatico no login (todos os usuarios)
Name: "{commonstartup}\CPE VideoWall Terminal"; Filename: "{app}\VideoWall.Viewer.exe"
Name: "{group}\CPE VideoWall Terminal"; Filename: "{app}\VideoWall.Viewer.exe"
Name: "{group}\Desinstalar Terminal"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CPE VideoWall Terminal"; Filename: "{app}\VideoWall.Viewer.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\windowsdesktop-runtime-8-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalando o runtime .NET 8..."; Flags: waituntilterminated; Check: NeedsDotNet
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando o WebView2..."; Flags: waituntilterminated; Check: NeedsWebView2
; Libera o app no Firewall (descoberta/controle na rede)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CPE VideoWall Terminal"""; Flags: runhidden; StatusMsg: "Configurando firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""CPE VideoWall Terminal"" dir=in action=allow program=""{app}\VideoWall.Viewer.exe"" enable=yes profile=any"; Flags: runhidden waituntilterminated; StatusMsg: "Configurando firewall..."
Filename: "{app}\VideoWall.Viewer.exe"; Description: "Iniciar o Terminal agora"; Flags: nowait postinstall skipifsilent
; Atualizacao vinda da versao ANTERIOR (1.38 e mais antigas), que executava o
; instalador direto na sessao do usuario: reabre o terminal ao final. A partir da
; 1.39 quem reabre e o "reabrir.cmd", e este passo e ignorado (o instalador roda
; como SYSTEM pela tarefa agendada, e abrir ali criaria uma instancia invisivel).
Filename: "{app}\VideoWall.Viewer.exe"; Flags: nowait runasoriginaluser; Check: NeedsSelfRelaunch

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /f /tn ""{#UpdateTask}"""; Flags: runhidden; RunOnceId: "DelUpdateTask"

[Code]
function DotNet8Present(): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Base + '\8.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function NeedsDotNet(): Boolean;
begin
  Result := not DotNet8Present();
end;

function NeedsWebView2(): Boolean;
var
  pv: String;
begin
  Result := not (
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv)
    or RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv)
  );
end;

{ Verdadeiro quando o instalador foi iniciado pela tarefa agendada (conta SYSTEM).
  O perfil do SYSTEM fica em ...\config\systemprofile, o que identifica o caso. }
function RunningAsSystem(): Boolean;
begin
  Result := Pos('systemprofile', LowerCase(ExpandConstant('{userappdata}'))) > 0;
end;

function NeedsSelfRelaunch(): Boolean;
begin
  Result := WizardSilent and (not RunningAsSystem());
end;

{ Cria o script de atualizacao e registra a tarefa agendada que o executa como
  SYSTEM. E ela que permite atualizar um quiosque sem ninguem na frente da TV:
  o terminal roda como usuario comum e nao poderia gravar em Arquivos de
  Programas nem elevar sozinho. O script grava "pronto.flag" ao terminar, que e
  o sinal para o terminal reabrir.

  A tarefa roda SOZINHA A CADA 5 MINUTOS e sai na hora quando nao ha instalador
  na pasta de troca. Antes ela so rodava sob demanda ("schtasks /run"), mas o
  terminal - sem elevacao - nao consegue disparar uma tarefa do SYSTEM: a
  atualizacao caia no plano B, que executava o instalador direto e ABRIA O PEDIDO
  DE UAC NA TV. Com o gatilho periodico, o terminal so precisa baixar o arquivo;
  quem instala e a tarefa, em silencio. }
procedure RegisterUpdateTask();
var
  Base, UpdateDir, CmdPath, Script: String;
  ResultCode: Integer;
begin
  Base := ExpandConstant('{commonappdata}\CPE\VideoWall');
  UpdateDir := Base + '\update';
  CmdPath := Base + '\atualizar.cmd';

  Script :=
    '@echo off' + #13#10 +
    'set "U=' + UpdateDir + '"' + #13#10 +
    'if not exist "%U%\setup-terminal.exe" exit /b 0' + #13#10 +
    'del /q "%U%\pronto.flag" 2>nul' + #13#10 +
    '"%U%\setup-terminal.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS' + #13#10 +
    'echo ok> "%U%\pronto.flag"' + #13#10 +
    'del /q "%U%\setup-terminal.exe" 2>nul' + #13#10;

  if not SaveStringToFile(CmdPath, Script, False) then
    Exit;

  Exec(ExpandConstant('{sys}\schtasks.exe'),
       '/create /f /tn "{#UpdateTask}" /sc MINUTE /mo 5 /ru SYSTEM /rl HIGHEST' +
       ' /tr "cmd /c \"' + CmdPath + '\""',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterUpdateTask();
end;
