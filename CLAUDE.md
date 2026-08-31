# CPE VideoWall — guia para quem (ou o que) for dar manutenção

Leia este arquivo por inteiro antes de mexer no código. Ele é curto de propósito;
o detalhe está em [ARQUITETURA.md](ARQUITETURA.md) (como as peças conversam e as
armadilhas já pagas) e em [IMPLANTACAO.md](IMPLANTACAO.md) (instalar e atualizar em campo).

## O que é

Controlador de videowall para a **CPE Tecnologia**, equivalente comercial ao QX2-Wall
Controller. O operador monta uma "parede" de fontes (páginas web, cor, texto, live do
YouTube) num painel central e a **projeta em TVs espalhadas pelo prédio**, cada uma
ligada a um mini-PC. Também controla essas páginas remotamente (mouse, teclado, login,
marcações), vê miniatura ao vivo de cada tela, agenda trocas de layout e mantém todos
os mini-PCs atualizados sozinho.

Cenário real em produção: **8 TVs**, cada uma com um mini-PC Dell, rede cabeada, tipicamente
mostrando um dashboard GIS pesado e uma live de câmera do YouTube.

## Vocabulário (use estes termos)

| Termo | Significa |
|---|---|
| **Controlador** / central | O painel de controle (`src/VideoWall`). Uma máquina só. |
| **Terminal** / tela | O mini-PC atrás da TV (`src/VideoWall.Viewer`). São 8. |
| **Parede** | O conjunto de fontes posicionadas que se projeta numa tela. |
| **Fonte** / elemento | Um item da parede: navegador, cor, texto, imagem, live. |
| **Célula** / vaga (slot) | O lugar de uma fonte dentro da tela do terminal. |
| **Layout** | Uma parede salva em JSON, com nome. |

## Estrutura

```
src/VideoWall/          Controlador — app WPF (MVVM), o único com interface de operação
src/VideoWall.Viewer/   Terminal — app WPF de tela cheia, "burro" (só obedece)
src/VideoWall.Network/  Biblioteca compartilhada: TODO o protocolo de rede
instaladores/           Scripts Inno Setup dos dois instaladores
publicar.bat            Publica os binários e gera os dois instaladores
publicar-release.ps1    Cria a release no GitHub e sobe os assets
```

`VideoWall.Network` é a fronteira: **se controlador e terminal precisam concordar sobre
algo, isso mora lá** (classes de protocolo + par cliente/servidor de cada canal).

Os arquivos grandes são [MainViewModel.cs](src/VideoWall/ViewModels/MainViewModel.cs) (~1900
linhas, todo o estado do controlador) e [MainWindow.xaml.cs do terminal](src/VideoWall.Viewer/MainWindow.xaml.cs)
(~960 linhas, renderização e todos os servidores). Comece por eles.

## Comandos

```powershell
# Compilar (o dotnet costuma estar fora do PATH nesta máquina)
& "C:\Program Files\dotnet\dotnet.exe" build VideoWall.sln -c Release

# Publicar binários + gerar os dois instaladores (precisa do Inno Setup 6)
.\publicar.bat

# Publicar a release no GitHub (é o que dispara o auto-update em campo)
$env:GITHUB_TOKEN = "ghp_..."   # escopo repo
.\publicar-release.ps1
```

Repositório: **MajorDesign/cpe_vision** (público). O `git push` usa a credencial do
Windows já em cache. Histórico é linear na `main`, um commit por versão.

## Regras do projeto (quebrar estas dá bug em campo)

1. **Toda alteração de código sobe a versão nos DOIS csproj**, sempre iguais:
   `src/VideoWall/VideoWall.csproj` e `src/VideoWall.Viewer/VideoWall.Viewer.csproj`.
   É esse número que o auto-update compara. Se você mexeu no terminal e não subiu a
   versão, a mudança **não chega às TVs**.
2. **Mudou o protocolo? Mude os dois lados.** Controlador e terminal podem estar em
   versões diferentes por até uma hora durante um rollout; campos novos precisam ser
   opcionais (o JSON é `System.Text.Json`, campo ausente = default).
