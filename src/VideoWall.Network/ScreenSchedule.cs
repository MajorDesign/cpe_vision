namespace VideoWall.Network
{
    /// <summary>
    /// Programação que a TELA executa sozinha: trocas por horário e rotação.
    ///
    /// Mora no terminal, não no controlador. Enquanto era um cronômetro dentro do
    /// painel, fechar o controlador parava a parede, a programação não ficava salva
    /// em lugar nenhum e um segundo controlador não tinha como saber dela. Com a
    /// tela executando, os três problemas somem: ela continua trocando sozinha,
    /// guarda tudo no próprio disco e qualquer controlador pode perguntar o que
    /// está programado — o mesmo princípio do layout ao vivo.
    ///
    /// Cada item leva as FONTES junto (não só o nome do layout): a tela não tem
    /// acesso aos arquivos de layout do controlador e precisa ser autossuficiente.
    /// </summary>
    public sealed class ScreenSchedule
    {
        /// <summary>Porta TCP em que a tela recebe e devolve sua programação.</summary>
        public const int Port = 48018;

        /// <summary>Pausa geral: mantém a programação, mas não dispara nada.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Trocas em horários fixos.</summary>
        public List<ScheduleSlot> Slots { get; set; } = new();

        /// <summary>Rotação automática entre layouts (nula = não configurada).</summary>
        public RotationPlan? Rotation { get; set; }

        /// <summary>Quem enviou e quando — ajuda a entender divergências em campo.</summary>
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>Nada programado?</summary>
        public bool IsEmpty =>
            (Slots == null || Slots.Count == 0) &&
            (Rotation == null || Rotation.Steps == null || Rotation.Steps.Count == 0);
    }

    /// <summary>Uma troca em horário fixo.</summary>
    public sealed class ScheduleSlot
    {
        public int Hour { get; set; }
        public int Minute { get; set; }

        /// <summary>Dias da semana (0 = domingo). Vazio = todos os dias.</summary>
        public List<int> Days { get; set; } = new();

        public bool Enabled { get; set; } = true;

        /// <summary>Nome do layout, só para exibição no painel.</summary>
        public string LayoutName { get; set; } = string.Empty;

        /// <summary>O conteúdo em si — é isto que a tela aplica.</summary>
        public List<ScreenSource> Sources { get; set; } = new();
    }

    /// <summary>Rotação: alterna os passos a cada X minutos.</summary>
    public sealed class RotationPlan
    {
        public bool Running { get; set; }

        /// <summary>Minutos entre trocas (mínimo 1).</summary>
        public int Minutes { get; set; } = 5;

        public List<RotationStep> Steps { get; set; } = new();
    }

    /// <summary>Um passo da rotação.</summary>
    public sealed class RotationStep
    {
        public string LayoutName { get; set; } = string.Empty;
        public List<ScreenSource> Sources { get; set; } = new();
    }
}
