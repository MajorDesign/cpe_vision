# CPE VideoWall — Arquitetura

Documento de manutenção: como o sistema funciona por dentro, por que cada decisão foi
tomada e onde estão as minas terrestres. Leia [CLAUDE.md](CLAUDE.md) antes deste.

---

## 1. Topologia

```
        ┌──────────────────────────────┐
        │  CONTROLADOR (1 máquina)     │  src/VideoWall  (WPF, MVVM)
        │  - monta a parede            │
        │  - projeta nas telas         │
        │  - controle ao vivo          │
        │  - hub de atualização        │
        └──────┬───────────────────────┘
               │  rede local (mesma sub-rede, cabeada)
   ┌───────────┼───────────┬───────────┐
   ▼           ▼           ▼           ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│TERMINAL│ │TERMINAL│ │TERMINAL│ │TERMINAL│   src/VideoWall.Viewer (WPF tela cheia)
│ + TV   │ │ + TV   │ │ + TV   │ │ + TV   │   8 mini-PCs Dell em quiosque
└────────┘ └────────┘ └────────┘ └────────┘
```

O terminal é **burro por projeto**: não tem interface de operação, não decide nada,
só anuncia que existe, obedece comandos e responde perguntas. Toda a inteligência
está no controlador.

Com uma exceção importante: **o terminal é a fonte da verdade sobre o que está no ar.**
Ele guarda o layout aplicado, com as URLs navegadas ao vivo, e o devolve quando
perguntado (porta 48017) e o persiste em disco. Isso é o que permite fechar o
controlador, reabrir e reconstruir a parede — e o terminal voltar exibindo o mesmo
conteúdo depois de uma atualização ou queda de energia.

## 2. Os três projetos

### `src/VideoWall.Network` — o contrato

Biblioteca sem interface, referenciada pelos dois apps. Contém as classes de protocolo
(serializadas em JSON) e, para cada canal, um par **`XServer` (terminal) / `XClient` ou
`XSender` (controlador)**. Nenhuma regra de negócio.

Classes de protocolo:

| Classe | Papel |
|---|---|
| `ViewerInfo` | Anúncio do terminal: id (nome da máquina), nome, IP, porta de comando, se o overlay de HW está ligado |
| `ControllerInfo` | Anúncio do central: IP e porta de atualização |
| `ScreenCommand` | Ordem do controlador: `show-browser`, `show-layout`, `clear`, `restart`, `toggle-overlay` |
| `ScreenSource` | Uma fonte dentro de um layout projetado (ver §4) |
| `RemoteInputEvent` | Um evento do controle ao vivo: mouse, rolagem, tecla, navegação, zoom, marcação |
| `CellState` | Estado de uma célula: URL atual + posição de rolagem |
| `TerminalPackage` | Instalador do terminal servido pelo central (versão + caminho) |

### `src/VideoWall` — o controlador

WPF com MVVM. O centro de gravidade é
[MainViewModel.cs](src/VideoWall/ViewModels/MainViewModel.cs): coleções observáveis
(`Elements` = a parede, `RemoteScreens` = as telas na rede, `Layouts`, `Favorites`,
`Schedules`) e ~40 `ICommand`. O code-behind de
[Views/MainWindow.xaml.cs](src/VideoWall/Views/MainWindow.xaml.cs) cuida do que é
inerentemente visual: arrastar/redimensionar na prévia, duplo-clique para editar,
abrir as janelas auxiliares.

Janelas auxiliares: `UrlEditWindow` (editar/pré-visualizar uma página com WebView2 ao
vivo), `TextEditWindow`, `SchedulerWindow` (agenda + rotação), `LiveControlWindow`
(controle remoto), `WindowPickerWindow`.

Serviços (`Services/`) fazem persistência em JSON sob `%LocalAppData%\VideoWall`:
`LayoutService` (layouts nomeados), `FavoritesService`, `ScheduleService`,
`SettingsService` (layout principal e preferências), `TerminalPackageService`
(cache do instalador do terminal — ver §7).

