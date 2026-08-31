# CPE VideoWall â€” guia para quem (ou o que) for dar manutenÃ§Ã£o

Leia este arquivo por inteiro antes de mexer no cÃ³digo. Ele Ã© curto de propÃ³sito;
o detalhe estÃ¡ em [ARQUITETURA.md](ARQUITETURA.md) (como as peÃ§as conversam e as
armadilhas jÃ¡ pagas) e em [IMPLANTACAO.md](IMPLANTACAO.md) (instalar e atualizar em campo).

## O que Ã©

Controlador de videowall para a **CPE Tecnologia**, equivalente comercial ao QX2-Wall
Controller. O operador monta uma "parede" de fontes (pÃ¡ginas web, cor, texto, live do
YouTube) num painel central e a **projeta em TVs espalhadas pelo prÃ©dio**, cada uma
ligada a um mini-PC. TambÃ©m controla essas pÃ¡ginas remotamente (mouse, teclado, login,
marcaÃ§Ãµes), vÃª miniatura ao vivo de cada tela, agenda trocas de layout e mantÃ©m todos
os mini-PCs atualizados sozinho.

CenÃ¡rio real em produÃ§Ã£o: **8 TVs**, cada uma com um mini-PC Dell, rede cabeada, tipicamente
mostrando um dashboard GIS pesado e uma live de cÃ¢mera do YouTube.

## VocabulÃ¡rio (use estes termos)

| Termo | Significa |
|---|---|
| **Controlador** / central | O painel de controle (`src/VideoWall`). Uma mÃ¡quina sÃ³. |
| **Terminal** / tela | O mini-PC atrÃ¡s da TV (`src/VideoWall.Viewer`). SÃ£o 8. |
| **Parede** | O conjunto de fontes posicionadas que se projeta numa tela. |
| **Fonte** / elemento | Um item da parede: navegador, cor, texto, imagem, live. |
| **CÃ©lula** / vaga (slot) | O lugar de uma fonte dentro da tela do terminal. |
| **Layout** | Uma parede salva em JSON, com nome. |

## Estrutura

```
src/VideoWall/          Controlador â€” app WPF (MVVM), o Ãºnico com interface de operaÃ§Ã£o
src/VideoWall.Viewer/   Terminal â€” app WPF de tela cheia, "burro" (sÃ³ obedece)
src/VideoWall.Network/  Biblioteca compartilhada: TODO o protocolo de rede
instaladores/           Scripts Inno Setup dos dois instaladores
publicar.bat            Publica os binÃ¡rios e gera os dois instaladores
publicar-release.ps1    Cria a release no GitHub e sobe os assets
```

`VideoWall.Network` Ã© a fronteira: **se controlador e terminal precisam concordar sobre
algo, isso mora lÃ¡** (classes de protocolo + par cliente/servidor de cada canal).

Os arquivos grandes sÃ£o [MainViewModel.cs](src/VideoWall/ViewModels/MainViewModel.cs) (~1900
linhas, todo o estado do controlador) e [MainWindow.xaml.cs do terminal](src/VideoWall.Viewer/MainWindow.xaml.cs)
(~960 linhas, renderizaÃ§Ã£o e todos os servidores). Comece por eles.

## Comandos

```powershell
# Compilar (o dotnet costuma estar fora do PATH nesta mÃ¡quina)
& "C:\Program Files\dotnet\dotnet.exe" build VideoWall.sln -c Release

# Publicar binÃ¡rios + gerar os dois instaladores (precisa do Inno Setup 6)
.\publicar.bat

# Publicar a release no GitHub (Ã© o que dispara o auto-update em campo)
$env:GITHUB_TOKEN = "ghp_..."   # escopo repo
.\publicar-release.ps1
```

RepositÃ³rio: **MajorDesign/cpe_vision** (pÃºblico). O `git push` usa a credencial do
Windows jÃ¡ em cache. HistÃ³rico Ã© linear na `main`, um commit por versÃ£o.

## Regras do projeto (quebrar estas dÃ¡ bug em campo)

1. **Toda alteraÃ§Ã£o de cÃ³digo sobe a versÃ£o nos DOIS csproj**, sempre iguais:
   `src/VideoWall/VideoWall.csproj` e `src/VideoWall.Viewer/VideoWall.Viewer.csproj`.
   Ã‰ esse nÃºmero que o auto-update compara. Se vocÃª mexeu no terminal e nÃ£o subiu a
   versÃ£o, a mudanÃ§a **nÃ£o chega Ã s TVs**.
2. **Mudou o protocolo? Mude os dois lados.** Controlador e terminal podem estar em
   versÃµes diferentes por atÃ© uma hora durante um rollout; campos novos precisam ser
   opcionais (o JSON Ã© `System.Text.Json`, campo ausente = default).
3. **Os arquivos `.iss` sÃ£o ASCII sem acento** e a versÃ£o deles Ã© lida do executÃ¡vel
   publicado (`GetVersionNumbersString`) â€” nÃ£o escreva versÃ£o Ã  mÃ£o lÃ¡.
