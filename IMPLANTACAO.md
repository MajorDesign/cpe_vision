# VideoWall CPE — Implantação e Atualização

## Os dois instaladores

| Instalador | Onde roda | O que instala |
|---|---|---|
| `setup-controlador.exe` | **PC central** (1 máquina) | Painel de controle: parede virtual, telas na rede, controle ao vivo, agendador. Também é o **hub de atualização** das telas. |
| `setup-terminal.exe` | **Cada mini-PC atrás da TV** (8 telas) | Terminal em tela cheia, com **início automático no login** (quiosque) e auto-update 24/7. |

Ambos **embutem o runtime .NET 8 Desktop e o WebView2** — funcionam em máquina limpa,
sem instalar nada antes. Ambos criam a regra de firewall por programa (cobre a
descoberta UDP 48010 e todas as portas TCP de controle).

Gerados por `publicar.bat` em `instaladores/saida/`. A versão dos setups é lida
automaticamente do executável publicado (o `<Version>` do `.csproj`) — é ela que o
central compara para decidir se um terminal está desatualizado.

### Estrutura
- `dist/Controlador/` — binários do painel central (framework-dependent, pasta).
- `dist/Terminal/` — binário do terminal (arquivo único).
- `instaladores/redist/` — runtime .NET 8 Desktop + WebView2 (embutidos nos setups).

## Instalar

1. **Central:** rode `setup-controlador.exe` no PC de controle. Precisa de internet
   (é ele quem busca as atualizações no GitHub).
2. **Cada terminal:** rode `setup-terminal.exe` no mini-PC. Instala em
   `C:\Program Files\CPE\VideoWall Terminal`, configura o início automático e
   registra a tarefa agendada `CPE VideoWall Update`.

Os terminais **não precisam de internet** — só de rede local com o central,
na mesma sub-rede (Wi-Fi com *client isolation* bloqueia a descoberta).

## Auto-update 24/7 (central é o hub)

O terminal de quiosque fica ligado por semanas, então não basta verificar ao iniciar.
Cada terminal verifica **a cada 30 minutos** (com o app aberto) e também no pré-load:

1. O **central** consulta as releases do GitHub a cada 30 min e baixa o
   `setup-terminal.exe` novo **uma única vez**, para
   `%LocalAppData%\CPE Tecnologia\VideoWall\terminal-update`.
2. O central o serve na LAN em `http://<ip-do-central>:48020` (`/version` e `/setup`)
   e se anuncia por broadcast UDP para as telas o localizarem.
3. Cada **terminal** compara a versão servida com a sua. Sendo mais nova, baixa o
   instalador para `C:\ProgramData\CPE\VideoWall\update`. Quem instala é a tarefa
   agendada `CPE VideoWall Update`, que roda como **SYSTEM a cada 5 minutos**: se houver
   instalador na pasta, aplica em silêncio, **sem UAC** e sem ninguém na frente da TV;
   se não houver, sai na hora. O script `reabrir.cmd` reabre o terminal ao final.

   O terminal ainda tenta adiantar a instalação disparando a tarefa, mas **não depende
   disso** — rodando sem elevação, nem sempre ele consegue acionar uma tarefa do SYSTEM.
   O terminal **nunca** executa o instalador por conta própria: isso abriria o pedido de
   UAC na TV, onde não há ninguém para clicar.
4. O terminal restaura sozinho o layout que estava exibindo (`last-layout.json`),
   já logado nas páginas (a sessão fica na pasta do WebView2).

O primeiro ciclo de cada tela sai numa hora aleatória (3 a 30 min) para as 8 telas
não reiniciarem todas ao mesmo tempo.

**Reserva:** se o central estiver fora do ar, o terminal tenta o GitHub direto
(precisa de internet no terminal). Se o central estiver sem internet, dá para
alimentá-lo à mão: copie o `setup-terminal.exe` para uma pasta `terminal-update`
ao lado de `VideoWall.exe` na instalação do central, junto de um `version.txt` com o número da versão.

### Lançar uma atualização