### `src/VideoWall.Viewer` — o terminal

Uma janela sem borda em tela cheia. Todo o trabalho está em
[MainWindow.xaml.cs](src/VideoWall.Viewer/MainWindow.xaml.cs), que no `OnLoaded` sobe:
o anúncio UDP (`UdpBeacon`), **seis servidores TCP**, o timer que salva o layout, o
timer de auto-update, e restaura o layout salvo.

Auxiliares: `OverlayWindow` (janela sempre-no-topo para live/PiP), `TerminalLayoutStore`
(último layout em disco), `TerminalSettings` (preferência de overlay de HW),
`TerminalUpdater` (auto-update).

## 3. Canais de rede

Todos são TCP com JSON, exceto a descoberta (UDP broadcast). O firewall é liberado
**por programa** nos dois instaladores, então portas novas não exigem regra nova.

| Porta | Quem escuta | Para quê | Classes |
|---|---|---|---|
| 48010/UDP | ambos | Descoberta. Terminal anuncia `ViewerInfo` a cada 2s (offline após 6s sem anúncio); central anuncia `ControllerInfo` | `UdpBeacon`, `ViewerDiscoveryListener`, `ControllerLocator` |
| 48011 | terminal | Comandos | `CommandServer` / `CommandSender` |
| 48013 | terminal | Controle ao vivo (fluxo persistente de eventos, fila `Channel`) | `LiveInputServer` / `LiveInputSender` |
| 48014 | terminal | Miniatura da tela inteira (JPEG) | `ThumbnailServer` / `ThumbnailClient` |
| 48015 | terminal | Estado de uma célula (URL + rolagem) | `LiveStateServer` / `LiveStateClient` |
| 48016 | terminal | Espelho de vídeo de uma célula (~10 JPEG/s) | `LiveViewServer` / `LiveViewClient` |
| 48017 | terminal | Layout atual com URLs ao vivo | `LayoutQueryServer` / `LayoutQueryClient` |
| 48020 | **central** | Auto-update pela LAN (`/version`, `/setup`) | `UpdateServer` |

Repare que **48020 é o único canal em que o terminal é o cliente** e o central o servidor.

O `UdpBeacon` envia para o broadcast dirigido de **cada interface IPv4 ativa**, não só
para 255.255.255.255 — máquinas com Hyper-V, VPN ou Wi-Fi + cabo simplesmente não
recebiam o anúncio de outro jeito. Wi-Fi com *client isolation* bloqueia a descoberta.

## 4. Modelo de dados: parede × layout projetado

São **dois modelos diferentes** e a conversão entre eles é onde nascem bugs.

**No controlador**, uma fonte é um `WallElement` (`Models/`): `BrowserElement`,
`ColorElement`, `TextElement`, `ImageElement`, `WindowCaptureElement`, `CameraElement`.
Coordenadas em **pixels da parede virtual**, com `ZIndex`.

**Na rede**, vira um `ScreenSource` com coordenadas **normalizadas 0..1** relativas à
tela do terminal (`(X + OffsetX) / WallWidth`, etc.), para independer de resolução.
Só três tipos atravessam a rede: `browser`, `color`, `text`. Espelho de aplicativo
(`WindowCapture`) e câmera **não são enviados** — só funcionam na parede local.

A volta (`SourceToElement` no `MainViewModel`) reconstrói `WallElement` a partir do que
o terminal reporta, e é o que permite reconstruir a parede ao reabrir o controlador.

**A ordem da lista é o endereço da célula.** O controle ao vivo e as marcações miram
uma célula por índice (`RemoteInputEvent.TargetIndex`), e esse índice é a posição em
`Elements.OrderBy(ZIndex)` filtrada por browser/color/text. Se você mudar essa ordenação
ou o filtro num dos lados, o controle ao vivo passa a agir na célula errada — mude
sempre nos dois.

## 5. Fluxos principais

### Projetar uma parede numa tela

1. Operador seleciona uma tela em "TELAS NA REDE" e clica **▶ Projetar na tela**.
2. `SendLayoutToScreen` converte `Elements` → `List<ScreenSource>` normalizados e envia
   `show-layout` pela 48011.
