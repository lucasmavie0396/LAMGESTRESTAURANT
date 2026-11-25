using System.Globalization;

namespace LAMGest.GeradorLicencas;

/// <summary>Janelas pequenas do gerador: motivo, pagamento, licença sem pagamento e ligação à base.</summary>
internal static class Dialogos
{
    private static Form Janela(string titulo, int largura, int altura) => new()
    {
        Text = titulo, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
        ClientSize = new Size(largura, altura), Font = new Font("Segoe UI", 9.75f), ShowInTaskbar = false,
    };

    private static (Button Ok, Button Cancelar) Botoes(Form f, int y)
    {
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(f.ClientSize.Width - 184, y), Width = 80, Height = 30 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(f.ClientSize.Width - 96, y), Width = 84, Height = 30 };
        f.Controls.AddRange([ok, cancelar]);
        f.AcceptButton = ok;
        f.CancelButton = cancelar;
        return (ok, cancelar);
    }

    private static Label Rotulo(string texto, int x, int y, int largura = 170) =>
        new() { Text = texto, Location = new Point(x, y + 4), Size = new Size(largura, 22) };

    /// <summary>Pede um texto obrigatório (motivo). Null = cancelado.</summary>
    public static string? Texto(IWin32Window dono, string titulo, string mensagem)
    {
        using var f = Janela(titulo, 520, 210);
        var m = new Label { Text = mensagem, Location = new Point(12, 12), Size = new Size(496, 90) };
        var t = new TextBox { Location = new Point(12, 110), Width = 496, MaxLength = 500 };
        f.Controls.AddRange([m, t]);
        Botoes(f, 160);
        while (f.ShowDialog(dono) == DialogResult.OK)
        {
            if (t.Text.Trim().Length >= 3)
                return t.Text.Trim();
            MessageBox.Show(f, "Indique o motivo (pelo menos 3 caracteres).", titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return null;
    }

    /// <summary>Pagamento: data, meses, valor (sugerido pelo valor por mês) e referência; mostra até quando fica a licença.</summary>
    public static (DateOnly Data, decimal Valor, int Meses, string? Referencia)? Pagamento(IWin32Window dono, ClienteResumo cliente)
    {
        using var f = Janela($"Pagamento de «{cliente.Nome}»", 520, 290);
        var data = new DateTimePicker { Location = new Point(190, 12), Width = 150, Format = DateTimePickerFormat.Short, MaxDate = DateTime.Today, Value = DateTime.Today };
        var meses = new NumericUpDown { Location = new Point(190, 48), Width = 80, Minimum = 1, Maximum = 36, Value = (int)cliente.Plano };
        var valor = new TextBox { Location = new Point(190, 84), Width = 150 };
        var referencia = new TextBox { Location = new Point(190, 120), Width = 310, MaxLength = 100, PlaceholderText = "M-Pesa, transferência, recibo…" };
        var previsao = new Label { Location = new Point(12, 162), Size = new Size(496, 60), Font = new Font("Segoe UI", 9.75f, FontStyle.Bold) };
        f.Controls.AddRange([Rotulo("Data do pagamento", 12, 12), data, Rotulo("Meses pagos", 12, 48), meses, Rotulo("Valor recebido (MT)", 12, 84), valor,
            Rotulo("Referência", 12, 120), referencia, previsao]);
        Botoes(f, 240);

        var valorEditado = false;
        string Sugerido() => RegrasCobranca.ValorSugerido(cliente.ValorMensal, (int)meses.Value).ToString("0.00", CultureInfo.GetCultureInfo("pt-PT"));
        valor.Text = Sugerido();
        valor.TextChanged += (_, _) => valorEditado = valor.Focused;
        void Actualizar()
        {
            if (!valorEditado)
                valor.Text = Sugerido();
            var ate = RegrasCobranca.NovaValidade(cliente.ValidaAte, DateOnly.FromDateTime(data.Value), (int)meses.Value);
            previsao.Text = $"A licença fica válida até {ate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" +
                            (cliente.ValidaAte is { } v ? $" (a actual vale até {v.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})." : ".");
        }
        meses.ValueChanged += (_, _) => Actualizar();
        data.ValueChanged += (_, _) => Actualizar();
        Actualizar();

        while (f.ShowDialog(dono) == DialogResult.OK)
        {
            if (RegrasCobranca.LerValor(valor.Text, out var v) && v > 0)
                return (DateOnly.FromDateTime(data.Value), v, (int)meses.Value, string.IsNullOrWhiteSpace(referencia.Text) ? null : referencia.Text.Trim());
            MessageBox.Show(f, "Indique o valor recebido (ex.: 2500 ou 2500,50).", f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return null;
    }

    /// <summary>Licença sem pagamento: validade e motivo (teste, oferta, contrato pago por fora).</summary>
    public static (DateOnly ValidaAte, string Motivo)? SemPagamento(IWin32Window dono, ClienteResumo cliente)
    {
        using var f = Janela($"Licença sem pagamento — «{cliente.Nome}»", 520, 200);
        var ate = new DateTimePicker { Location = new Point(190, 12), Width = 150, Format = DateTimePickerFormat.Short, MinDate = DateTime.Today.AddDays(1), Value = DateTime.Today.AddDays(30) };
        var motivo = new TextBox { Location = new Point(190, 48), Width = 310, MaxLength = 500, PlaceholderText = "ex.: período de teste" };
        var aviso = new Label { Location = new Point(12, 88), Size = new Size(496, 50), ForeColor = Color.FromArgb(71, 85, 105),
            Text = "Use só para testes, ofertas ou contratos pagos por fora: fica no histórico como licença sem pagamento." };
        f.Controls.AddRange([Rotulo("Válida até", 12, 12), ate, Rotulo("Motivo", 12, 48), motivo, aviso]);
        Botoes(f, 150);
        while (f.ShowDialog(dono) == DialogResult.OK)
        {
            if (motivo.Text.Trim().Length >= 3)
                return (DateOnly.FromDateTime(ate.Value), motivo.Text.Trim());
            MessageBox.Show(f, "Indique o motivo.", f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return null;
    }

    /// <summary>
    /// Instância do SQL Server (as deste computador aparecem na lista), autenticação do Windows ou utilizador do SQL Server (ex.: sa)
    /// e nome da base. Só fecha com OK depois de ligar e preparar a base; devolve a ligação já testada.
    /// </summary>
    public static LigacaoBase? Ligacao(IWin32Window? dono, LigacaoBase? actual, string? erroAnterior = null)
    {
        // Disposição automática (tabela): nada fica cortado com outras escalas de ecrã.
        using var f = new Form
        {
            Text = "Base de dados das licenças", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, Font = new Font("Segoe UI", 9.75f), ShowInTaskbar = dono is null,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12),
        };
        var detectadas = LigacaoBase.InstanciasLocais();
        var instancia = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDown };
        instancia.Items.AddRange([.. detectadas]);
        instancia.Text = actual?.Instancia ?? detectadas.FirstOrDefault() ?? @".\SQLEXPRESS";
        var cinzento = Color.FromArgb(71, 85, 105);
        var encontradas = new Label { AutoSize = true, ForeColor = cinzento, Margin = new Padding(3, 0, 3, 6),
            Text = detectadas.Count == 0 ? "Nenhuma instância encontrada neste computador: escreva o nome." : $"Encontradas neste computador: {string.Join(", ", detectadas)}" };
        var windows = new RadioButton { Text = "Autenticação do Windows (a sua conta)", AutoSize = true };
        var sql = new RadioButton { Text = "Utilizador do SQL Server (ex.: sa)", AutoSize = true };
        var utilizador = new TextBox { Width = 220, Text = actual?.Utilizador ?? "sa" };
        var senha = new TextBox { Width = 220, UseSystemPasswordChar = true, PlaceholderText = actual?.SenhaCifrada is null ? "" : "(guardada: vazio mantém)" };
        var nome = new TextBox { Width = 320, Text = actual?.Base ?? LigacaoBase.BasePadrao };
        var aviso = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 10, 3, 6), ForeColor = erroAnterior is null ? cinzento : Color.Firebrick,
            Text = erroAnterior ?? "A base é criada se não existir: é preciso permissão para criar bases (se a sua conta do Windows não a tiver, use o sa). " +
                   "A palavra-passe fica guardada cifrada pelo Windows, só para a sua conta. Nunca use a base de um sistema de cliente." };
        (actual?.Utilizador is null ? windows : sql).Checked = true;
        void Actualizar() => utilizador.Enabled = senha.Enabled = sql.Checked;
        windows.CheckedChanged += (_, _) => Actualizar();
        Actualizar();

        var tabela = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        void Linha(string? rotulo, Control c)
        {
            tabela.Controls.Add(rotulo is null ? new Label { AutoSize = true } : new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) });
            tabela.Controls.Add(c);
        }
        Linha("Instância do SQL Server", instancia);
        Linha(null, encontradas);
        Linha("Entrar com", windows);
        Linha(null, sql);
        Linha("Utilizador", utilizador);
        Linha("Palavra-passe", senha);
        Linha("Nome da base", nome);
        tabela.Controls.Add(aviso);
        tabela.SetColumnSpan(aviso, 2);
        var ok = new Button { Text = "Ligar", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 32) };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 32) };
        var botoes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
        botoes.Controls.AddRange([cancelar, ok]);
        tabela.Controls.Add(botoes);
        tabela.SetColumnSpan(botoes, 2);
        f.Controls.Add(tabela);
        f.AcceptButton = ok;
        f.CancelButton = cancelar;

        while (f.ShowDialog(dono) == DialogResult.OK)
        {
            try
            {
                BaseLicencas.ValidarNome(nome.Text);
                if (instancia.Text.Trim().Length == 0)
                    throw new ArgumentException("Indique a instância do SQL Server.");
                LigacaoBase escolha;
                if (windows.Checked)
                    escolha = new LigacaoBase(instancia.Text.Trim(), nome.Text.Trim());
                else if (utilizador.Text.Trim().Length == 0)
                    throw new ArgumentException("Indique o utilizador do SQL Server (ex.: sa).");
                else if (senha.Text.Length == 0 && actual?.SenhaCifrada is not null && string.Equals(actual.Utilizador, utilizador.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    escolha = new LigacaoBase(instancia.Text.Trim(), nome.Text.Trim(), actual.Utilizador, actual.SenhaCifrada);
                else
                    escolha = LigacaoBase.ComUtilizador(instancia.Text.Trim(), nome.Text.Trim(), utilizador.Text, senha.Text);
                f.Cursor = Cursors.WaitCursor;
                new BaseLicencas(escolha).Preparar();
                return escolha;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
            {
                aviso.ForeColor = Color.Firebrick;
                aviso.Text = BaseLicencas.ExplicarErro(ex);
            }
            finally
            {
                f.Cursor = Cursors.Default;
            }
        }
        return null;
    }
}
