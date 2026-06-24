# VideoWall CPE — Implantação e Atualização

## Estrutura
- `dist/Controlador/` — binários do painel central (framework-dependent).
- `dist/Terminal/` — binários do terminal burro (arquivo único + `update-config.json`).
- `instaladores/saida/` — **instaladores finais**:
  - `setup-controlador.exe` — instala o painel central.
  - `setup-terminal.exe` — instala o terminal no mini-PC.
- `instaladores/redist/` — runtime .NET 8 Desktop + WebView2 (embutidos nos setups).

Os instaladores **embutem o runtime .NET 8** (e o WebView2, no Controlador), então funcionam em **máquina limpa/formatada** — não é preciso instalar nada antes.

## Instalar
- **Central:** rode `setup-controlador.exe` no PC de controle.
- **Cada mini-PC (terminal):** rode `setup-terminal.exe`. Ele instala em
  `C:\Program Files\CPE\VideoWall Terminal` e **configura o início automático no login** (kiosk).

## Atualização pela internet (GitHub Releases) — atualizar de qualquer lugar

O terminal se atualiza sozinho a partir do **GitHub Releases** — você sobe a nova versão de **qualquer lugar**, e todos os terminais baixam e se atualizam (checam ao iniciar e a cada 30 min).

### Configurar uma vez
1. Crie um repositório no GitHub (público, para o binário ser baixável).
2. Edite `src/VideoWall.Viewer/update-config.json` com o seu repositório:
   ```json
   { "Owner": "sua-conta", "Repo": "seu-repo", "Asset": "VideoWall.Viewer.exe" }
   ```
3. Rode `publicar.bat` e instale os terminais com o `setup-terminal.exe` gerado.

### Lançar uma atualização (de onde estiver)
1. Suba o número da versão em `src/VideoWall.Viewer/VideoWall.Viewer.csproj`
   (`<Version>1.0.0</Version>` → `1.0.1`).
2. Rode `publicar.bat` (gera o novo `dist/Terminal/VideoWall.Viewer.exe`).
3. No GitHub, crie um **Release** com a tag igual à versão (ex.: `1.0.1`) e anexe o
   arquivo `dist/Terminal/VideoWall.Viewer.exe` como asset (nome `VideoWall.Viewer.exe`).
4. Pronto — em até 30 min cada terminal detecta, baixa (~0,2 MB) e se atualiza sozinho.
   Reinicia já na versão nova.

> Como o app é **framework-dependent**, cada atualização é minúscula (apenas o app),
> porque o runtime .NET já foi instalado pelo setup.