3. O terminal chama `ApplyLayout`, que **reconcilia por índice** (`ReconcileSlot`): para
   cada posição, reaproveita o elemento existente se o tipo bate e só recarrega o
   navegador se a **URL projetada** daquela vaga mudou.

   Essa reconciliação é essencial: sem ela, reprojetar reiniciaria todas as páginas,
   perdendo rolagem, login e a navegação feita pelo controle ao vivo. Por isso
   `_slotUrls` guarda a URL **projetada** (não a navegada ao vivo) — é a chave de
   comparação.
4. Fontes marcadas como `Overlay` (live/PiP) não entram na superfície: viram um
   `Border` transparente que segura o índice e uma `OverlayWindow` posicionada em
   pixels físicos por cima (ver §8, airspace).
5. O terminal guarda o layout em `_currentSources` e o persiste (`TerminalLayoutStore`).

### Controle ao vivo (operar a página que está na TV)

Foi reescrito na 1.26 e o motivo importa: antes o controlador abria um **navegador
próprio**, que divergia do terminal — sessões e logins separados, e o operador não
conseguia logar num sistema e ver o resultado na TV. Hoje o controlador **não tem
navegador nenhum** nesse fluxo, é um espelho:

1. `LiveControlWindow` abre com um `Image` e pede à 48016 um fluxo de JPEGs da célula.
2. Mouse, rolagem e teclado do operador viram `RemoteInputEvent` com coordenadas
   normalizadas sobre a área desenhada, e vão pela 48013.
3. O terminal injeta no WebView2 daquela célula via **CDP**
   (`Input.dispatchMouseEvent` / `dispatchKeyEvent`).
4. Navegação é autoritativa: o controlador manda `nav` a cada `SourceChanged`; o
   terminal só navega se a URL for realmente diferente (evita duplicar o clique).
5. Ao reabrir, `LiveStateClient` (48015) pergunta em que página e rolagem a célula está,
   e o espelho começa de onde parou.

O zoom e as **marcações** (caneta, seta, retângulo, marca-texto) vão pelo mesmo canal.
Marcação não é desenhada com WPF: o terminal **injeta um SVG dentro da própria página**
(`__cpeAnnoStart/Point/End/Clear`), porque qualquer elemento WPF desenhado sobre um
WebView2 fica invisível.

### Miniatura ao vivo

`_thumbnailTimer` (3s) pede a todas as telas (48014) uma foto. O terminal captura a
própria tela por **GDI BitBlt** na thread de UI, reduz e devolve JPEG. Para a tela
selecionada, o controlador ainda **recorta essa foto por célula** (mesma normalização
do envio) e usa cada pedaço como prévia do elemento correspondente — é assim que a
pré-visualização grande mostra conteúdo real.

## 6. Agendamento e rotação

`ScheduleService` guarda entradas com horário, dias, layout **e tela alvo**. Ao disparar,
o VM seleciona a tela, aplica o layout e projeta. A "rotação" é um `DispatcherTimer` que
cicla uma sequência de layouts numa tela a cada X minutos. Ambos reusam exatamente o
caminho de projeção manual — não há um segundo caminho de código para manter.

## 7. Auto-update (o mecanismo mais delicado)

Requisitos do campo que moldaram o desenho: as TVs ficam ligadas semanas sem reiniciar;
os terminais podem não ter internet; ninguém vai até a TV; e o app fica em Arquivos de
Programas rodando como usuário comum — **não pode se auto-substituir nem elevar**.

```
GitHub Releases ──(30 min, só o central)──► CONTROLADOR
                                             TerminalPackageService
                                             baixa setup-terminal.exe p/ %LocalAppData%
                                                     │ UpdateServer :48020  /version /setup
                                                     ▼
                                            TERMINAL (a cada 30 min)
                                            TerminalUpdater: LAN primeiro, GitHub reserva
                                                     │ baixa p/ C:\ProgramData\CPE\VideoWall\update
                                                     │ schtasks /run "CPE VideoWall Update"
                                                     ▼
                                            Tarefa SYSTEM → atualizar.cmd
                                            instala /VERYSILENT, grava pronto.flag
                                                     ▼
                                            reabrir.cmd (sessão do usuário) reabre o app
                                                     ▼
                                            terminal restaura o layout salvo, já logado
```

