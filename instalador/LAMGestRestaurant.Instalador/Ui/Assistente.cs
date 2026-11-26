using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Ui;

/// <summary>Assistente de instalação: Servidor (SQL Server, base de dados, serviço) ou Posto (ligação ao servidor e número do posto).</summary>
public sealed class Assistente : Form
{
    internal enum Etapa { BoasVindas, Sql, Aplicacao, PostoServidor, Administrador, ResumoServidor, Posto, ResumoPosto, Remover, Progresso }

    private static readonly Color Azul = Color.FromArgb(37, 99, 235), Verde = Color.FromArgb(21, 128, 61), Vermelho = Color.FromArgb(185, 28, 28);

    private readonly Label titulo = new() { AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };
    private readonly Label subtitulo = new() { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
    private readonly Panel conteudo = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 8), AutoScroll = true };
    private readonly Button voltar = new() { Text = "< Voltar", Width = 110, Height = 32 };
    private readonly Button seguinte = new() { Text = "Seguinte >", Width = 120, Height = 32 };
    private readonly Button cancelar = new() { Text = "Cancelar", Width = 110, Height = 32 };

    internal Etapa EtapaActual { get; private set; }
    private bool modoServidor = true, modoRemover, aTrabalhar, concluido;

    // Boas-vindas
    private readonly RadioButton optServidor = new() { Text = "Servidor central", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
    private readonly RadioButton optPosto = new() { Text = "Posto de trabalho (caixa, cozinha, garçom, gerência)", AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
    private readonly RadioButton optRemover = new() { Text = "Remover o LAMGest deste computador", AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };

    // Remoção
    private readonly TextBox listaRemover = new() { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, BackColor = Color.White, Font = new Font("Consolas", 9.5f), Size = new Size(735, 300) };
    private readonly CheckBox confirmarRemover = new() { AutoSize = false, Size = new Size(735, 44), CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft,
        Text = "Confirmo que quero retirar o LAMGest deste computador. A base de dados e as cópias de segurança ficam guardadas." };
    private PecasInstaladas? pecas;

    // Servidor — SQL Server
    private readonly ComboBox instancia = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 320 };
    private readonly RadioButton autWindows = new() { Text = "Windows (conta deste computador)", AutoSize = true, Checked = true };
    private readonly RadioButton autSql = new() { Text = "SQL Server (utilizador e palavra-passe)", AutoSize = true };
    private readonly TextBox codigoInstalacao = new() { UseSystemPasswordChar = true, Width = 220 };
    private bool desbloqueado;
    private int tentativasCodigo;
    private readonly TextBox utilizadorSql = new() { Text = "sa", Width = 220 };
    private readonly TextBox palavraPasseSql = new() { UseSystemPasswordChar = true, Width = 220 };
    private readonly TextBox baseDados = new() { Text = "LAMGestRestaurantDB", Width = 320 };
    private readonly Button testarSql = new() { Text = "Testar ligação", Width = 140, Height = 30 };
    private readonly Label resultadoSql = new() { AutoSize = true, MaximumSize = new Size(680, 0) };
    private readonly LinkLabel avisoSql = new() { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Color.FromArgb(185, 28, 28), Margin = new Padding(3, 0, 3, 8) };
    private readonly Button procurarSql = new() { Text = "Procurar de novo", Width = 150, Height = 30 };
    private readonly Label estadoSqlcmd = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private IReadOnlyList<InstanciaSql> instanciasSql = [];
    private EstadoBase? estadoBase;
    private ConfigSql? sqlVerificado;

    // Servidor — aplicação
    private readonly TextBox pastaInstalacao = new() { Text = @"C:\Program Files\LAMGestRestaurant", Width = 380 };
    private readonly Button procurarPasta = new() { Text = "Procurar…", Width = 100, Height = 28 };
    private readonly NumericUpDown porta = new() { Minimum = 1024, Maximum = 65535, Value = 8080, Width = 90 };
    private readonly CheckBox firewall = new() { Text = "Abrir a porta na firewall do Windows (só redes privadas e de domínio)", AutoSize = true, Checked = true };
    private readonly CheckBox backup = new() { Text = "Cópia de segurança diária da base de dados, às", AutoSize = true, Checked = true };
    private readonly DateTimePicker horaBackup = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80, Value = DateTime.Today.AddHours(23).AddMinutes(30) };
    private readonly TextBox copiaPara = new() { Width = 380, PlaceholderText = "Opcional: disco externo ou partilha (ex.: E:\\Backups\\LAMGest)" };
    private readonly CheckBox https = new() { Text = "Ligação cifrada (HTTPS) na porta", AutoSize = true, Checked = true };
    private readonly NumericUpDown portaHttps = new() { Minimum = 1024, Maximum = 65535, Value = Certificados.PortaHttpsPadrao, Width = 90 };
    private readonly CheckBox soHttps = new() { Text = "Só HTTPS (marque depois de actualizar todos os postos e tablets: o HTTP passa a reencaminhar)", AutoSize = true };

    // Servidor — posto deste computador (abre por localhost, sem IP)
    private readonly CheckBox postoNoServidor = new() { Text = "Este computador também é um posto (caixa, cozinha, gerência…)", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };

    // Servidor — administrador
    private readonly TextBox adminUtilizador = new() { Text = "admin", Width = 220 };
    private readonly TextBox adminNome = new() { Text = "Administrador", Width = 320 };
    private readonly TextBox adminPalavraPasse = new() { UseSystemPasswordChar = true, Width = 220 };
    private readonly TextBox adminConfirmacao = new() { UseSystemPasswordChar = true, Width = 220 };

    // Servidor — resumo
    private readonly TextBox resumo = new() { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, BackColor = Color.White, Font = new Font("Consolas", 9.5f) };
    // Sem AutoSize para o texto poder ocupar várias linhas.
    private readonly CheckBox confirmarMigracoes = new() { AutoSize = false, Size = new Size(735, 62), CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft };

    // Posto
    private readonly TextBox servidorPosto = new() { Width = 220, PlaceholderText = "ex.: 192.168.1.10" };
    private readonly NumericUpDown portaPosto = new() { Minimum = 1, Maximum = 65535, Value = 8080, Width = 90 };
    private readonly NumericUpDown numeroPosto = new() { Minimum = 1, Maximum = 999, Value = 1, Width = 90 };
    private readonly ComboBox paginaPosto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly CheckBox atalhoAmbiente = new() { Text = "Atalho no ambiente de trabalho", AutoSize = true, Checked = true };
    private readonly CheckBox abrirAoIniciar = new() { Text = "Abrir automaticamente ao iniciar o Windows", AutoSize = true };
    private readonly CheckBox ecraInteiro = new() { Text = "Ecrã inteiro (recomendado para o ecrã da cozinha)", AutoSize = true };
    private readonly CheckBox entradaTactil = new() { Text = "Ecrã de entrada táctil (tocar no nome e escrever o código)", AutoSize = true };
    private readonly CheckBox impressaoDirecta = new() { Text = "Imprimir recibos e talões directamente na impressora predefinida deste posto (sem janela)", AutoSize = true, Checked = true };
    private readonly CheckBox agenteImpressao = new() { Text = "Agente de impressão: imprimir talões e recibos nas impressoras deste computador", AutoSize = true, Checked = AgentePosto.Incluido, Enabled = AgentePosto.Incluido };
    private readonly TextBox agenteUtilizador = new() { Text = "admin", Width = 220 };
    private readonly TextBox agentePalavraPasse = new() { UseSystemPasswordChar = true, Width = 220 };
    private RegistoAgente? registoAgente;
    private readonly Button testarPosto = new() { Text = "Testar ligação e posto", Width = 190, Height = 30 };
    private readonly Label resultadoPosto = new() { AutoSize = true, MaximumSize = new Size(680, 0) };
    private PostoServidor? postoEncontrado;
    private HttpsServidor? httpsPosto;
    private readonly CheckBox usarHttpsPosto = new() { Text = "Ligação cifrada (HTTPS)", AutoSize = true, Enabled = false };
    private string? navegador;
    private bool usarPrograma;

    // Progresso
    private readonly TextBox registo = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = new Font("Consolas", 9.5f), Dock = DockStyle.Fill };
    private readonly ProgressBar barra = new() { Style = ProgressBarStyle.Marquee, Dock = DockStyle.Top, Height = 16, MarqueeAnimationSpeed = 30 };

    public Assistente()
    {
        Text = $"LAMGest Restaurant — Instalação ({Pacote.Versao})";
        if (InstalacaoPosto.AbrirIcone() is { } icone)
            using (icone) Icon = new Icon(icone);
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(800, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);
        AutoScaleMode = AutoScaleMode.Dpi;

        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White, Padding = new Padding(28, 14, 28, 0) };
        titulo.Location = new Point(28, 14);
        subtitulo.Location = new Point(30, 46);
        var marca = new Label { Text = "LG", AutoSize = false, Size = new Size(44, 44), TextAlign = ContentAlignment.MiddleCenter, BackColor = Azul, ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        marca.Location = new Point(ClientSize.Width - 28 - 44, 17);
        cabecalho.Controls.AddRange([titulo, subtitulo, marca]);
        var linhaTopo = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.White };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = 420, Padding = new Padding(0, 13, 20, 0) };
        botoes.Controls.AddRange([cancelar, seguinte, voltar]);
        rodape.Controls.Add(botoes);
        var linhaBaixo = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };

        // O controlo Fill é o primeiro a entrar: na ancoragem do WinForms fica com o espaço que sobra.
        Controls.AddRange([conteudo, linhaTopo, cabecalho, linhaBaixo, rodape]);
        AcceptButton = seguinte;

        voltar.Click += (_, _) => Voltar();
        seguinte.Click += async (_, _) => await SeguinteAsync();
        cancelar.Click += (_, _) => Close();
        // Exclusividade explícita: os pares de opções mudam de contentor quando as páginas são reconstruídas.
        autWindows.CheckedChanged += (_, _) => autSql.Checked = !autWindows.Checked;
        autSql.CheckedChanged += (_, _) =>
        {
            autWindows.Checked = !autSql.Checked;
            utilizadorSql.Enabled = palavraPasseSql.Enabled = autSql.Checked;
            sqlVerificado = null;
        };
        foreach (var opcao in new[] { optServidor, optPosto, optRemover })
            opcao.CheckedChanged += (_, _) =>
            {
                if (!opcao.Checked)
                    return;
                foreach (var outra in new[] { optServidor, optPosto, optRemover }.Where(o => o != opcao))
                    outra.Checked = false;
            };
        https.CheckedChanged += (_, _) => portaHttps.Enabled = soHttps.Enabled = https.Checked;
        LerHttpsExistente();
        foreach (var c in new Control[] { instancia, utilizadorSql, palavraPasseSql, baseDados })
            c.TextChanged += (_, _) => sqlVerificado = null;
        testarSql.Click += async (_, _) => await VerificarSqlAsync();
        testarPosto.Click += async (_, _) => await VerificarPostoAsync();
        servidorPosto.TextChanged += (_, _) => { postoEncontrado = null; httpsPosto = null; };
        numeroPosto.ValueChanged += (_, _) => postoEncontrado = null;
        agenteImpressao.CheckedChanged += (_, _) => agenteUtilizador.Enabled = agentePalavraPasse.Enabled = agenteImpressao.Checked;
        agenteUtilizador.Enabled = agentePalavraPasse.Enabled = agenteImpressao.Checked;
        backup.CheckedChanged += (_, _) => horaBackup.Enabled = copiaPara.Enabled = backup.Checked;
        procurarPasta.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pasta de instalação do LAMGest Restaurant", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK)
                pastaInstalacao.Text = Path.Combine(d.SelectedPath, "LAMGestRestaurant");
        };
        FormClosing += (_, e) =>
        {
            if (aTrabalhar)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Aguarde o fim da instalação.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        utilizadorSql.Enabled = palavraPasseSql.Enabled = false;
        ProcurarInstancias();
        procurarSql.Click += (_, _) => ProcurarInstancias();
        instancia.TextChanged += (_, _) => MostrarAvisoSql();
        avisoSql.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(Requisitos.EnderecoSqlExpress) { UseShellExecute = true });
        backup.CheckedChanged += (_, _) => MostrarEstadoSqlcmd();
        paginaPosto.Items.AddRange([.. PaginaInicial.Todas]);
        paginaPosto.SelectedIndex = 1;
        navegador = InstalacaoPosto.EncontrarNavegador();
        // Com o WebView2 (Windows 10/11 com o Edge) o posto abre no programa LAMGest, sem navegador; senão, no Chrome/Edge.
        usarPrograma = AgentePosto.Incluido && InstalacaoPosto.TemWebView2();

        IrPara(Etapa.BoasVindas);
    }

    // ---------------- Navegação ----------------

    internal void IrPara(Etapa etapa)
    {
        EtapaActual = etapa;
        conteudo.SuspendLayout();
        conteudo.Controls.Clear();
        var (t, s, pagina) = etapa switch
        {
            Etapa.BoasVindas => ("Bem-vindo", "Escolha o que vai instalar neste computador.", PaginaBoasVindas()),
            Etapa.Sql => ("Servidor — SQL Server", "Onde fica a base de dados do LAMGest.", PaginaSql()),
            Etapa.Aplicacao => ("Servidor — aplicação", "Pasta, porta de rede e cópias de segurança.", PaginaAplicacao()),
            Etapa.PostoServidor => ("Servidor — posto deste computador", "Usar este servidor também para vender, sem IP.", PaginaPostoServidor()),
            Etapa.Administrador => ("Servidor — primeiro administrador", "Conta para entrar no sistema pela primeira vez.", PaginaAdministrador()),
            Etapa.ResumoServidor => ("Servidor — pronto a instalar", "Reveja as opções antes de continuar.", PaginaResumoServidor()),
            Etapa.Posto => ("Posto de trabalho", "Ligação ao servidor e número deste posto.", PaginaPosto()),
            Etapa.ResumoPosto => ("Posto — pronto a instalar", "Reveja as opções antes de continuar.", PaginaResumoPosto()),
            Etapa.Remover => ("Remover o LAMGest", "O que foi encontrado neste computador.", PaginaRemover()),
            _ => (modoRemover ? "A remover…" : "A instalar…", modoRemover ? "Remoção" : modoServidor ? "Servidor central" : "Posto de trabalho", PaginaProgresso()),
        };
        titulo.Text = t;
        subtitulo.Text = s;
        conteudo.Controls.Add(pagina);
        conteudo.ResumeLayout();

        voltar.Visible = etapa is not (Etapa.BoasVindas or Etapa.Progresso);
        seguinte.Text = etapa switch
        {
            Etapa.ResumoServidor or Etapa.ResumoPosto => "Instalar",
            Etapa.Remover => pecas is { Nada: true } ? "Fechar" : "Remover",
            Etapa.Progresso => "Concluir",
            _ => "Seguinte >",
        };
        seguinte.Enabled = etapa != Etapa.Progresso || concluido;
        cancelar.Visible = etapa != Etapa.Progresso;
    }

    private void Voltar()
    {
        IrPara(EtapaActual switch
        {
            Etapa.Sql or Etapa.Posto or Etapa.Remover => Etapa.BoasVindas,
            Etapa.Aplicacao => Etapa.Sql,
            Etapa.PostoServidor => Etapa.Aplicacao,
            Etapa.Administrador => Etapa.PostoServidor,
            Etapa.ResumoServidor => estadoBase is { TemAdministrador: true } ? Etapa.PostoServidor : Etapa.Administrador,
            Etapa.ResumoPosto => Etapa.Posto,
            var e => e,
        });
    }

    private async Task SeguinteAsync()
    {
        if (aTrabalhar)
            return;
        switch (EtapaActual)
        {
            case Etapa.BoasVindas:
                if (!desbloqueado)
                {
                    if (!Motor.CodigoInstalacao.Valido(codigoInstalacao.Text))
                    {
                        codigoInstalacao.Clear();
                        if (++tentativasCodigo >= 5)
                        {
                            Aviso("Código de instalação errado. O instalador vai fechar.");
                            Close();
                            return;
                        }
                        Aviso("Código de instalação errado. Sem o código correcto o instalador não continua.");
                        return;
                    }
                    desbloqueado = true;
                }
                modoServidor = optServidor.Checked;
                modoRemover = optRemover.Checked;
                if (modoRemover)
                {
                    pecas = Desinstalacao.Procurar(LocaisRemocao.Padrao());
                    confirmarRemover.Checked = false;
                    IrPara(Etapa.Remover);
                    return;
                }
                if (modoServidor && !Pacote.TemAplicacao)
                {
                    Aviso("Este instalador não inclui a aplicação do servidor (só serve para postos). Gere o Setup completo com scripts\\criar-setup.ps1.");
                    return;
                }
                IrPara(modoServidor ? Etapa.Sql : Etapa.Posto);
                break;
            case Etapa.Sql:
                if (sqlVerificado is null && !await VerificarSqlAsync())
                    return;
                if (estadoBase is { Existe: true, Vazia: false, DoLamgest: false })
                {
                    Aviso($"A base «{baseDados.Text.Trim()}» já existe e não é do LAMGest. Escolha outro nome.");
                    return;
                }
                if (estadoBase is { Desconhecidas.Count: > 0 })
                {
                    Aviso("Esta base foi actualizada por uma versão mais recente do LAMGest. Use o instalador dessa versão.");
                    return;
                }
                IrPara(Etapa.Aplicacao);
                break;
            case Etapa.Aplicacao:
                if (ValidarAplicacao() is { } erroApp)
                {
                    Aviso(erroApp);
                    return;
                }
                IrPara(Etapa.PostoServidor);
                break;
            case Etapa.PostoServidor:
                if (postoNoServidor.Checked && !usarPrograma && navegador is null && MessageBox.Show(this,
                        "Este Windows não tem o Microsoft Edge WebView2 Runtime nem o Google Chrome: o posto é registado, mas os atalhos não são criados.\n\nInstale o Chrome e volte a correr o instalador. Continuar?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                IrPara(estadoBase is { TemAdministrador: true } ? Etapa.ResumoServidor : Etapa.Administrador);
                break;
            case Etapa.Administrador:
                if (ValidarAdministrador() is { } erroAdmin)
                {
                    Aviso(erroAdmin);
                    return;
                }
                IrPara(Etapa.ResumoServidor);
                break;
            case Etapa.ResumoServidor:
                if (estadoBase is { ActualizacaoComDados: true } && !confirmarMigracoes.Checked)
                {
                    Aviso("Confirme a actualização da base de dados (é feita uma cópia de segurança antes).");
                    return;
                }
                await InstalarServidorAsync();
                break;
            case Etapa.Posto:
                if (!await VerificarPostoAsync())
                    return;
                if (postoEncontrado is null && MessageBox.Show(this,
                        $"O posto {numeroPosto.Value} ainda não está registado no servidor.\n\nPode continuar e registá-lo depois em Caixa → Terminais (campo «N.º posto»). Continuar?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                if (!usarPrograma && navegador is null)
                {
                    Aviso("Este Windows não tem o Microsoft Edge WebView2 Runtime nem o Google Chrome. Instale um deles e volte a correr o instalador.");
                    return;
                }
                if (!usarPrograma && !InstalacaoPosto.EChrome(navegador) && MessageBox.Show(this,
                        "O Google Chrome não está instalado neste computador.\n\nPode instalar o Chrome e voltar a correr o instalador (recomendado), ou continuar com o Microsoft Edge. Continuar com o Edge?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                registoAgente = null;
                if (agenteImpressao.Checked && !await RegistarAgenteAsync())
                    return;
                IrPara(Etapa.ResumoPosto);
                break;
            case Etapa.ResumoPosto:
                await InstalarPostoAsync();
                break;
            case Etapa.Remover:
                if (pecas is null || pecas.Nada)
                {
                    Close();
                    return;
                }
                if (!confirmarRemover.Checked)
                {
                    Aviso("Confirme que quer retirar o LAMGest deste computador.");
                    return;
                }
                await RemoverAsync();
                break;
            case Etapa.Progresso:
                Close();
                break;
        }
    }

    // ---------------- Páginas ----------------

    private Control PaginaBoasVindas()
    {
        var servidor = Texto("Instala a aplicação como serviço do Windows, cria ou actualiza a base de dados no SQL Server, "
            + "o primeiro administrador, a regra da firewall e a cópia de segurança diária. Faça-o uma vez, no computador central.");
        var posto = Texto("Liga este computador ao servidor pelo endereço IP e associa-o a um número de posto. "
            + "Cria um atalho que abre o sistema já ligado ao posto. Não instala programas nem base de dados.");
        var remover = Texto("Retira o serviço, os atalhos, o agente de impressão e as tarefas do LAMGest. A base de dados e as cópias de segurança não são apagadas.");
        servidor.Margin = posto.Margin = remover.Margin = new Padding(24, 0, 0, 18);
        servidor.MaximumSize = posto.MaximumSize = remover.MaximumSize = new Size(700, 0);
        var aviso = Texto(Pacote.TemAplicacao ? $"Versão do pacote: {Pacote.Versao}" : "Este Setup não inclui a aplicação do servidor: só instala postos.", cinzento: true);
        return Coluna(
            Texto("Este assistente instala o LAMGest Restaurant ERP na rede local do restaurante."),
            Espaco(10), optServidor, servidor, optPosto, posto, optRemover, remover, aviso,
            Espaco(10), Grelha(("Código de instalação", codigoInstalacao)));
    }

    /// <summary>Lê de novo as instâncias do SQL Server deste computador (depois de instalar o SQL Server sem fechar o assistente).</summary>
    private void ProcurarInstancias()
    {
        instanciasSql = Requisitos.InstanciasComEstado();
        var actual = instancia.Text.Trim();
        instancia.Items.Clear();
        instancia.Items.AddRange([.. instanciasSql.Select(i => i.Nome)]);
        if (actual.Length == 0 || (instanciasSql.Count > 0 && instanciasSql.All(i => !string.Equals(i.Nome, actual, StringComparison.OrdinalIgnoreCase)) && actual == @".\SQLEXPRESS"))
            instancia.Text = instanciasSql.FirstOrDefault()?.Nome ?? @".\SQLEXPRESS";
        sqlVerificado = null;
        MostrarAvisoSql();
    }

    private void MostrarAvisoSql()
    {
        var aviso = Requisitos.AvisoSql(instanciasSql, instancia.Text);
        avisoSql.Text = aviso ?? "";
        avisoSql.Visible = aviso is not null;
        avisoSql.Links.Clear();
        if (aviso?.IndexOf(Requisitos.EnderecoSqlExpress, StringComparison.Ordinal) is int i and >= 0)
            avisoSql.Links.Add(i, Requisitos.EnderecoSqlExpress.Length);
    }

    private void MostrarEstadoSqlcmd()
    {
        var sqlcmd = Requisitos.ProcurarSqlcmd();
        var aviso = Requisitos.AvisoSqlcmd(backup.Checked, sqlcmd);
        estadoSqlcmd.Text = aviso ?? (sqlcmd is null ? "" : $"sqlcmd: {sqlcmd}");
        estadoSqlcmd.ForeColor = aviso is null ? Color.FromArgb(100, 116, 139) : Vermelho;
    }

    private Control PaginaSql() => Coluna(
        avisoSql,
        Texto("Indique a instância do SQL Server e uma conta com permissão de administrador (sysadmin). "
            + "Estas credenciais só são usadas durante a instalação: o serviço recebe uma conta própria, apenas com acesso a esta base."),
        Grelha(
            ("Instância", Linha(instancia, procurarSql)),
            ("Autenticação", Coluna(autWindows, autSql)),
            ("Utilizador", utilizadorSql),
            ("Palavra-passe", palavraPasseSql),
            ("Base de dados", baseDados)),
        Texto(@"Exemplos de instância: .\SQLEXPRESS, .\SYSON, SERVIDOR\SQLEXPRESS. Se a base não existir é criada; se existir, é actualizada (com cópia de segurança antes).", cinzento: true),
        Linha(testarSql), resultadoSql);

    private Control PaginaAplicacao() => Coluna(
        Grelha(
            ("Pasta de instalação", Linha(pastaInstalacao, procurarPasta)),
            ("Porta de rede", Linha(porta, Texto("HTTP: postos antigos e reencaminhamento", cinzento: true)))),
        Linha(https, portaHttps), soHttps,
        Texto("Com HTTPS as palavras-passe e os códigos passam cifrados na rede. O Setup cria uma autoridade de certificação própria deste servidor; "
            + "os postos instalados de novo passam a confiar nela e a usar HTTPS, e os tablets passam no próximo arranque da app.", cinzento: true),
        Espaco(6), firewall,
        Linha(backup, horaBackup),
        Grelha(("Cópia adicional para", copiaPara)),
        Texto("A cópia de segurança é guardada na pasta de backups da instância e verificada; ficam os últimos 14 dias. "
            + "Guarde também uma cópia fora deste computador (disco externo ou rede).", cinzento: true),
        EstadoSqlcmdActualizado());

    private Control EstadoSqlcmdActualizado()
    {
        MostrarEstadoSqlcmd();
        return estadoSqlcmd;
    }

    private Control PaginaPostoServidor()
    {
        postoNoServidor.CheckedChanged -= AoMudarPostoNoServidor;
        postoNoServidor.CheckedChanged += AoMudarPostoNoServidor;
        AoMudarPostoNoServidor(null, EventArgs.Empty);
        return Coluna(
            postoNoServidor,
            Texto("O posto deste computador abre o sistema por «localhost»: não precisa de IP nem de rede, e continua a funcionar se o "
                + "router mudar o IP do servidor. O IP só é preciso nos outros postos e nos tablets.", cinzento: true),
            Espaco(4),
            Grelha(
                ("Número do posto", numeroPosto),
                ("Abrir em", paginaPosto)),
            Texto("Se o número já existir no sistema (ex.: numa actualização), é esse posto que passa a ser o deste computador. Senão é criado «Posto N — Servidor».", cinzento: true),
            Espaco(4), atalhoAmbiente, abrirAoIniciar, ecraInteiro, entradaTactil, impressaoDirecta, agenteImpressao,
            Texto(AgentePosto.Incluido
                ? "O agente de impressão é registado pela base de dados: não pede a palavra-passe do administrador."
                : "Este Setup não inclui o agente de impressão (gere-o com scripts\\criar-setup.ps1).", cinzento: true),
            Texto(usarPrograma ? "O sistema abre no programa LAMGest (janela própria com o motor do Edge, WebView2), sem navegador."
                : navegador is null ? "Atenção: este Windows não tem o WebView2 nem o Google Chrome (os atalhos não são criados)."
                : InstalacaoPosto.EChrome(navegador) ? $"Navegador: Google Chrome ({navegador})"
                : $"Atenção: o Google Chrome não está instalado; será usado o Edge ({navegador}).", cinzento: true));
    }

    private void AoMudarPostoNoServidor(object? sender, EventArgs e)
    {
        numeroPosto.Enabled = paginaPosto.Enabled = atalhoAmbiente.Enabled = abrirAoIniciar.Enabled = ecraInteiro.Enabled
            = entradaTactil.Enabled = impressaoDirecta.Enabled = postoNoServidor.Checked;
        agenteImpressao.Enabled = postoNoServidor.Checked && AgentePosto.Incluido;
    }

    private Control PaginaAdministrador() => Coluna(
        Texto("A base de dados ainda não tem administrador. Esta conta permite entrar no sistema e criar os restantes utilizadores e postos."),
        Grelha(
            ("Utilizador", adminUtilizador),
            ("Nome completo", adminNome),
            ("Palavra-passe", adminPalavraPasse),
            ("Confirmar", adminConfirmacao)),
        Texto("Mínimo 8 caracteres, com letra minúscula e algarismo. A palavra-passe não é gravada em nenhum ficheiro.", cinzento: true));

    private Control PaginaResumoServidor()
    {
        resumo.Text = TextoResumoServidor();
        resumo.Size = new Size(735, estadoBase is { ActualizacaoComDados: true } ? 320 : 390);
        var blocos = new List<Control> { resumo };
        if (estadoBase is { ActualizacaoComDados: true } e)
        {
            confirmarMigracoes.Text = $"Confirmo a actualização da base «{baseDados.Text.Trim()}» ({e.Pedidos} pedidos registados) com {e.Pendentes.Count} alteração(ões) de estrutura. "
                + "Foi-me explicado que é feita uma cópia de segurança antes.";
            blocos.Add(Espaco(4));
            blocos.Add(confirmarMigracoes);
        }
        return Coluna([.. blocos]);
    }

    private Control PaginaPosto() => Coluna(
        Texto("Indique o endereço do servidor central (mostrado no fim da instalação do servidor) e o número deste posto. "
            + "O número tem de estar registado no sistema em Caixa → Terminais (campo «N.º posto»)."),
        Grelha(
            ("Servidor (IP)", servidorPosto),
            ("Porta", portaPosto),
            ("Número do posto", numeroPosto),
            ("Abrir em", paginaPosto)),
        usarHttpsPosto,
        Espaco(4), atalhoAmbiente, abrirAoIniciar, ecraInteiro, entradaTactil, impressaoDirecta,
        agenteImpressao,
        Grelha(
            ("Administrador LAMGest", agenteUtilizador),
            ("Palavra-passe", agentePalavraPasse)),
        Texto(AgentePosto.Incluido
            ? "O agente regista-se no servidor com a conta de um administrador (a palavra-passe não fica guardada). Depois, em Configurações → Impressoras, escolha «Ligada a: este posto» e a impressora."
            : "Este Setup não inclui o agente de impressão (gere-o com scripts\\criar-setup.ps1).", cinzento: true),
        Linha(testarPosto), resultadoPosto,
        Texto(usarPrograma ? "O sistema abre no programa LAMGest (janela própria com o motor do Edge, WebView2), sem navegador."
            : navegador is null ? "Atenção: este Windows não tem o WebView2 nem o Google Chrome."
            : InstalacaoPosto.EChrome(navegador) ? $"Navegador: Google Chrome ({navegador})"
            : $"Atenção: o Google Chrome não está instalado; será usado o Edge ({navegador}).", cinzento: true));

    private Control PaginaResumoPosto()
    {
        var o = OpcoesPostoActuais();
        resumo.Text = string.Join(Environment.NewLine,
            $"Servidor:        {o.Servidor}:{o.Porta}",
            $"Posto:           {o.Numero}" + (postoEncontrado is null ? "  (ainda não registado no servidor)" : $"  — {postoEncontrado.Nome}"),
            $"Abre em:         {o.Pagina.Rotulo}",
            $"Endereço:        {InstalacaoPosto.EnderecoPosto(o)}",
            $"Ligação:         {(o.Https is { } h ? "cifrada (HTTPS) — autoridade " + Certificados.Legivel(h.ImpressaoDigital) : "HTTP (sem cifra)")}",
            $"Entrada:         {(o.EntradaTactil ? "ecrã táctil (nome + código)" : "utilizador e palavra-passe")}",
            $"Impressão:       {(o.ImpressaoDirecta ? "directa na impressora predefinida deste posto" : "com a janela de impressão")}",
            $"Agente:          {(registoAgente is null ? "não instalar" : $"instalar (impressoras deste computador; posto registado: {registoAgente.Nome})")}",
            usarPrograma ? "Programa:        LAMGest (janela própria, sem navegador)" : $"Navegador:       {navegador}",
            "",
            "Atalhos:",
            "  • Menu Iniciar",
            o.AtalhoAmbienteTrabalho ? "  • Ambiente de trabalho (todos os utilizadores)" : "",
            o.AbrirAoIniciar ? "  • Arranque automático do Windows" : "",
            o.EcraInteiro ? "  • Ecrã inteiro (F11 para sair)" : "");
        resumo.Size = new Size(735, 390);
        return Coluna(resumo);
    }

    private Control PaginaRemover()
    {
        var p = pecas!;
        if (p.Nada)
        {
            listaRemover.Text = "Não encontrei nenhuma instalação do LAMGest neste computador.";
            return Coluna(listaRemover);
        }
        var linhas = new List<string>();
        if (p.Servidor)
        {
            linhas.Add("Servidor:");
            if (p.Servico) linhas.Add("  • serviço Windows «LAMGestRestaurant»");
            if (p.PastaServidor is { } pasta) linhas.Add("  • aplicação em " + pasta);
            if (p.TarefaBackup) linhas.Add($"  • tarefa agendada «{InstalacaoServidor.NomeTarefaBackup}»");
            foreach (var porta in p.Portas) linhas.Add($"  • regra da firewall da porta {porta}");
        }
        if (p.Posto)
        {
            linhas.Add("Posto:");
            foreach (var a in p.Atalhos) linhas.Add("  • atalho " + a);
            if (p.Agente) linhas.Add("  • agente de impressão");
            if (p.PastaPosto is { } pp) linhas.Add("  • programas em " + pp);
            foreach (var f in p.FicheirosPosto) linhas.Add("  • " + f);
        }
        if (p.Autoridade) linhas.Add("Autoridade local do HTTPS (raízes fidedignas deste computador)");
        linhas.Add("");
        linhas.Add("Fica guardado: a base de dados no SQL Server, as cópias de segurança e");
        linhas.Add($"a configuração, as chaves e os logs em {InstalacaoServidor.PastaDados}.");
        listaRemover.Text = string.Join(Environment.NewLine, linhas);
        return Coluna(listaRemover, Espaco(4), confirmarRemover);
    }

    private async Task RemoverAsync()
    {
        var p = pecas!;
        await ExecutarComProgressoAsync(progresso => Desinstalacao.RemoverAsync(p, LocaisRemocao.Padrao(), progresso));
    }

    /// <summary>Numa actualização, mantém as opções de HTTPS que o servidor já tinha (servidor.json).</summary>
    private void LerHttpsExistente()
    {
        try
        {
            var ficheiro = Path.Combine(InstalacaoServidor.PastaDados, "config", "servidor.json");
            if (!File.Exists(ficheiro))
                return;
            using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ficheiro));
            if (!d.RootElement.TryGetProperty("Seguranca", out var seg))
                return;
            if (seg.TryGetProperty("PortaHttps", out var ph) && ph.TryGetInt32(out var valor) && valor >= portaHttps.Minimum && valor <= portaHttps.Maximum)
                portaHttps.Value = valor;
            soHttps.Checked = seg.TryGetProperty("SoHttps", out var so) && so.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

    private Control PaginaProgresso()
    {
        var painel = new Panel { Dock = DockStyle.Fill };
        painel.Controls.Add(registo);
        painel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        painel.Controls.Add(barra);
        return painel;
    }

    // ---------------- Validações e verificações ----------------

    private ConfigSql ConfigSqlActual() => new(instancia.Text.Trim(), autWindows.Checked,
        autSql.Checked ? utilizadorSql.Text.Trim() : null, autSql.Checked ? palavraPasseSql.Text : null, baseDados.Text.Trim());

    private async Task<bool> VerificarSqlAsync()
    {
        var c = ConfigSqlActual();
        if (string.IsNullOrWhiteSpace(c.Instancia))
            return Falha(resultadoSql, "Indique a instância do SQL Server.");
        if (ServicoSql.ValidarNomeBase(c.BaseDados) is { } erroNome)
            return Falha(resultadoSql, erroNome);
        if (!c.AutenticacaoWindows && (string.IsNullOrEmpty(c.Utilizador) || string.IsNullOrEmpty(c.PalavraPasse)))
            return Falha(resultadoSql, "Indique o utilizador e a palavra-passe do SQL Server.");

        return await OcupadoAsync(resultadoSql, "A ligar ao SQL Server…", async () =>
        {
            InfoServidor info;
            try
            {
                info = await ServicoSql.TestarAsync(c);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is -1 or 2 or 53 or 26 or -2)
            {
                // Instância inexistente, parada ou inacessível: o erro do SQL Server é técnico demais para quem instala.
                return Falha(resultadoSql, $"Não consegui ligar a «{c.Instancia}». Confirme o nome da instância (lista acima) e que o serviço do SQL Server "
                    + "está em execução (services.msc). Se o SQL Server ainda não está instalado, instale o SQL Server Express e carregue em «Procurar de novo».");
            }
            if (Requisitos.ErroVersaoSql(info.Versao) is { } versaoInvalida)
                return Falha(resultadoSql, versaoInvalida);
            if (!info.Sysadmin)
                return Falha(resultadoSql, "Ligação feita, mas esta conta não é administradora (sysadmin) do SQL Server. Use outra conta (ex.: sa).");
            var estado = await ServicoSql.AnalisarAsync(c, Pacote.Migracoes);
            estadoBase = estado;
            sqlVerificado = c;
            var situacao = estado switch
            {
                { Existe: false } => "a base não existe e vai ser criada.",
                { Vazia: true, DoLamgest: false } => "a base existe e está vazia.",
                { DoLamgest: false } => "ATENÇÃO: a base existe e não é do LAMGest.",
                { Pendentes.Count: 0 } => $"base do LAMGest actualizada ({estado.Pedidos} pedidos).",
                _ => $"base do LAMGest com {estado.Pendentes.Count} actualização(ões) por aplicar ({estado.Pedidos} pedidos).",
            };
            var ok = estado.DoLamgest || estado.Vazia;
            Resultado(resultadoSql, $"{(ok ? "✔" : "✖")} {info.Nome} ({info.Edicao}) — {situacao}", ok);
            return ok;
        });
    }

    private string? ValidarAplicacao()
    {
        var pasta = pastaInstalacao.Text.Trim();
        if (!Path.IsPathFullyQualified(pasta))
            return "Indique uma pasta de instalação completa (ex.: C:\\Program Files\\LAMGestRestaurant).";
        if (Directory.Exists(pasta) && Directory.EnumerateFileSystemEntries(pasta).Any() && !File.Exists(Path.Combine(pasta, "LAMGestRestaurant.Api.exe")))
            return "A pasta escolhida não está vazia e não é uma instalação do LAMGest.";
        if (backup.Checked && copiaPara.Text.Trim() is { Length: > 0 } c && !Path.IsPathFullyQualified(c) && !c.StartsWith(@"\\", StringComparison.Ordinal))
            return "A pasta da cópia adicional tem de ser completa (ex.: E:\\Backups ou \\\\NAS\\Backups).";
        if (https.Checked && portaHttps.Value == porta.Value)
            return "A porta HTTPS tem de ser diferente da porta HTTP.";
        return Requisitos.AvisoEspaco(Requisitos.EspacoLivre(pasta))
               ?? Requisitos.AvisoPorta((int)porta.Value, Requisitos.PortaOcupada((int)porta.Value), Requisitos.LamgestInstalado())
               ?? (https.Checked ? Requisitos.AvisoPorta((int)portaHttps.Value, Requisitos.PortaOcupada((int)portaHttps.Value), Requisitos.LamgestInstalado()) : null)
               ?? Requisitos.AvisoSqlcmd(backup.Checked, Requisitos.ProcurarSqlcmd());
    }

    private string? ValidarAdministrador()
    {
        if (!Regex.IsMatch(adminUtilizador.Text.Trim(), @"^[A-Za-z0-9._@+-]{3,256}$"))
            return "O utilizador deve ter pelo menos 3 caracteres (letras, algarismos e . _ @ + -), sem espaços.";
        if (string.IsNullOrWhiteSpace(adminNome.Text))
            return "Indique o nome completo.";
        if (Segredos.ValidarPalavraPasseAdmin(adminPalavraPasse.Text) is { } erro)
            return erro;
        return adminPalavraPasse.Text != adminConfirmacao.Text ? "As palavras-passe não coincidem." : null;
    }

    private async Task<bool> VerificarPostoAsync()
    {
        var servidor = servidorPosto.Text.Trim();
        if (InstalacaoPosto.ValidarServidor(servidor) is { } erro)
            return Falha(resultadoPosto, erro);
        var p = (int)portaPosto.Value;
        var numero = (int)numeroPosto.Value;
        return await OcupadoAsync(resultadoPosto, $"A contactar {servidor}:{p}…", async () =>
        {
            var estado = await InstalacaoPosto.TestarServidorAsync(servidor, p);
            postoEncontrado = await InstalacaoPosto.ObterPostoAsync(servidor, p, numero);
            var linhaHttps = await VerificarHttpsPostoAsync(servidor);
            Resultado(resultadoPosto, (postoEncontrado is null
                ? $"✔ {estado}\n⚠ O posto {numero} não está registado (registe-o em Caixa → Terminais)."
                : $"✔ {estado}\n✔ Posto {numero}: {postoEncontrado.Nome}") + "\n" + linhaHttps, true);
            return true;
        });
    }

    /// <summary>
    /// HTTPS do servidor: lê a autoridade local (em HTTP) e confirma que o certificado serve para este endereço.
    /// Sem HTTPS no servidor (ou com o certificado sem este endereço), o posto fica em HTTP.
    /// </summary>
    private async Task<string> VerificarHttpsPostoAsync(string servidor)
    {
        httpsPosto = null;
        usarHttpsPosto.Enabled = usarHttpsPosto.Checked = false;
        HttpsServidor? h;
        try
        {
            h = await Certificados.LerDoServidorAsync(servidor, (int)portaPosto.Value);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return "⚠ Não foi possível confirmar o HTTPS do servidor: o posto fica em HTTP. " + ex.Message;
        }
        if (h is null)
            return "ℹ O servidor não tem ligação cifrada (HTTPS): o posto fica em HTTP.";
        try
        {
            using var http = Certificados.Cliente(h, TimeSpan.FromSeconds(6));
            using var r = await http.GetAsync(InstalacaoPosto.Endereco(servidor, h.Porta, "api/saude", https: true));
            r.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"⚠ O servidor tem HTTPS (porta {h.Porta}), mas o certificado não serve para «{servidor}» ou a porta está fechada. "
                + "Volte a correr o Setup no servidor ou use o IP mostrado no fim dessa instalação. O posto fica em HTTP.";
        }
        httpsPosto = h;
        usarHttpsPosto.Enabled = usarHttpsPosto.Checked = true;
        return $"✔ Ligação cifrada (HTTPS, porta {h.Porta}). Autoridade local: {Certificados.Legivel(h.ImpressaoDigital)}";
    }

    private HttpsServidor? HttpsEscolhido => usarHttpsPosto.Checked ? httpsPosto : null;

    /// <summary>Regista o agente no servidor com a conta de administrador indicada (a chave fica só em memória até instalar).</summary>
    private async Task<bool> RegistarAgenteAsync()
    {
        if (postoEncontrado is null)
            return Falha(resultadoPosto, "O agente de impressão precisa do posto registado no servidor (Caixa → Terminais, campo «N.º posto»). Registe-o ou desmarque o agente.");
        if (agenteUtilizador.Text.Trim().Length == 0 || agentePalavraPasse.Text.Length == 0)
            return Falha(resultadoPosto, "Indique o utilizador e a palavra-passe de um administrador do LAMGest para registar o agente de impressão.");
        var servidor = servidorPosto.Text.Trim();
        var p = (int)portaPosto.Value;
        var numero = (int)numeroPosto.Value;
        return await OcupadoAsync(resultadoPosto, "A registar o agente de impressão no servidor…", async () =>
        {
            registoAgente = await AgentePosto.RegistarAsync(servidor, p, numero, agenteUtilizador.Text, agentePalavraPasse.Text, https: HttpsEscolhido);
            agentePalavraPasse.Clear();
            Resultado(resultadoPosto, $"✔ Agente de impressão registado no posto {numero} ({registoAgente.Nome}).", true);
            return true;
        });
    }

    // ---------------- Instalação ----------------

    private OpcoesServidor OpcoesServidorActuais() => new(
        sqlVerificado!,
        pastaInstalacao.Text.Trim(),
        (int)porta.Value,
        firewall.Checked,
        backup.Checked,
        TimeOnly.FromDateTime(horaBackup.Value),
        backup.Checked && copiaPara.Text.Trim().Length > 0 ? copiaPara.Text.Trim() : null,
        estadoBase is { TemAdministrador: true } ? null : new NovoAdministrador(adminUtilizador.Text.Trim(), adminNome.Text.Trim(), adminPalavraPasse.Text),
        confirmarMigracoes.Checked,
        https.Checked ? (int)portaHttps.Value : null,
        https.Checked && soHttps.Checked,
        postoNoServidor.Checked
            ? new OpcoesPostoServidor((int)numeroPosto.Value, (PaginaInicial)paginaPosto.SelectedItem!, atalhoAmbiente.Checked, abrirAoIniciar.Checked,
                ecraInteiro.Checked, entradaTactil.Checked, impressaoDirecta.Checked, agenteImpressao.Checked && AgentePosto.Incluido)
            : null);

    private OpcoesPosto OpcoesPostoActuais() => new(
        servidorPosto.Text.Trim(), (int)portaPosto.Value, (int)numeroPosto.Value, (PaginaInicial)paginaPosto.SelectedItem!,
        atalhoAmbiente.Checked, abrirAoIniciar.Checked, ecraInteiro.Checked, entradaTactil.Checked, impressaoDirecta.Checked, HttpsEscolhido);

    private string TextoResumoServidor()
    {
        var o = OpcoesServidorActuais();
        var e = estadoBase!;
        var s = new StringBuilder();
        s.AppendLine($"SQL Server:      {o.Sql.Instancia} ({(o.Sql.AutenticacaoWindows ? "autenticação Windows" : "utilizador " + o.Sql.Utilizador)})");
        s.AppendLine($"Base de dados:   {o.Sql.BaseDados}");
        s.AppendLine("                 " + (e.Existe
            ? e.Pendentes.Count == 0 ? "existente e actualizada — não é alterada" : $"existente — {e.Pendentes.Count} actualização(ões): {string.Join(", ", e.Pendentes.Select(NomeMigracao))}"
            : "nova — vai ser criada"));
        if (e.Existe && e.DoLamgest)
            s.AppendLine("                 cópia de segurança antes de qualquer alteração");
        s.AppendLine($"Aplicação:       {o.PastaInstalacao}");
        s.AppendLine("Serviço:         LAMGestRestaurant (arranque automático)");
        s.AppendLine($"Conta:           {ServicoSql.ContaServico}");
        s.AppendLine($"Acesso à base:   {(o.Sql.AutenticacaoWindows ? "a conta do serviço" : "login SQL " + ServicoSql.LoginSqlServico)} (só leitura/escrita de dados)");
        s.AppendLine("Endereço:        " + (o.PortaHttps is int ph ? $"https://<IP deste servidor>:{ph} (cifrado)" : $"http://<IP deste servidor>:{o.Porta}"));
        if (o.PortaHttps is not null)
            s.AppendLine($"                 http://<IP deste servidor>:{o.Porta} " + (o.SoHttps ? "reencaminha para o HTTPS" : "continua a funcionar (postos antigos)"));
        s.AppendLine($"Firewall:        {(o.AbrirFirewall ? (o.PortaHttps is int f ? $"portas {o.Porta} e {f} abertas" : $"porta {o.Porta} aberta") + " só para a rede local (funciona sem internet)" : "não alterada")}");
        s.AppendLine($"Cópias:          {(o.BackupDiario ? $"diárias às {o.HoraBackup:HH\\:mm}" + (o.CopiaBackupPara is null ? "" : $", cópia em {o.CopiaBackupPara}") : "não agendadas")}");
        s.AppendLine($"Administrador:   {(o.Administrador is null ? "já existe — mantém-se" : o.Administrador.Utilizador + " (" + o.Administrador.NomeCompleto + ")")}");
        if (o.Posto is { } p)
        {
            s.AppendLine($"Este computador: posto n.º {p.Numero} (sem IP)");
            s.AppendLine($"                 {InstalacaoServidor.EnderecoPostoLocal(o, p)}");
            s.AppendLine($"                 abre em {p.Pagina.Rotulo}{(p.Agente ? "; agente de impressão" : "")}");
        }
        else
            s.AppendLine("Este computador: só servidor (sem posto)");
        s.AppendLine();
        s.AppendLine("Depois de instalar: entre no sistema, registe os outros postos em");
        s.AppendLine("Caixa → Terminais (N.º posto) e corra este instalador em cada");
        s.AppendLine("computador, na opção «Posto de trabalho», com o IP deste servidor.");
        return s.ToString();
    }

    private static string NomeMigracao(string id) => id.Contains('_') ? id[(id.IndexOf('_') + 1)..] : id;

    private async Task InstalarServidorAsync()
    {
        var opcoes = OpcoesServidorActuais();
        await ExecutarComProgressoAsync(async progresso =>
        {
            var instalacao = new InstalacaoServidor(opcoes, progresso);
            var enderecos = await instalacao.ExecutarAsync();
            progresso.Report("");
            if (instalacao.Certificado is { } c)
            {
                progresso.Report("Ligação cifrada (HTTPS). Ao instalar cada posto, confirme que a impressão digital mostrada é esta:");
                progresso.Report("   " + Certificados.Legivel(c.ImpressaoDigitalCa));
                progresso.Report("");
            }
            progresso.Report("Nos outros postos, use o instalador na opção «Posto de trabalho» com um destes endereços:");
            foreach (var e in enderecos)
                progresso.Report("   " + e);
            if (enderecos.Count == 0)
                progresso.Report($"   http://<IP do servidor>:{opcoes.Porta}");
        });
    }

    private async Task InstalarPostoAsync()
    {
        var opcoes = OpcoesPostoActuais();
        var agente = registoAgente;
        await ExecutarComProgressoAsync(progresso =>
        {
            var alvo = usarPrograma ? AgentePosto.CopiarPrograma(PastasAgente.Padrao(), progresso) : navegador!;
            InstalacaoPosto.Instalar(opcoes, PastasAtalhos.ParaTodosOsUtilizadores(), alvo, progresso);
            if (agente is not null)
            {
                var exe = AgentePosto.Instalar(opcoes.Servidor, opcoes.Porta, agente, PastasAgente.Padrao(), progresso, https: opcoes.Https);
                AgentePosto.Iniciar(exe, progresso);
                progresso.Report("Escolha as impressoras deste posto no LAMGest: Configurações → Impressoras → «Ligada a: " + agente.Nome + "».");
            }
            if (Configurador.Copiar(Configurador.PastaPosto(), progresso) is { } configurador)
                Configurador.CriarAtalho(configurador, PastasAtalhos.ParaTodosOsUtilizadores().MenuIniciar, progresso);
            progresso.Report("");
            progresso.Report("Posto instalado. Abra «LAMGest Restaurant» no ambiente de trabalho ou no menu Iniciar.");
            return Task.CompletedTask;
        });
    }

    private async Task ExecutarComProgressoAsync(Func<IProgress<string>, Task> trabalho)
    {
        registo.Clear();
        concluido = false;
        IrPara(Etapa.Progresso);
        barra.Style = ProgressBarStyle.Marquee;
        var progresso = new Progress<string>(linha => registo.AppendText(linha + Environment.NewLine));
        aTrabalhar = true;
        try
        {
            await Task.Run(() => trabalho(progresso));
            await Task.Delay(100); // deixa chegar as últimas linhas do Progress
            barra.Style = ProgressBarStyle.Continuous;
            barra.Value = 100;
            titulo.Text = modoRemover ? "Remoção concluída" : "Instalação concluída";
            concluido = true;
            IrParaFim(sucesso: true);
        }
        catch (Exception ex)
        {
            await Task.Delay(100);
            registo.AppendText(Environment.NewLine + "ERRO: " + ex.Message + Environment.NewLine);
            registo.AppendText("Corrija o problema e volte a tentar: o instalador pode ser executado de novo sem perder dados." + Environment.NewLine);
            barra.Style = ProgressBarStyle.Continuous;
            barra.Value = 0;
            titulo.Text = modoRemover ? "A remoção não terminou" : "A instalação não terminou";
            IrParaFim(sucesso: false);
        }
        finally
        {
            aTrabalhar = false;
        }
    }

    private void IrParaFim(bool sucesso)
    {
        seguinte.Enabled = true;
        seguinte.Text = sucesso ? "Concluir" : "Fechar";
        concluido = true;
        voltar.Visible = !sucesso;
        voltar.Text = "< Voltar";
        // Após falha, «Voltar» regressa ao resumo para corrigir e repetir.
        voltar.Click -= VoltarAoResumo;
        if (!sucesso)
            voltar.Click += VoltarAoResumo;
    }

    private void VoltarAoResumo(object? sender, EventArgs e)
    {
        voltar.Click -= VoltarAoResumo;
        concluido = false;
        IrPara(modoRemover ? Etapa.Remover : modoServidor ? Etapa.ResumoServidor : Etapa.ResumoPosto);
    }

    // ---------------- Auxiliares de interface ----------------

    private async Task<bool> OcupadoAsync(Label resultado, string mensagem, Func<Task<bool>> accao)
    {
        aTrabalhar = true;
        UseWaitCursor = true;
        seguinte.Enabled = voltar.Enabled = testarSql.Enabled = testarPosto.Enabled = false;
        Resultado(resultado, mensagem, null);
        try
        {
            return await accao();
        }
        catch (Exception ex)
        {
            return Falha(resultado, ex.Message);
        }
        finally
        {
            aTrabalhar = false;
            UseWaitCursor = false;
            seguinte.Enabled = voltar.Enabled = testarSql.Enabled = testarPosto.Enabled = true;
        }
    }

    private static bool Falha(Label l, string mensagem)
    {
        Resultado(l, "✖ " + mensagem, false);
        return false;
    }

    private static void Resultado(Label l, string texto, bool? ok)
    {
        l.Text = texto;
        l.ForeColor = ok switch { true => Verde, false => Vermelho, null => Color.FromArgb(71, 85, 105) };
    }

    private void Aviso(string mensagem) => MessageBox.Show(this, mensagem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static Label Texto(string texto, bool cinzento = false) => new()
    {
        Text = texto,
        AutoSize = true,
        MaximumSize = new Size(735, 0),
        ForeColor = cinzento ? Color.FromArgb(100, 116, 139) : Color.FromArgb(30, 41, 59),
        Margin = new Padding(0, 2, 0, 8),
    };

    private static Panel Espaco(int altura) => new() { Height = altura, Width = 1, Margin = Padding.Empty };

    private static FlowLayoutPanel Linha(params Control[] controlos)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        foreach (var c in controlos)
            c.Margin = new Padding(0, 0, 10, 0);
        f.Controls.AddRange(controlos);
        return f;
    }

    private static TableLayoutPanel Coluna(params Control[] controlos)
    {
        var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty };
        foreach (var c in controlos)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c);
        }
        return t;
    }

    private static TableLayoutPanel Grelha(params (string Rotulo, Control Controlo)[] linhas)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (rotulo, controlo) in linhas)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = rotulo, AutoSize = true, Margin = new Padding(0, 7, 8, 4) });
            controlo.Margin = new Padding(0, 3, 0, 3);
            t.Controls.Add(controlo);
        }
        return t;
    }

    // ---------------- Capturas (verificação visual sem instalar) ----------------

    /// <summary>Preenche o assistente com dados de exemplo e grava uma imagem de cada página. Não liga a nada nem instala nada.</summary>
    internal void Capturar(string pasta)
    {
        Directory.CreateDirectory(pasta);
        void Gravar(Etapa e, string nome, Action? preparar = null)
        {
            preparar?.Invoke();
            IrPara(e);
            Application.DoEvents();
            using var imagem = new Bitmap(Width, Height);
            DrawToBitmap(imagem, new Rectangle(Point.Empty, Size));
            imagem.Save(Path.Combine(pasta, nome + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        Gravar(Etapa.BoasVindas, "1-boas-vindas");
        Gravar(Etapa.Sql, "2-sql", () =>
        {
            autSql.Checked = true;
            palavraPasseSql.Text = "exemplo";
            Resultado(resultadoSql, "✔ SQL Server 16.0.1000.6 (Express Edition (64-bit)) — a base não existe e vai ser criada.", true);
        });
        estadoBase = new EstadoBase(false, true, false, [], ["20260101_Inicial"], [], false, 0);
        sqlVerificado = ConfigSqlActual();
        Gravar(Etapa.Aplicacao, "3-aplicacao");
        Gravar(Etapa.PostoServidor, "3b-posto-servidor");
        Gravar(Etapa.Administrador, "4-administrador");
        Gravar(Etapa.ResumoServidor, "5-resumo-servidor");
        estadoBase = new EstadoBase(true, false, true, ["20260101_Inicial"], ["20261103_NumeroPosto"], [], true, 152);
        Gravar(Etapa.ResumoServidor, "5b-resumo-actualizacao");
        modoServidor = false;
        Gravar(Etapa.Posto, "6-posto", () =>
        {
            servidorPosto.Text = "192.168.1.10";
            numeroPosto.Value = 3;
            Resultado(resultadoPosto, "✔ Servidor LAMGest a funcionar.\n✔ Posto 3: Caixa Bar\n✔ Ligação cifrada (HTTPS, porta 8443). Autoridade local: 3F2A 91C0 7DE4 0B5A 66C1 E2F9 0A47 B3D8", true);
            usarHttpsPosto.Enabled = usarHttpsPosto.Checked = true;
            postoEncontrado = new PostoServidor(3, 7, "Caixa Bar");
        });
        navegador ??= @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        Gravar(Etapa.ResumoPosto, "7-resumo-posto");
        modoRemover = true;
        pecas = new PecasInstaladas(true, @"C:\Program Files\LAMGestRestaurant", [8080, 8443], true,
            [@"C:\Users\Public\Desktop\LAMGest Restaurant.lnk", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\LAMGest Restaurant.lnk"],
            true, null, [@"C:\ProgramData\LAMGestRestaurant\posto.json"], true);
        Gravar(Etapa.Remover, "9-remover");
        modoRemover = false;
        Gravar(Etapa.Progresso, "8-progresso", () =>
        {
            registo.Text = "• A verificar o SQL Server e a base de dados…\r\n• A copiar a aplicação…\r\n   Aplicação em C:\\Program Files\\LAMGestRestaurant\r\n• A criar a base de dados…\r\n";
        });
    }
}
