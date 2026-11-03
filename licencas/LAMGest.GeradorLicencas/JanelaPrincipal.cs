using System.Globalization;
using LAMGestRestaurant.Licenciamento;

namespace LAMGest.GeradorLicencas;

/// <summary>Janela do gerador: estabelecimentos (um por NUIT), emissão e renovação de licenças.</summary>
public sealed class JanelaPrincipal : Form
{
    private readonly ArmazemLicencas armazem;
    private readonly TextBox pesquisa = new() { PlaceholderText = "Pesquisar nome ou NUIT…", Dock = DockStyle.Top };
    private readonly ListView estabelecimentos = Lista(("Nome comercial", 210), ("NUIT", 100), ("Válida até", 100), ("Estado", 140));
    private readonly ListView historico = Lista(("Emitida", 100), ("Válida até", 100), ("Licença", 340));
    private readonly TextBox nome = new() { Width = 360, MaxLength = 150 };
    private readonly TextBox nuit = new() { Width = 140, MaxLength = 9 };
    private readonly DateTimePicker validaAte = new() { Width = 140, Format = DateTimePickerFormat.Short };
    private readonly TextBox observacoes = new() { Width = 360, MaxLength = 500 };
    private readonly TextBox texto = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 110, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly Button gerar = Botao("Gerar licença");
    private readonly Button nova = Botao("Novo estabelecimento");
    private readonly Button renovar = Botao("Renovar (+1 ano)", activo: false);
    private readonly Button copiar = Botao("Copiar", activo: false);
    private readonly Button guardar = Botao("Guardar ficheiro .lic…", activo: false);

