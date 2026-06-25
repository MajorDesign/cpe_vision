; ============================================================
;  Instalador do Controlador (painel central) - CPE VideoWall
;  Inclui o runtime .NET 8 Desktop, os nativos do VLC e o
;  bootstrapper do WebView2.
; ============================================================

#define AppName "CPE VideoWall Controlador"
#define AppVersion "1.0.0"
#define Publisher "CPE Tecnologia"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\CPE\VideoWall Controlador
DefaultGroupName=CPE VideoWall
DisableProgramGroupPage=yes
OutputDir=.\saida
OutputBaseFilename=setup-controlador
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#AppName}
; Identidade visual CPE
SetupIconFile=..\assets\cpe.ico
UninstallDisplayIcon={app}\VideoWall.exe
; Fecha o controlador em uso para conseguir trocar os arquivos.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[Files]
; Todos os arquivos publicados do Controlador (app + libvlc + webview2 etc.)
Source: "..\dist\Controlador\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Redistribuiveis
Source: "redist\windowsdesktop-runtime-8-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsDotNet
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{autodesktop}\CPE VideoWall Controlador"; Filename: "{app}\VideoWall.exe"; Tasks: desktopicon
Name: "{group}\CPE VideoWall Controlador"; Filename: "{app}\VideoWall.exe"
Name: "{group}\Desinstalar Controlador"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\windowsdesktop-runtime-8-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalando o runtime .NET 8..."; Flags: waituntilterminated; Check: NeedsDotNet
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Instalando o WebView2..."; Flags: waituntilterminated; Check: NeedsWebView2
; Libera o app no Firewall (descoberta/controle das telas na rede)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""CPE VideoWall Controlador"""; Flags: runhidden; StatusMsg: "Configurando firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""CPE VideoWall Controlador"" dir=in action=allow program=""{app}\VideoWall.exe"" enable=yes profile=any"; Flags: runhidden waituntilterminated; StatusMsg: "Configurando firewall..."
; Em atualização silenciosa (auto-update), reabre o controlador automaticamente
; (como usuário normal, sem privilégio elevado).
Filename: "{app}\VideoWall.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent
; Em instalação manual, oferece iniciar ao final.
Filename: "{app}\VideoWall.exe"; Description: "Iniciar o Controlador agora"; Flags: nowait postinstall skipifsilent

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
  // Presente se a chave do Evergreen Runtime existir no registro.
  Result := not (
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv)
    or RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv)
  );
end;