Detalhes que não são óbvios:

- **A versão do pacote vem da TAG da release**, anotada num `version.txt` ao lado do
  instalador em cache; a versão embutida no `.exe` (que os `.iss` gravam desde a 1.39,
  via `GetVersionNumbersString`) é só a reserva. Confiar apenas no arquivo já quebrou em
  campo: instaladores gerados antes da 1.39 saíram **sem versão embutida**, e o central
  passou a anunciar `0.0.0` — nenhum terminal atualizava, e ele rebaixava os mesmos
  60 MB a cada meia hora. Para alimentar o central à mão, ponha o `setup-terminal.exe`
  **e um `version.txt`** na pasta `terminal-update`.
- **Por que uma tarefa agendada.** É o único jeito de instalar sem UAC num quiosque cujo
  usuário não é administrador. O instalador (que roda elevado) a registra como SYSTEM.
- **Por que um `reabrir.cmd` separado.** A tarefa roda na sessão 0; se ela abrisse o app,
  ele subiria invisível e ainda assim tomaria as portas TCP, quebrando a tela de verdade.
  Quem reabre é um script iniciado pelo próprio terminal, na sessão do usuário, antes de
  disparar a instalação; ele espera o `pronto.flag` (até 10 min) e nunca abre uma segunda
  instância.
- **`NeedsSelfRelaunch` no `.iss`** só relança o app quando a instalação silenciosa **não**
  veio da conta SYSTEM (detectado por `systemprofile` em `{userappdata}`). Isso cobre a
  atualização vinda de versões anteriores a 1.39, sem criar o processo zumbi da sessão 0.
- **Escalonamento:** o primeiro ciclo de cada tela é sorteado entre 3 e 30 minutos, para
  as 8 TVs não reiniciarem juntas.
- **Ponto de segurança conhecido:** `C:\ProgramData\CPE\VideoWall\update` é gravável pelo
  usuário e a tarefa SYSTEM executa o que está lá. Aceitável em mini-PC dedicado; se
  esses PCs ganharem logon de terceiros, assine o instalador e valide a assinatura antes
  de executar.

O controlador se atualiza pelo mesmo princípio, mas simples: o pré-load baixa
`setup-controlador.exe` da release e o executa (com UAC, já que ali há um operador).

## 8. Armadilhas conhecidas

Cada item abaixo foi descoberto em produção. Não "conserte" nenhum sem entender o porquê.

**Airspace do WebView2.** Cada WebView2 é uma janela nativa. Dois deles na mesma janela
WPF **não respeitam z-order**, e qualquer elemento WPF (borda, canvas, texto) desenhado
por cima **desaparece**. Consequências vivas no código: a live é uma `OverlayWindow`
top-level separada (composta pelo DWM acima das outras); as marcações são SVG injetado
na página; a moldura colorida (`ColorElement`) aparece na prévia do controlador mas não
sobre um navegador na TV. Sobreposição com transparência entre duas páginas é
**impossível** nesse modelo — não há alpha entre janelas irmãs.

**Largura canônica de 1920 CSS.** Todo navegador é diagramado numa largura lógica fixa;
só o zoom muda (`ApplyCanonicalZoom` no terminal, `ApplyFitZoom` no controle). Assim,
redimensionar a célula não reflui a página (preserva rolagem) e controlador e terminal
sempre concordam sobre coordenadas. Efeito colateral aceito: numa grade 2×2, cada página
aparece inteira e reduzida.

**Coordenadas × zoom no CDP.** O CDP trabalha em **pixels CSS**, então o terminal calcula
`x = ev.X * (ActualWidth / ZoomFactor)`. Esquecer de dividir pelo zoom produz o sintoma
clássico: a rolagem funciona, mas o clique cai no lugar errado e nada navega.

