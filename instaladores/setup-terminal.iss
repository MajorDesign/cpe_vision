; ============================================================
;  Instalador do Terminal (terminal burro) - CPE VideoWall
;  Inclui o runtime .NET 8 Desktop. Configura inicio automatico.
; ============================================================

#define AppName "CPE VideoWall Terminal"
#define AppVersion "1.0.0"
#define Publisher "CPE Tecnologia"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
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