3. **Os arquivos `.iss` são ASCII sem acento** e a versão deles é lida do executável
   publicado (`GetVersionNumbersString`) — não escreva versão à mão lá.
4. **Antes de `publicar.bat`, mate os processos e limpe `dist\`.** Executável travado
   por instância rodando faz a publicação falhar em silêncio e empacotar binário
   ANTIGO; o cache do WebView2 (`EBWebView`) em `dist\` incha o instalador em dezenas de MB.
5. **Nada de bloquear a thread de UI do terminal.** Ela renderiza 4+ navegadores e
   atende 7 servidores TCP; travar significa TV branca. Na `VideoWall.Network`, use
   `ConfigureAwait(false)`.
6. **Erro de rede não vira diálogo — mas também não vira silêncio.** O terminal fica
   ligado 24/7 sem ninguém por perto: engula a exceção **e tente de novo**. `catch {}`
   sem retentativa já deixou uma tela verde na lista e surda a tudo (ver "terminal
   zumbi" em [ARQUITETURA.md](ARQUITETURA.md#armadilhas-conhecidas)).

## Onde mexer para tarefas comuns

| Quero... | Vá em |
|---|---|
| Adicionar um tipo de fonte na parede | `Models/` (novo `WallElement`), `App.xaml` (DataTemplate), `MainViewModel.Add*`, `ScreenSource` + serialização, e o `ApplyLayout` do terminal |
| Mudar o que o terminal mostra | [VideoWall.Viewer/MainWindow.xaml.cs](src/VideoWall.Viewer/MainWindow.xaml.cs) → `ApplyLayout` / `ReconcileSlot` |
| Mudar botões/painéis do controlador | [Views/MainWindow.xaml](src/VideoWall/Views/MainWindow.xaml) + `MainViewModel` (comandos) |
| Novo comando remoto | `ScreenCommand` (constante), `CommandSender` no controlador, `ApplyCommand` no terminal |
| Novo canal de rede | Par `XServer`/`XClient` em `VideoWall.Network`, porta nova na faixa 4801x, e regra de firewall já é por programa (não precisa mexer) |
| Auto-update | [TerminalPackageService.cs](src/VideoWall/Services/TerminalPackageService.cs) (central), [TerminalUpdater.cs](src/VideoWall.Viewer/TerminalUpdater.cs) (terminal), [setup-terminal.iss](instaladores/setup-terminal.iss) (tarefa SYSTEM) |
| Persistência (layouts, favoritos, agenda) | `Services/LayoutService`, `FavoritesService`, `ScheduleService`, `SettingsService` — tudo JSON em `%LocalAppData%\VideoWall` |

## Armadilhas que já custaram caro

Estas são regras empíricas — leia o porquê em [ARQUITETURA.md](ARQUITETURA.md#armadilhas-conhecidas)
antes de "consertar" alguma delas:

- **Dois WebView2 na mesma janela não se empilham** (airspace). Sobreposição só funciona
  com janela top-level separada (`OverlayWindow`). Elemento WPF desenhado por cima de um
  WebView2 **some**.
- **Toda página é diagramada em 1920 CSS de largura** e só o zoom muda. Não "conserte"
  isso para largura real: quebra a rolagem e desalinha controlador × terminal.
- **Coordenadas do controle ao vivo são normalizadas (0..1)** e viram pixels CSS no
  terminal dividindo pelo zoom. Errar isso faz a rolagem funcionar e o clique não.
- **Live do YouTube só toca dentro de um navegador.** VLC/yt-dlp levam 403 nos segmentos
  (proteção anti-bot). Já foi tentado e revertido — não tente de novo.
- **Captura GDI (miniaturas) não enxerga vídeo em overlay de hardware** — aparece preto.
  Não é bug de captura; não é diagnóstico válido para "o vídeo não está tocando".
- **O terminal roda como usuário comum em Arquivos de Programas**: não pode se
  auto-substituir nem elevar. Atualização passa obrigatoriamente pela tarefa agendada SYSTEM.

## Estado atual

Versão **1.41.0**. Fases A (parede local) e D (rede, controle remoto, auto-update)
concluídas. O que falta está no fim de [ARQUITETURA.md](ARQUITETURA.md#o-que-falta).