**Live do YouTube.** Encerrado em definitivo na 1.38: nenhum player fora do navegador
toca a live. VLC e afins recebem **403** nos segmentos `.ts` do googlevideo mesmo com
User-Agent, Referer, Origin e o IP correto — é a proteção anti-bot (assinatura "n"), que
exige o JS do YouTube rodando. Todo o código de VLC foi revertido. Além disso, as lives
do cliente têm **incorporação desativada**, então o `player.html` (mapeado no host virtual
`cpe.live`) cai para a página `watch` normal, e um script injetado força play, esconde a
interface e entra em tela cheia via CDP. Se algum dia ativarem a incorporação no Studio,
o caminho limpo do embed volta sozinho.

**Tela cheia da live é obrigatória, não cosmética.** Na página `watch` o vídeo vem
cercado por cabeçalho, logo e título do YouTube — inaceitável numa TV de operação. Só o
atalho **F** do player entra em tela cheia de forma confiável, e ele exige gesto do
usuário, que num quiosque não existe: por isso a tecla é enviada por **CDP**, que conta
como gesto. [`YouTubeFullscreen`](src/VideoWall.Viewer/YouTubeFullscreen.cs) centraliza
isso e vale para **as células da parede e para a live sobreposta**. O laço é oportunista:
tenta enquanto não está em tela cheia e **para ao conseguir** — reenviar F com o player
já em tela cheia o tira de lá e faz a live re-bufferizar (era o sintoma de "sempre
carregando"). Perdeu a tela cheia (navegação, recarga, anúncio)? O evento
`ContainsFullScreenElementChanged` re-arma o laço; é isso que faz o "sempre" durar o dia
inteiro. Em página que não é do YouTube o laço não faz nada — enviar F ali digitaria a
letra num campo de texto. Efeito colateral conhecido: com o player em tela cheia, a
camada SVG de marcações fica atrás dele (o elemento em fullscreen cobre a página), então
marcar sobre uma live não funciona.

**Vídeo pesado disputa a GPU.** Live + dashboard WebGL no mesmo mini-PC engasgam. Existem
o botão de overlay de vídeo por hardware (mais leve, mas preto em algumas placas) e a
trava de qualidade da live. A solução robusta continua sendo **separar dashboard e
câmeras em terminais diferentes**.

**Captura GDI não vê vídeo em overlay de hardware.** Aparece preto na miniatura. Nunca
use a miniatura para concluir que "o vídeo não está tocando".

**WebView2 em Arquivos de Programas.** A pasta de dados padrão fica no diretório do app,
que é somente leitura para o quiosque, e o WebView2 **falha ao iniciar** (tela preta).
Os dois apps forçam `WEBVIEW2_USER_DATA_FOLDER` para `%LocalAppData%` no `App.OnStartup`.
Argumentos do navegador também são definidos ali, **antes** de qualquer WebView2 existir.

**Autoplay.** O WebView2 bloqueia vídeo sem gesto do usuário; num quiosque não há gesto.
Resolvido com `--autoplay-policy=no-user-gesture-required`.

**Publicação.** O terminal publica como arquivo único; o controlador publica em **pasta**.
Instância rodando trava o `.exe` e a publicação falha em silêncio — mate os processos e
limpe `dist\` antes.

**DPI.** O app usa escala DPI única (System Aware); telas com escalas diferentes podem
desalinhar. Evoluir para Per-Monitor DPI é trabalho em aberto.

## 9. O que falta

- Auto-update do controlador ainda exige o UAC do instalador (aceitável: há operador).
- Editor de layout dedicado por terminal (16:9). Hoje reaproveita a parede, e as
  coordenadas podem esticar.
- Enviar câmera RTSP direta ao terminal (exigiria empacotar VLC no terminal — hoje o
  cliente só tem câmera via YouTube).
- Perfis de usuário e níveis de acesso; grupos de fontes na biblioteca; editor de grade.
- Per-Monitor DPI.