    /// <summary>Botão do tamanho do texto (não corta o rótulo com outras escalas de ecrã).</summary>
    private static Button Botao(string texto, bool activo = true) =>
        new() { Text = texto, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4), MinimumSize = new Size(100, 34), Enabled = activo };
    private readonly Label estado = new() { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(3, 8, 3, 3) };
    private LicencaEmitida? ultima;

    public JanelaPrincipal(ArmazemLicencas armazem)
    {
        this.armazem = armazem;
        Text = "LAMGest — Gerador de licenças";
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(1080, 640);
        StartPosition = FormStartPosition.CenterScreen;
        if (Icon.ExtractAssociatedIcon(Environment.ProcessPath!) is { } icone)
            Icon = icone;

        var menu = new MenuStrip();
        var chave = new ToolStripMenuItem("Chave");
        chave.DropDownItems.Add("Ver chave pública", null, (_, _) => VerChavePublica());
        chave.DropDownItems.Add("Exportar cópia de segurança…", null, (_, _) => ExportarCopia());
        chave.DropDownItems.Add("Importar cópia de segurança…", null, (_, _) => ImportarCopia());
        menu.Items.Add(chave);
        menu.Items.Add(new ToolStripMenuItem("Abrir pasta de dados", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", armazem.Pasta)));
        MainMenuStrip = menu;

        var esquerda = new Panel { Dock = DockStyle.Left, Width = 590, Padding = new Padding(12) };
        var tituloLista = new Label { Text = "Estabelecimentos", Dock = DockStyle.Top, Height = 26, Font = new Font(Font, FontStyle.Bold) };
        estabelecimentos.Dock = DockStyle.Fill;
        var tituloHistorico = new Label { Text = "Licenças emitidas para o estabelecimento seleccionado", Dock = DockStyle.Bottom, Height = 26, Font = new Font(Font, FontStyle.Bold) };
        historico.Dock = DockStyle.Bottom;
        historico.Height = 170;
        esquerda.Controls.AddRange([estabelecimentos, pesquisa, tituloLista, tituloHistorico, historico]);
        // Ordem de encaixe: o Fill é o último a ser disposto.
        estabelecimentos.BringToFront();

        var direita = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        // Uma coluna com a largura disponível (sem isto, cresce com o conteúdo e sai da janela).
        direita.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        direita.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        direita.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        direita.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        direita.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        direita.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        direita.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        direita.Controls.Add(new Label { Text = "Dados do estabelecimento", AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) });
        var campos = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        void Campo(string rotulo, Control c)
        {
            campos.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 10, 4) });
            campos.Controls.Add(c);
        }
        Campo("Nome comercial", nome);
        Campo("NUIT", nuit);
        Campo("Válida até", validaAte);
        Campo("Observações", observacoes);
        direita.Controls.Add(campos);
        var botoes = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 10) };
        botoes.Controls.AddRange([gerar, renovar, nova]);
        direita.Controls.Add(botoes);
        direita.Controls.Add(new Label { Text = "Licença — copie e envie ao cliente (activa-se no sistema em Configurações → Licença):", AutoSize = true,
            MaximumSize = new Size(480, 0), Margin = new Padding(0, 6, 0, 4) });
        direita.Controls.Add(texto);
        var accoes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        accoes.Controls.AddRange([copiar, guardar, estado]);
        direita.Controls.Add(accoes);

        Controls.Add(direita);
        Controls.Add(esquerda);
        Controls.Add(menu);

        nuit.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar);
        pesquisa.TextChanged += (_, _) => CarregarLista();
        estabelecimentos.SelectedIndexChanged += (_, _) => Seleccionar();
        gerar.Click += (_, _) => Emitir(validaAte.Value);
        renovar.Click += (_, _) => Renovar();
        nova.Click += (_, _) => Limpar();
        copiar.Click += (_, _) => { if (ultima is not null) { Clipboard.SetText(ultima.Texto); estado.Text = "Licença copiada."; } };
        guardar.Click += (_, _) => GuardarFicheiro();

        Limpar();
        CarregarLista();
    }

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Para a captura de verificação: selecciona o primeiro estabelecimento da lista.</summary>
    internal void SeleccionarPrimeiro()
    {
        if (estabelecimentos.Items.Count > 0)
            estabelecimentos.Items[0].Selected = true;
    }

    private static ListView Lista(params (string Titulo, int Largura)[] colunas)
    {
        var l = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true };
        foreach (var (titulo, largura) in colunas)
            l.Columns.Add(titulo, largura);
        return l;
    }

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Situacao(DateOnly validaAte)
    {
        var dias = validaAte.DayNumber - Hoje.DayNumber;
        return dias < 0 ? "Expirada" : dias <= 30 ? $"Expira em {dias} dias" : "Válida";
    }

    private void CarregarLista()
    {
        var filtro = pesquisa.Text.Trim();
        estabelecimentos.BeginUpdate();
        estabelecimentos.Items.Clear();
        foreach (var l in armazem.Estabelecimentos().Where(l => filtro.Length == 0
                     || l.Dados.Nome.Contains(filtro, StringComparison.CurrentCultureIgnoreCase) || l.Dados.Nuit.Contains(filtro, StringComparison.Ordinal)))
        {
            var item = new ListViewItem([l.Dados.Nome, l.Dados.Nuit, Data(l.Dados.ValidaAte), Situacao(l.Dados.ValidaAte)]) { Tag = l };
            if (l.Dados.ValidaAte < Hoje) item.ForeColor = Color.Firebrick;
            estabelecimentos.Items.Add(item);
        }
        estabelecimentos.EndUpdate();
    }

    private void Seleccionar()
    {
        if (estabelecimentos.SelectedItems.Count == 0 || estabelecimentos.SelectedItems[0].Tag is not LicencaEmitida l)
            return;
        nome.Text = l.Dados.Nome;
        nuit.Text = l.Dados.Nuit;
        validaAte.Value = ArmazemLicencas.ValidadeRenovacao(l.Dados.ValidaAte, Hoje).ToDateTime(TimeOnly.MinValue);
        observacoes.Text = "";
        renovar.Enabled = true;
        Mostrar(l);
        historico.Items.Clear();
        foreach (var h in armazem.Registo().Where(x => x.Dados.Nuit == l.Dados.Nuit).OrderByDescending(x => x.EmitidaEmUtc))
            historico.Items.Add(new ListViewItem([Data(h.Dados.EmitidaEm), Data(h.Dados.ValidaAte), h.Dados.Id.ToString()]));
    }

    private void Limpar()
    {
        estabelecimentos.SelectedItems.Clear();
        nome.Text = nuit.Text = observacoes.Text = texto.Text = "";
        validaAte.Value = DateTime.Today.AddYears(1);
        renovar.Enabled = copiar.Enabled = guardar.Enabled = false;
        historico.Items.Clear();
        ultima = null;
        estado.Text = "";
        nome.Focus();
    }

    private void Mostrar(LicencaEmitida l)
    {
        ultima = l;
        texto.Text = l.Texto;
        copiar.Enabled = guardar.Enabled = true;
        estado.Text = $"Licença de {l.Dados.Nome} (NUIT {l.Dados.Nuit}) válida até {Data(l.Dados.ValidaAte)}.";
    }

    private void Renovar()
    {
        if (estabelecimentos.SelectedItems.Count == 0 || estabelecimentos.SelectedItems[0].Tag is not LicencaEmitida l)
            return;
        Emitir(ArmazemLicencas.ValidadeRenovacao(l.Dados.ValidaAte, Hoje).ToDateTime(TimeOnly.MinValue));
    }

    private void Emitir(DateTime ate)
    {
        var existente = armazem.Estabelecimentos().FirstOrDefault(l => l.Dados.Nuit == nuit.Text.Trim());
        if (existente is not null && !string.Equals(existente.Dados.Nome, nome.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && MessageBox.Show(this, $"O NUIT {existente.Dados.Nuit} já pertence a «{existente.Dados.Nome}».\n\nEmitir a licença com o nome «{nome.Text.Trim()}»?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            var l = armazem.Emitir(nome.Text, nuit.Text, DateOnly.FromDateTime(ate), Hoje, observacoes.Text);
            CarregarLista();
            foreach (ListViewItem item in estabelecimentos.Items)
                if (item.Tag is LicencaEmitida e && e.Dados.Nuit == l.Dados.Nuit)
                    item.Selected = true;
            Mostrar(l);
            estado.Text = $"Licença gerada para {l.Dados.Nome}, válida até {Data(l.Dados.ValidaAte)}. Copie-a ou guarde o ficheiro.";
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void GuardarFicheiro()
    {
        if (ultima is null)
            return;
        using var dialogo = new SaveFileDialog
        {
            Filter = "Licença LAMGest (*.lic)|*.lic",
            FileName = $"licenca-{ultima.Dados.Nuit}-{ultima.Dados.ValidaAte:yyyyMMdd}.lic",
        };
        if (dialogo.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialogo.FileName, ultima.Texto);
            estado.Text = "Ficheiro guardado: " + dialogo.FileName;
        }
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
        try
        {
            armazem.ExportarCopia(dialogo.FileName, senha);
            MessageBox.Show(this, "Cópia guardada. Guarde o ficheiro fora deste computador (ex.: pen USB num cofre).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportarCopia()
    {
        using var dialogo = new OpenFileDialog { Filter = "Cópia da chave (*.pem)|*.pem" };
        if (dialogo.ShowDialog(this) != DialogResult.OK)
            return;
        var senha = Palavra.Pedir(this, "Palavra-passe da cópia:", confirmar: false);
        if (senha is null)
            return;
        try
        {
            armazem.ImportarCopia(dialogo.FileName, senha);
            MessageBox.Show(this, "Chave reposta.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>Pedido de palavra-passe (escondida).</summary>
internal static class Palavra
{
    public static string? Pedir(IWin32Window dono, string mensagem, bool confirmar)
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