1. Suba o `<Version>` **nos dois** csproj (`src/VideoWall/VideoWall.csproj` e
   `src/VideoWall.Viewer/VideoWall.Viewer.csproj`) — mantenha os dois iguais.
2. Rode `publicar.bat` (publica os binários e gera os dois instaladores).
3. Rode `publicar-release.ps1` com `$env:GITHUB_TOKEN` (escopo `repo`): cria a release
   `vX.Y.Z` em `MajorDesign/cpe_vision` e sobe os assets.
4. Pronto. O central pega em até 30 min (ou ao reabrir) e as 8 telas em até mais 30 min.

Para não esperar, o painel tem dois botões que reiniciam o terminal remotamente (ele
volta pelo pré-load, que é onde a atualização é buscada) — **sem AnyDesk, sem ir até o
mini-PC**:

- **🔄 Atualizar terminal** — só a tela selecionada.
- **🔄 Atualizar TODAS as telas** — a parede inteira, uma a cada 3 segundos (não apaga
  tudo de uma vez, e evita 8 downloads simultâneos).

Cada tela volta exibindo o mesmo layout e já logada: o terminal salva o que está no ar
antes de reiniciar e restaura ao abrir.

> Nada de reinstalar tela por tela: instala-se o terminal **uma vez**; depois é só
> publicar a release.

## Páginas que exigem login

O login **não viaja junto com a URL**. Cada navegador tem seu próprio perfil: o do
controlador fica no PC central, o de cada terminal fica no mini-PC dele. Por isso a
pré-visualização (duplo-clique na fonte) pode aparecer **logada** enquanto a TV mostra a
tela de login — o sistema redireciona para `/login` porque aquele navegador não tem sessão.

Como logar uma tela:

1. Selecione a tela e a célula da página.
2. **🔴 Controle ao vivo** — o mouse e o teclado vão para o navegador do terminal.
3. Digite usuário e senha ali e entre.
4. Feche a janela. **Não** clique em "Parar tela" (isso limpa a tela).

É uma vez por terminal: a sessão fica no perfil do WebView2 do terminal
(`%LocalAppData%`) e sobrevive a reinício, atualização e queda de energia — ao voltar,
o terminal restaura o layout com as URLs ao vivo e a página volta logada.

Depois de logar, **salve o layout de novo** para gravar a URL boa (ex.:
`/home/radar?pageId=591`). Se o layout tiver `/login` guardado, reprojetar joga a célula
de volta para a tela de login. Reprojetar a mesma URL é inofensivo: o terminal só
recarrega uma célula quando a URL projetada muda.

Se um dia a sessão passar a expirar com frequência, as saídas são levar os cookies do
central para as telas ou guardar as credenciais no terminal para auto-login — nenhuma
das duas está implementada.

## Portas usadas

| Porta | Direção | Uso |
|---|---|---|
| 48010/UDP | terminal → rede | Descoberta (anúncio das telas e do central) |
| 48011/TCP | central → terminal | Comandos (projetar layout, limpar, reiniciar) |
| 48013/TCP | central → terminal | Controle ao vivo (mouse/teclado/marcações) |
| 48014/TCP | central → terminal | Miniatura ao vivo da tela |
| 48015/TCP | central → terminal | Estado da célula (página + rolagem) |
| 48016/TCP | central → terminal | Espelho de vídeo do controle ao vivo |
| 48017/TCP | central → terminal | Layout atual da tela |
| 48020/TCP | terminal → central | **Atualização pela LAN** |

## Observações

- A partir da 1.39 o terminal se atualiza sem UAC. Terminais em versões anteriores
  precisam de **uma instalação manual da 1.39** (ou de internet no terminal, usando o
  botão 🔄); a partir daí a propagação é automática pela LAN.
- A pasta `C:\ProgramData\CPE\VideoWall\update` é gravável pelo usuário do quiosque
  (é onde o instalador baixado aguarda a tarefa SYSTEM). Em máquina dedicada de
  quiosque isso é aceitável; num PC compartilhado, restrinja quem faz logon local.