4. **Antes de `publicar.bat`, mate os processos e limpe `dist\`.** ExecutÃ¡vel travado
   por instÃ¢ncia rodando faz a publicaÃ§Ã£o falhar em silÃªncio e empacotar binÃ¡rio
   ANTIGO; o cache do WebView2 (`EBWebView`) em `dist\` incha o instalador em dezenas de MB.
5. **Nada de bloquear a thread de UI do terminal.** Ela renderiza 4+ navegadores e
   atende 7 servidores TCP; travar significa TV branca. Na `VideoWall.Network`, use
   `ConfigureAwait(false)`.
6. **Erro de rede nÃ£o vira diÃ¡logo â€” mas tambÃ©m nÃ£o vira silÃªncio.** O terminal fica
   ligado 24/7 sem ninguÃ©m por perto: engula a exceÃ§Ã£o **e tente de novo**. `catch {}`
   sem retentativa jÃ¡ deixou uma tela verde na lista e surda a tudo (ver "terminal
   zumbi" em [ARQUITETURA.md](ARQUITETURA.md#armadilhas-conhecidas)).

## Onde mexer para tarefas comuns

| Quero... | VÃ¡ em |
|---|---|
| Adicionar um tipo de fonte na parede | `Models/` (novo `WallElement`), `App.xaml` (DataTemplate), `MainViewModel.Add*`, `ScreenSource` + serializaÃ§Ã£o, e o `ApplyLayout` do terminal |
| Mudar o que o terminal mostra | [VideoWall.Viewer/MainWindow.xaml.cs](src/VideoWall.Viewer/MainWindow.xaml.cs) â†’ `ApplyLayout` / `ReconcileSlot` |
| Mudar botÃµes/painÃ©is do controlador | [Views/MainWindow.xaml](src/VideoWall/Views/MainWindow.xaml) + `MainViewModel` (comandos) |
| Novo comando remoto | `ScreenCommand` (constante), `CommandSender` no controlador, `ApplyCommand` no terminal |
| Novo canal de rede | Par `XServer`/`XClient` em `VideoWall.Network`, porta nova na faixa 4801x, e regra de firewall jÃ¡ Ã© por programa (nÃ£o precisa mexer) |
| Auto-update | [TerminalPackageService.cs](src/VideoWall/Services/TerminalPackageService.cs) (central), [TerminalUpdater.cs](src/VideoWall.Viewer/TerminalUpdater.cs) (terminal), [setup-terminal.iss](instaladores/setup-terminal.iss) (tarefa SYSTEM) |
| PersistÃªncia (layouts, favoritos, agenda) | `Services/LayoutService`, `FavoritesService`, `ScheduleService`, `SettingsService` â€” tudo JSON em `%LocalAppData%\VideoWall` |

## Armadilhas que jÃ¡ custaram caro

Estas sÃ£o regras empÃ­ricas â€” leia o porquÃª em [ARQUITETURA.md](ARQUITETURA.md#armadilhas-conhecidas)
antes de "consertar" alguma delas:

- **Dois WebView2 na mesma janela nÃ£o se empilham** (airspace). SobreposiÃ§Ã£o sÃ³ funciona
  com janela top-level separada (`OverlayWindow`). Elemento WPF desenhado por cima de um
  WebView2 **some**.
- **Toda pÃ¡gina Ã© diagramada em 1920 CSS de largura** e sÃ³ o zoom muda. NÃ£o "conserte"
  isso para largura real: quebra a rolagem e desalinha controlador Ã— terminal.
- **Coordenadas do controle ao vivo sÃ£o normalizadas (0..1)** e viram pixels CSS no
  terminal dividindo pelo zoom. Errar isso faz a rolagem funcionar e o clique nÃ£o.
- **Live do YouTube sÃ³ toca dentro de um navegador.** VLC/yt-dlp levam 403 nos segmentos
  (proteÃ§Ã£o anti-bot). JÃ¡ foi tentado e revertido â€” nÃ£o tente de novo.
- **Captura GDI (miniaturas) nÃ£o enxerga vÃ­deo em overlay de hardware** â€” aparece preto.
  NÃ£o Ã© bug de captura; nÃ£o Ã© diagnÃ³stico vÃ¡lido para "o vÃ­deo nÃ£o estÃ¡ tocando".
- **O terminal roda como usuÃ¡rio comum em Arquivos de Programas**: nÃ£o pode se
  auto-substituir nem elevar. AtualizaÃ§Ã£o passa obrigatoriamente pela tarefa agendada SYSTEM.

## Estado atual

VersÃ£o **1.48.0**. Fases A (parede local) e D (rede, controle remoto, auto-update)
concluÃ­das. O que falta estÃ¡ no fim de [ARQUITETURA.md](ARQUITETURA.md#o-que-falta).
