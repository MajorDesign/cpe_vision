using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Executa a programação DA PRÓPRIA TELA: trocas por horário e rotação.
    ///
    /// Antes isso era um cronômetro dentro do controlador — fechar o painel parava
    /// a parede. Aqui, a tela troca sozinha mesmo sem ninguém conectado, guarda a
    /// programação no próprio disco (volta depois de queda de energia ou
    /// atualização) e a devolve a qualquer controlador que perguntar.
    /// </summary>
    internal sealed class TerminalScheduler : IDisposable
    {
        /// <summary>Passo do relógio. Um minuto é a menor granularidade útil aqui.</summary>
        private static readonly TimeSpan Passo = TimeSpan.FromSeconds(20);

        private static readonly string Arquivo = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CPE Tecnologia", "VideoWall", "agenda.json");

        private readonly Action<IReadOnlyList<ScreenSource>> _aplicar;
        private readonly DispatcherTimer _relogio;
        private readonly object _trava = new();

        private ScreenSchedule _agenda = new();

        // Estado da rotação (memória): qual passo está no ar e quando troca.
        private int _passoAtual = -1;
        private DateTime _proximaTroca = DateTime.MinValue;

        // Evita repetir o mesmo horário dentro do mesmo minuto.
        private readonly Dictionary<string, DateTime> _ultimoDisparo = new();

        public TerminalScheduler(Action<IReadOnlyList<ScreenSource>> aplicar)
        {
            _aplicar = aplicar;
            _agenda = Carregar();

            _relogio = new DispatcherTimer { Interval = Passo };
            _relogio.Tick += (_, _) => Avaliar(DateTime.Now);
            _relogio.Start();
        }

        /// <summary>Programação atual — é o que o controlador lê.</summary>
        public ScreenSchedule Current
        {
            get { lock (_trava) return _agenda; }
        }

        /// <summary>Recebe uma programação nova do controlador e a grava.</summary>
        public void Replace(ScreenSchedule nova)
        {
            lock (_trava)
            {
                _agenda = nova;
                _ultimoDisparo.Clear();
                _passoAtual = -1;
                // Rotação recém-ligada começa já, sem esperar o primeiro intervalo.
                _proximaTroca = DateTime.MinValue;
            }

            Salvar(nova);
            Avaliar(DateTime.Now);
        }

        /// <summary>Decide o que deve estar no ar agora.</summary>
        private void Avaliar(DateTime agora)
        {
            ScreenSchedule agenda;
            lock (_trava) agenda = _agenda;

            if (agenda == null || !agenda.Enabled)
                return;

            try
            {
                if (DispararHorarios(agenda, agora))
                    return; // um horário acabou de trocar a parede: a rotação espera

                GirarRotacao(agenda, agora);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Falha ao executar a programação da tela", ex);
            }
        }

        /// <summary>Trocas em horário fixo. Devolve true se alguma disparou agora.</summary>
        private bool DispararHorarios(ScreenSchedule agenda, DateTime agora)
        {
            if (agenda.Slots == null)
                return false;

            foreach (var slot in agenda.Slots)
            {
                if (!slot.Enabled || slot.Sources == null || slot.Sources.Count == 0)
                    continue;
                if (slot.Hour != agora.Hour || slot.Minute != agora.Minute)
                    continue;
                if (slot.Days is { Count: > 0 } && !slot.Days.Contains((int)agora.DayOfWeek))
                    continue;

                // O relógio bate várias vezes dentro do mesmo minuto.
                string chave = $"{slot.Hour:00}:{slot.Minute:00}|{slot.LayoutName}";
                if (_ultimoDisparo.TryGetValue(chave, out var quando) &&
                    quando.Date == agora.Date && quando.Hour == agora.Hour && quando.Minute == agora.Minute)
                    continue;

                _ultimoDisparo[chave] = agora;
                _aplicar(slot.Sources);

                // A rotação recomeça a contar a partir do que o horário colocou no ar.
                _passoAtual = -1;
                _proximaTroca = agora + TimeSpan.FromMinutes(Math.Max(1, agenda.Rotation?.Minutes ?? 5));
                return true;
            }

            return false;
        }

        private void GirarRotacao(ScreenSchedule agenda, DateTime agora)
        {
            var plano = agenda.Rotation;
            if (plano == null || !plano.Running || plano.Steps == null || plano.Steps.Count == 0)
                return;

            if (agora < _proximaTroca)
                return;

            _passoAtual = (_passoAtual + 1) % plano.Steps.Count;
            var passo = plano.Steps[_passoAtual];

            if (passo.Sources is { Count: > 0 })
                _aplicar(passo.Sources);

            _proximaTroca = agora + TimeSpan.FromMinutes(Math.Max(1, plano.Minutes));
        }

        // ------------------------------------------------------------------ disco

        private static ScreenSchedule Carregar()
        {
            try
            {
                if (File.Exists(Arquivo))
                    return JsonSerializer.Deserialize<ScreenSchedule>(File.ReadAllText(Arquivo)) ?? new ScreenSchedule();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Programação da tela ilegível — começando vazia", ex);
            }
            return new ScreenSchedule();
        }

        private static void Salvar(ScreenSchedule agenda)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
                File.WriteAllText(Arquivo, JsonSerializer.Serialize(agenda,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Não foi possível gravar a programação da tela", ex);
            }
        }

        public void Dispose()
        {
            try { _relogio.Stop(); } catch { }
        }
    }
}
