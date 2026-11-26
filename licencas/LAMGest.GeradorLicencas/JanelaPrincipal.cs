using System.Globalization;
using LAMGestRestaurant.Licenciamento;
using Microsoft.Data.SqlClient;

namespace LAMGest.GeradorLicencas;

/// <summary>
/// Janela do gerador: clientes (um por NUIT) com a situação da cobrança, pagamentos (cada pagamento emite a licença até ao fim do
/// período pago), licenças emitidas, suspensão por falta de pagamento e histórico.
/// </summary>
public sealed class JanelaPrincipal : Form
{
    private readonly ArmazemLicencas armazem;
    private BaseLicencas base_;
    private readonly TextBox pesquisa = new() { PlaceholderText = "Pesquisar nome ou NUIT…", Dock = DockStyle.Top };
    private readonly ComboBox filtro = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ListView clientes = Lista(("Cliente", 175), ("NUIT", 92), ("Plano", 88), ("Válida até", 98), ("Situação", 290));
    private readonly Label resumo = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(71, 85, 105) };

    // Separador «Cliente»
    private readonly TextBox nome = new() { Width = 360, MaxLength = 150 };
    private readonly TextBox nuit = new() { Width = 140, MaxLength = 9 };
    private readonly TextBox contacto = new() { Width = 320, MaxLength = 200, PlaceholderText = "Telefone, e-mail…" };
    private readonly ComboBox plano = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TextBox valorMensal = new() { Width = 140, MaxLength = 20, PlaceholderText = "ex.: 2500,00" };
    private readonly TextBox observacoes = new() { Width = 320, MaxLength = 1000, Multiline = true, Height = 54 };
    private readonly Label situacao = new() { AutoSize = true, MaximumSize = new Size(520, 0), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Margin = new Padding(0, 4, 0, 8) };
    private readonly Button guardarCliente = Botao("Guardar cliente");
    private readonly Button novoCliente = Botao("Novo cliente");
    private readonly Button suspender = Botao("Suspender", activo: false);
    private readonly Button cancelar = Botao("Cancelar contrato", activo: false);

    // Separador «Pagamentos»
    private readonly ListView pagamentos = Lista(("Data", 90), ("Valor", 110), ("Meses", 55), ("Pago até", 90), ("Referência", 140), ("Estado", 160));
    private readonly Button registarPagamento = Botao("Registar pagamento…", activo: false);
    private readonly Button anularPagamento = Botao("Anular pagamento…", activo: false);

    // Separador «Licenças»
    private readonly ListView licencas = Lista(("Emitida", 90), ("Válida até", 90), ("Origem", 170), ("Licença", 250));
    private readonly TextBox texto = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 110, Dock = DockStyle.Bottom, Font = new Font("Consolas", 9f) };
    private readonly Button copiarMensagem = Botao("Copiar mensagem para o cliente", activo: false);
    private readonly Button copiar = Botao("Copiar só a licença", activo: false);
    private readonly Button guardar = Botao("Guardar ficheiro .lic…", activo: false);
    private readonly Button semPagamento = Botao("Emitir sem pagamento…", activo: false);
    private readonly Button desbloquear = Botao("Desbloquear relógio…", activo: false);

    private readonly ListView historico = Lista(("Data", 130), ("Acção", 140), ("Detalhe", 380), ("Utilizador", 140));
    private readonly TabControl separadores = new() { Dock = DockStyle.Fill };
    private readonly Label estado = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(71, 85, 105) };

    private ClienteResumo? actual;
    private LicencaRegisto? licencaMostrada;

    /// <summary>Botão do tamanho do texto (não corta o rótulo com outras escalas de ecrã).</summary>
    private static Button Botao(string texto, bool activo = true) =>
        new() { Text = texto, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4), MinimumSize = new Size(100, 34), Enabled = activo };

    public JanelaPrincipal(ArmazemLicencas armazem, BaseLicencas baseLicencas)
    {
        this.armazem = armazem;
        base_ = baseLicencas;
        Text = $"LAMGest — Licenças e cobrança ({baseLicencas.NomeBase})";
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(1420, 780);
        MinimumSize = new Size(1300, 700);
        StartPosition = FormStartPosition.CenterScreen;
        if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icone)
            Icon = icone;

        var menu = new MenuStrip();
        var menuBase = new ToolStripMenuItem("Base de dados");
        menuBase.DropDownItems.Add("Cópia de segurança da base…", null, (_, _) => CopiaBase());
        menuBase.DropDownItems.Add("Importar o registo antigo (registo.json)", null, (_, _) => ImportarRegisto());
        menuBase.DropDownItems.Add("Ligar a outra instância ou base…", null, (_, _) => MudarBase());
        var chave = new ToolStripMenuItem("Chave");
        chave.DropDownItems.Add("Ver chave pública", null, (_, _) => VerChavePublica());
        chave.DropDownItems.Add("Exportar cópia de segurança…", null, (_, _) => ExportarCopia());
        chave.DropDownItems.Add("Importar cópia de segurança…", null, (_, _) => ImportarCopia());
        menu.Items.AddRange([menuBase, chave, new ToolStripMenuItem("Abrir pasta de dados", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", armazem.Pasta))]);
        MainMenuStrip = menu;

        // Esquerda: clientes
        filtro.Items.AddRange(["Todos os clientes", "Em atraso", "A vencer (7 dias)", "Suspensos", "Sem licença", "Cancelados"]);
        filtro.SelectedIndex = 0;
        var esquerda = new Panel { Dock = DockStyle.Left, Width = 770, Padding = new Padding(12) };
        clientes.Dock = DockStyle.Fill;
        esquerda.Controls.AddRange([clientes, resumo, new Panel { Dock = DockStyle.Top, Height = 6 }, filtro, new Panel { Dock = DockStyle.Top, Height = 6 }, pesquisa,
            new Label { Text = "Clientes", Dock = DockStyle.Top, Height = 26, Font = new Font(Font, FontStyle.Bold) }]);
        clientes.BringToFront();

        // Direita: separadores
        separadores.TabPages.Add(PaginaCliente());
        separadores.TabPages.Add(PaginaPagamentos());
        separadores.TabPages.Add(PaginaLicencas());
        var paginaHistorico = new TabPage("Histórico") { Padding = new Padding(8) };
        historico.Dock = DockStyle.Fill;
        paginaHistorico.Controls.Add(historico);
        separadores.TabPages.Add(paginaHistorico);
        var direita = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 12, 0) };
        direita.Controls.AddRange([separadores, estado]);

        Controls.Add(direita);
        Controls.Add(esquerda);
        Controls.Add(menu);

        pesquisa.TextChanged += (_, _) => CarregarLista();
        filtro.SelectedIndexChanged += (_, _) => CarregarLista();
        clientes.SelectedIndexChanged += (_, _) => { if (clientes.SelectedItems.Count > 0 && clientes.SelectedItems[0].Tag is ClienteResumo c) Seleccionar(c.Id); };
        Limpar();
        CarregarLista();
    }

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);
    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string Data(DateOnly? d) => d is { } v ? Data(v) : "—";

    /// <summary>Colunas à largura do conteúdo (ou do título, se for maior): nada cortado com outras escalas de ecrã.</summary>
    private static void Ajustar(ListView l)
    {
        if (l.Items.Count == 0)
            return;
        for (var i = 0; i < l.Columns.Count; i++)
        {
            l.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
            var conteudo = l.Columns[i].Width;
            l.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
            l.Columns[i].Width = Math.Max(conteudo, l.Columns[i].Width) + 6;
        }
    }

    private static ListView Lista(params (string Titulo, int Largura)[] colunas)
    {
        var l = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true };
        foreach (var (titulo, largura) in colunas)
            l.Columns.Add(titulo, largura);
        return l;
    }

    private static string NomePlano(Plano p) => p switch
    {
        Plano.Mensal => "Mensal", Plano.Trimestral => "Trimestral", Plano.Semestral => "Semestral", _ => "Anual",
    };

    private TabPage PaginaCliente()
    {
        foreach (var p in Enum.GetValues<Plano>())
            plano.Items.Add(NomePlano(p));
        plano.SelectedIndex = 0;
        nuit.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar);

        var pagina = new TabPage("Cliente") { Padding = new Padding(12) };
        var tabela = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        tabela.Controls.Add(situacao);
        var campos = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        void Campo(string rotulo, Control c)
        {
            campos.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 10, 4) });
            campos.Controls.Add(c);
        }
        Campo("Nome comercial", nome);
        Campo("NUIT", nuit);
        Campo("Contacto", contacto);
        Campo("Plano", plano);
        Campo("Valor por mês (MT)", valorMensal);
        Campo("Observações", observacoes);
        tabela.Controls.Add(campos);
        var botoes = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        botoes.Controls.AddRange([guardarCliente, novoCliente, suspender, cancelar]);
        tabela.Controls.Add(botoes);
        tabela.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 14, 0, 0),
            Text = "Suspender: o gerador deixa de emitir licenças para este cliente. A licença que ele já tem vale até à data dela; " +
                   $"depois, ao fim de {RegrasCobranca.DiasToleranciaSistema} dias de tolerância, o sistema bloqueia as vendas e gravações " +
                   "(a consulta e os relatórios continuam). Não é possível retirar uma licença já emitida antes da data.",
        });
        pagina.Controls.Add(tabela);

        guardarCliente.Click += (_, _) => GuardarCliente();
        novoCliente.Click += (_, _) => Limpar();
        suspender.Click += (_, _) => Suspender();
        cancelar.Click += (_, _) => Cancelar();
        return pagina;
    }

    private TabPage PaginaPagamentos()
    {
        var pagina = new TabPage("Pagamentos") { Padding = new Padding(8) };
        pagamentos.Dock = DockStyle.Fill;
        var barra = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        barra.Controls.AddRange([registarPagamento, anularPagamento]);
        pagina.Controls.AddRange([pagamentos, barra]);
        registarPagamento.Click += (_, _) => RegistarPagamento();
        anularPagamento.Click += (_, _) => AnularPagamento();
        pagamentos.SelectedIndexChanged += (_, _) =>
            anularPagamento.Enabled = pagamentos.SelectedItems.Count > 0 && pagamentos.SelectedItems[0].Tag is PagamentoRegisto { Anulado: false };
        return pagina;
    }

    private TabPage PaginaLicencas()
    {
        var pagina = new TabPage("Licenças") { Padding = new Padding(8) };
        licencas.Dock = DockStyle.Fill;
        var barra = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        barra.Controls.AddRange([copiarMensagem, copiar, guardar, semPagamento, desbloquear]);
        pagina.Controls.AddRange([licencas, texto, barra]);
        licencas.SelectedIndexChanged += (_, _) =>
        {
            if (licencas.SelectedItems.Count > 0 && licencas.SelectedItems[0].Tag is LicencaRegisto l)
                MostrarLicenca(l);
        };
        copiarMensagem.Click += (_, _) =>
        {
            if (licencaMostrada is null || actual is null) return;
            Clipboard.SetText(RegrasCobranca.Mensagem(actual.Nome, licencaMostrada.ValidaAte, licencaMostrada.Texto));
            estado.Text = "Mensagem copiada: cole-a no WhatsApp ou no e-mail do cliente.";
        };
        copiar.Click += (_, _) => { if (licencaMostrada is not null) { Clipboard.SetText(licencaMostrada.Texto); estado.Text = "Licença copiada."; } };
        guardar.Click += (_, _) => GuardarFicheiro();
        semPagamento.Click += (_, _) => EmitirSemPagamento();
        desbloquear.Click += (_, _) => Desbloquear();
        return pagina;
    }

    /// <summary>Corre uma acção na base e mostra o erro (sem fechar o programa) se a base falhar.</summary>
    private bool Tentar(Action accao)
    {
        try
        {
            accao();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (SqlException ex)
        {
            MessageBox.Show(this, "Erro na base de dados: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }

    private static bool Passa(int indice, ClienteResumo c) => indice switch
    {
        1 => RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje) == Situacao.EmAtraso,
        2 => RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje) == Situacao.AVencer,
        3 => c.Estado == EstadoCliente.Suspenso,
        4 => RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje) == Situacao.SemLicenca,
        5 => c.Estado == EstadoCliente.Cancelado,
        _ => true,
    };

    private static Color Cor(Situacao s) => s switch
    {
        Situacao.EmAtraso => Color.Firebrick,
        Situacao.AVencer => Color.DarkOrange,
        Situacao.Suspenso or Situacao.Cancelado => Color.Gray,
        _ => SystemColors.WindowText,
    };

    private void CarregarLista()
    {
        IReadOnlyList<ClienteResumo> todos = [];
        if (!Tentar(() => todos = base_.Clientes()))
            return;
        var texto = pesquisa.Text.Trim();
        clientes.BeginUpdate();
        clientes.Items.Clear();
        foreach (var c in todos.Where(c => Passa(filtro.SelectedIndex, c) && (texto.Length == 0
                     || c.Nome.Contains(texto, StringComparison.CurrentCultureIgnoreCase) || c.Nuit.Contains(texto, StringComparison.Ordinal))))
        {
            var s = RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje);
            var item = new ListViewItem([c.Nome, c.Nuit, NomePlano(c.Plano), Data(c.ValidaAte), RegrasCobranca.Descrever(s, c.ValidaAte, Hoje)])
                { Tag = c, ForeColor = Cor(s) };
            clientes.Items.Add(item);
            if (actual?.Id == c.Id)
                item.Selected = true;
        }
        Ajustar(clientes);
        clientes.EndUpdate();
        var activos = todos.Where(c => c.Estado == EstadoCliente.Activo).ToList();
        int Conta(Situacao s) => activos.Count(c => RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje) == s);
        resumo.Text = $"{activos.Count} activos · {Conta(Situacao.EmAtraso)} em atraso · {Conta(Situacao.AVencer)} a vencer · " +
                      $"{todos.Count(c => c.Estado == EstadoCliente.Suspenso)} suspensos · receita mensal {RegrasCobranca.Valor(activos.Sum(c => c.ValorMensal))}";
    }

    /// <summary>Para a captura de verificação: selecciona o primeiro cliente da lista.</summary>
    internal void SeleccionarPrimeiro()
    {
        if (clientes.Items.Count > 0)
            clientes.Items[0].Selected = true;
    }

    internal void MostrarSeparador(int i) => separadores.SelectedIndex = i;

    private void Seleccionar(int id)
    {
        ClienteResumo? c = null;
        IReadOnlyList<PagamentoRegisto> ps = [];
        IReadOnlyList<LicencaRegisto> ls = [];
        IReadOnlyList<EventoHistorico> hs = [];
        if (!Tentar(() => { c = base_.Cliente(id); ps = base_.Pagamentos(id); ls = base_.Licencas(id); hs = base_.Historico(id); }) || c is null)
            return;
        actual = c;
        nome.Text = c.Nome;
        nuit.Text = c.Nuit;
        nuit.ReadOnly = true;
        contacto.Text = c.Contacto ?? "";
        plano.SelectedIndex = Array.IndexOf(Enum.GetValues<Plano>(), c.Plano);
        valorMensal.Text = c.ValorMensal.ToString("0.00", CultureInfo.GetCultureInfo("pt-PT"));
        observacoes.Text = c.Observacoes ?? "";
        var s = RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje);
        situacao.Text = $"{RegrasCobranca.Descrever(s, c.ValidaAte, Hoje)} · licença válida até {Data(c.ValidaAte)} · último pagamento {Data(c.UltimoPagamento)}";
        situacao.ForeColor = Cor(s);
        var activo = c.Estado == EstadoCliente.Activo;
        suspender.Enabled = c.Estado != EstadoCliente.Cancelado;
        suspender.Text = activo ? "Suspender" : "Reactivar";
        cancelar.Enabled = c.Estado != EstadoCliente.Cancelado;
        registarPagamento.Enabled = semPagamento.Enabled = activo;
        desbloquear.Enabled = true;

        pagamentos.Items.Clear();
        anularPagamento.Enabled = false;
        foreach (var p in ps)
            pagamentos.Items.Add(new ListViewItem([Data(p.Data), RegrasCobranca.Valor(p.Valor), p.Meses.ToString(CultureInfo.InvariantCulture), Data(p.PeriodoAte),
                p.Referencia ?? "", p.Anulado ? $"Anulado: {p.MotivoAnulacao}" : "Válido"]) { Tag = p, ForeColor = p.Anulado ? Color.Gray : SystemColors.WindowText });

        licencas.Items.Clear();
        texto.Text = "";
        licencaMostrada = null;
        copiar.Enabled = copiarMensagem.Enabled = guardar.Enabled = false;
        foreach (var l in ls)
        {
            var aceite = armazem.AceiteNoSistema(new LicencaEmitida(new DadosLicenca(l.LicencaId, c.Nome, c.Nuit, l.EmitidaEm, l.ValidaAte), l.Texto, l.EmitidaEmUtc, l.Observacoes));
            licencas.Items.Add(new ListViewItem([Data(l.EmitidaEm), Data(l.ValidaAte), l.PagamentoId is int p ? $"Pagamento {p}" : l.Observacoes ?? "Sem pagamento",
                l.LicencaId + (aceite ? "" : "  (inválida: outra chave)")]) { Tag = l, ForeColor = aceite ? SystemColors.WindowText : Color.Firebrick });
        }
        Ajustar(pagamentos);
        Ajustar(licencas);
        if (ls.Count > 0)
            MostrarLicenca(ls[0]);

        historico.Items.Clear();
        foreach (var h in hs)
            historico.Items.Add(new ListViewItem([h.DataUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), h.Accao, h.Detalhe, h.Utilizador]));
        Ajustar(historico);
        estado.Text = "";
    }

    private void MostrarLicenca(LicencaRegisto l)
    {
        licencaMostrada = l;
        texto.Text = l.Texto;
        copiar.Enabled = copiarMensagem.Enabled = guardar.Enabled = true;
    }

    private void Limpar()
    {
        actual = null;
        clientes.SelectedItems.Clear();
        nome.Text = nuit.Text = contacto.Text = observacoes.Text = valorMensal.Text = texto.Text = "";
        nuit.ReadOnly = false;
        plano.SelectedIndex = 0;
        situacao.Text = "Novo cliente: preencha os dados e carregue em «Guardar cliente».";
        situacao.ForeColor = SystemColors.ControlText;
        suspender.Enabled = cancelar.Enabled = registarPagamento.Enabled = anularPagamento.Enabled = semPagamento.Enabled = desbloquear.Enabled = false;
        copiar.Enabled = copiarMensagem.Enabled = guardar.Enabled = false;
        pagamentos.Items.Clear();
        licencas.Items.Clear();
        historico.Items.Clear();
        licencaMostrada = null;
        separadores.SelectedIndex = 0;
        estado.Text = "";
        nome.Focus();
    }

    private void GuardarCliente()
    {
        if (!RegrasCobranca.LerValor(valorMensal.Text, out var valor))
        {
            MessageBox.Show(this, "Indique o valor por mês (ex.: 2500 ou 2500,50).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var p = Enum.GetValues<Plano>()[plano.SelectedIndex];
        var id = actual?.Id;
        if (!Tentar(() =>
            {
                if (id is int existente)
                    base_.AlterarCliente(existente, nome.Text, contacto.Text, p, valor, observacoes.Text);
                else
                    id = base_.CriarCliente(nome.Text, nuit.Text, contacto.Text, p, valor, observacoes.Text);
            }))
            return;
        actual = new ClienteResumo(id!.Value, "", "", null, p, 0, EstadoCliente.Activo, null, null, null);
        CarregarLista();
        Seleccionar(id.Value);
        estado.Text = "Cliente guardado. Registe o pagamento para emitir a licença.";
    }

    private void Suspender()
    {
        if (actual is null)
            return;
        var novo = actual.Estado == EstadoCliente.Activo ? EstadoCliente.Suspenso : EstadoCliente.Activo;
        var motivo = Dialogos.Texto(this, novo == EstadoCliente.Suspenso ? $"Suspender «{actual.Nome}»" : $"Reactivar «{actual.Nome}»",
            novo == EstadoCliente.Suspenso
                ? "Motivo (ex.: falta de pagamento de Novembro). O gerador deixa de emitir licenças para este cliente."
                : "Motivo da reactivação (ex.: pagamento em atraso regularizado).");
        if (motivo is null || !Tentar(() => base_.MudarEstado(actual.Id, novo, motivo)))
            return;
        CarregarLista();
        Seleccionar(actual.Id);
        estado.Text = novo == EstadoCliente.Suspenso ? "Cliente suspenso." : "Cliente reactivado: já pode registar o pagamento.";
    }

    private void Cancelar()
    {
        if (actual is null)
            return;
        var motivo = Dialogos.Texto(this, $"Cancelar o contrato de «{actual.Nome}»", "Motivo do cancelamento (o cliente deixa de receber licenças; os registos ficam).");
        if (motivo is null || !Tentar(() => base_.MudarEstado(actual.Id, EstadoCliente.Cancelado, motivo)))
            return;
        CarregarLista();
        Seleccionar(actual.Id);
    }

    private void RegistarPagamento()
    {
        if (actual is null)
            return;
        var dados = Dialogos.Pagamento(this, actual);
        if (dados is null)
            return;
        var id = actual.Id;
        LicencaEmitida? licenca = null;
        PagamentoRegisto? pagamento = null;
        if (!Tentar(() => (pagamento, licenca) = base_.RegistarPagamento(armazem, id, dados.Value.Data, dados.Value.Valor, dados.Value.Meses, dados.Value.Referencia, Hoje)))
            return;
        CarregarLista();
        Seleccionar(id);
        if (licenca is null)
        {
            separadores.SelectedIndex = 1;
            estado.Text = $"Pagamento registado (período já terminado, até {Data(pagamento!.PeriodoAte)}): não foi emitida licença.";
            return;
        }
        separadores.SelectedIndex = 2;
        Clipboard.SetText(RegrasCobranca.Mensagem(licenca.Dados.Nome, licenca.Dados.ValidaAte, licenca.Texto));
        estado.Text = $"Pagamento registado; licença válida até {Data(licenca.Dados.ValidaAte)} emitida e mensagem copiada: cole-a no WhatsApp ou e-mail do cliente.";
    }

    private void AnularPagamento()
    {
        if (actual is null || pagamentos.SelectedItems.Count == 0 || pagamentos.SelectedItems[0].Tag is not PagamentoRegisto p)
            return;
        var motivo = Dialogos.Texto(this, "Anular pagamento",
            $"Anular o pagamento de {RegrasCobranca.Valor(p.Valor)} de {Data(p.Data)}? Fica registado como anulado.\n" +
            $"A licença já emitida com ele (até {Data(p.PeriodoAte)}) continua a valer no cliente até essa data.\n\nMotivo:");
        if (motivo is null || !Tentar(() => base_.AnularPagamento(p.Id, motivo)))
            return;
        Seleccionar(actual.Id);
        estado.Text = "Pagamento anulado.";
    }

    /// <summary>Código de desbloqueio do relógio (cliente que se enganou na data): copiado com as instruções.</summary>
    private void Desbloquear()
    {
        if (actual is null || Dialogos.Desbloqueio(this, actual) is not { } dados)
            return;
        var codigo = "";
        var id = actual.Id;
        if (!Tentar(() => codigo = base_.EmitirDesbloqueio(armazem, id, dados.Pedido, dados.Motivo, Hoje)))
            return;
        Seleccionar(id);
        Clipboard.SetText($"Código de desbloqueio do relógio do sistema LAMGest (pedido {dados.Pedido}, válido até " +
            $"{Data(Hoje.AddDays(ArmazemLicencas.DiasDesbloqueio))}).\nConfirme que a data e a hora do servidor estão certas e, no sistema, " +
            $"Configurações → Licença → «Desbloquear relógio» → cole o código abaixo.\n\n{codigo}");
        estado.Text = "Código de desbloqueio copiado: cole-o no WhatsApp ou no e-mail do cliente.";
    }

    private void EmitirSemPagamento()
    {
        if (actual is null)
            return;
        var dados = Dialogos.SemPagamento(this, actual);
        if (dados is null)
            return;
        LicencaEmitida? l = null;
        if (!Tentar(() => l = base_.EmitirSemPagamento(armazem, actual.Id, dados.Value.ValidaAte, dados.Value.Motivo, Hoje)))
            return;
        CarregarLista();
        Seleccionar(actual.Id);
        separadores.SelectedIndex = 2;
        Clipboard.SetText(RegrasCobranca.Mensagem(l!.Dados.Nome, l.Dados.ValidaAte, l.Texto));
        estado.Text = $"Licença válida até {Data(l.Dados.ValidaAte)} emitida e mensagem copiada.";
    }

    private void GuardarFicheiro()
    {
        if (licencaMostrada is null || actual is null)
            return;
        using var dialogo = new SaveFileDialog
        {
            Filter = "Licença LAMGest (*.lic)|*.lic",
            FileName = $"licenca-{actual.Nuit}-{licencaMostrada.ValidaAte:yyyyMMdd}.lic",
        };
        if (dialogo.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialogo.FileName, licencaMostrada.Texto);
            estado.Text = "Ficheiro guardado: " + dialogo.FileName;
        }
    }

    private void CopiaBase()
    {
        var escolha = MessageBox.Show(this, "Gravar a cópia na pasta de cópias do SQL Server?\n\nSim — pasta do SQL Server (recomendado).\n" +
            "Não — escolher outra pasta (o serviço do SQL Server tem de poder escrever nela).", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (escolha == DialogResult.Cancel)
            return;
        string? pasta = null;
        if (escolha == DialogResult.No)
        {
            using var d = new FolderBrowserDialog { Description = "Pasta para a cópia da base" };
            if (d.ShowDialog(this) != DialogResult.OK)
                return;
            pasta = d.SelectedPath;
        }
        string ficheiro = "";
        if (Tentar(() => ficheiro = base_.Copia(pasta)))
            MessageBox.Show(this, $"Cópia gravada em:\n{ficheiro}\n\nGuarde uma cópia fora deste computador (ex.: pen USB ou nuvem).", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ImportarRegisto()
    {
        var n = 0;
        if (Tentar(() => n = base_.Importar(armazem.Registo())))
        {
            CarregarLista();
            MessageBox.Show(this, n == 0 ? "Nada a importar: o registo antigo já está todo na base." : $"{n} licenças importadas do registo antigo.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void MudarBase()
    {
        if (Dialogos.Ligacao(this, LigacaoBase.Ler(armazem.Pasta)) is not { } nova)
            return;
        BaseLicencas? b = null;
        if (!Tentar(() => { b = new BaseLicencas(nova); b.Preparar(); }))
            return;
        nova.Gravar(armazem.Pasta);
        base_ = b!;
        Text = $"LAMGest — Licenças e cobrança ({b!.NomeBase})";
        Limpar();
        CarregarLista();
    }

    private void VerChavePublica() =>
        MessageBox.Show(this, "Chave pública (a que vai embutida no sistema LAMGest):\n\n" + armazem.ChavePublica(), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ExportarCopia()
    {
        var senha = Palavra.Pedir(this, "Palavra-passe da cópia (mín. 10 caracteres). Guarde-a em lugar seguro: sem ela a cópia não serve.", confirmar: true);
        if (senha is null)
            return;
        using var dialogo = new SaveFileDialog { Filter = "Cópia da chave (*.pem)|*.pem", FileName = "lamgest-chave-licencas.pem" };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        if (Tentar(() => armazem.ExportarCopia(dialogo.FileName, senha)))
            MessageBox.Show(this, "Cópia guardada. Guarde o ficheiro fora deste computador (ex.: pen USB num cofre).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ImportarCopia()
    {
        using var dialogo = new OpenFileDialog { Filter = "Cópia da chave (*.pem)|*.pem" };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        var senha = Palavra.Pedir(this, "Palavra-passe da cópia:", confirmar: false);
        if (senha is not null && Tentar(() => armazem.ImportarCopia(dialogo.FileName, senha)))
            MessageBox.Show(this, "Chave reposta.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

/// <summary>Pedido de palavra-passe (escondida).</summary>
internal static class Palavra
{
    public static string? Pedir(IWin32Window? dono, string mensagem, bool confirmar)
    {
        using var f = new Form { Text = "Palavra-passe", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, confirmar ? 190 : 150), Font = new Font("Segoe UI", 9.75f) };
        var texto = new Label { Text = mensagem, Location = new Point(12, 12), Size = new Size(396, 44) };
        var a = new TextBox { UseSystemPasswordChar = true, Location = new Point(12, 60), Width = 396 };
        var b = new TextBox { UseSystemPasswordChar = true, Location = new Point(12, 96), Width = 396, PlaceholderText = "Confirmar" };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(242, confirmar ? 146 : 106), Width = 80 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(328, confirmar ? 146 : 106), Width = 80 };
        f.Controls.AddRange(confirmar ? [texto, a, b, ok, cancelar] : [texto, a, ok, cancelar]);
        f.AcceptButton = ok;
        f.CancelButton = cancelar;
        while (f.ShowDialog(dono) == DialogResult.OK)
        {
            if (confirmar && a.Text != b.Text)
            {
                MessageBox.Show(f, "As palavras-passe não coincidem.", f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }
            return a.Text;
        }
        return null;
    }
}
