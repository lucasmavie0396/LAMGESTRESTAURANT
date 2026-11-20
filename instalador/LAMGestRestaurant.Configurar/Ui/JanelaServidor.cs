using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LAMGestRestaurant.Configurar.Motor;

namespace LAMGestRestaurant.Configurar.Ui;

/// <summary>Servidor: escolher a instância do SQL Server e a base do LAMGest que o sistema abre.</summary>
internal sealed class JanelaServidor : Form
{
    private readonly Locais locais;
    private readonly ServidorInstalado servidor;
    private readonly bool demonstracao;

    private readonly Label baseEmUso = Estilo.Valor("", 200, 112);
    private readonly Label servicoEmUso = Estilo.Valor("", 200, 136);
    private readonly ComboBox instancia = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 250, Location = new Point(200, 196) };
    private readonly RadioButton autWindows = new() { Text = "Windows (esta conta)", AutoSize = true, Checked = true, Location = new Point(200, 228) };
    private readonly RadioButton autSql = new() { Text = "SQL Server", AutoSize = true, Location = new Point(370, 228) };
    private readonly TextBox utilizadorSql = new() { Text = "sa", Width = 120, Location = new Point(470, 226), Enabled = false };
    private readonly TextBox palavraPasseSql = new() { UseSystemPasswordChar = true, Width = 140, Location = new Point(598, 226), Enabled = false };
    private readonly Button procurar = Estilo.Botao("Procurar bases", 140);
    private readonly ListView bases = new()
    {
        View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Location = new Point(24, 262), Size = new Size(732, 138),
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Label situacao = new() { AutoSize = false, Location = new Point(24, 404), Size = new Size(732, 22) };
    private readonly CheckBox confirmar = new()
    {
        AutoSize = false, Location = new Point(24, 426), Size = new Size(732, 40), Visible = false, CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft,
        Text = "Confirmo a actualização desta base para a versão do programa (é feita uma cópia de segurança completa antes; não pode ser desfeita).",
    };
    private readonly Label aviso = new()
    {
        AutoSize = false, Location = new Point(24, 470), Size = new Size(732, 74), BackColor = Estilo.FundoAmbar, ForeColor = Estilo.Ambar, Padding = new Padding(8, 6, 8, 6),
        Text = "⚠ Ao aplicar, o serviço LAMGest pára e volta a arrancar (cerca de 10 s): todos os postos perdem a ligação por momentos e os utilizadores voltam a entrar. " +
               "Os agentes de impressão dos postos têm de ser registados de novo (LAMGest-Configurar em cada posto), salvo se a base nova já os tiver.",
    };
    private readonly TextBox registo = Estilo.Registo();
    private readonly Button aplicar = Estilo.Botao("Aplicar e reiniciar serviço", 210, principal: true);
    private readonly Button fechar = Estilo.Botao("Fechar", 100);
    private IReadOnlyList<string> migracoes = Array.Empty<string>();
    private bool aTrabalhar;

    public JanelaServidor(Locais locais, ServidorInstalado servidor, bool demonstracao = false)
    {
        this.locais = locais;
        this.servidor = servidor;
        this.demonstracao = demonstracao;
        Estilo.Janela(this, "LAMGest Restaurant — Configurar base de dados (servidor)", new Size(780, 738));
        var largura = ClientSize.Width;

        Controls.Add(Estilo.Seccao("Em uso agora", 24, 84));
        Controls.Add(Estilo.Rotulo("Base de dados", 24, 112));
        Controls.Add(baseEmUso);
        Controls.Add(Estilo.Rotulo("Serviço", 24, 136));
        Controls.Add(servicoEmUso);

        Controls.Add(Estilo.Seccao("Mudar para", 24, 168));
        Controls.Add(Estilo.Rotulo("Instância SQL Server", 24, 196));
        Controls.Add(instancia);
        procurar.Location = new Point(462, 194);
        procurar.Height = 28;
        Controls.Add(procurar);
        Controls.Add(Estilo.Rotulo("Autenticação", 24, 226));
        Controls.AddRange(new Control[] { autWindows, autSql, utilizadorSql, palavraPasseSql });

        bases.Columns.Add("Base de dados", 250);
        bases.Columns.Add("Situação", 300);
        bases.Columns.Add("Última actualização", 160);
        Controls.Add(bases);
        Controls.Add(situacao);
        Controls.Add(confirmar);
        Controls.Add(aviso);
        registo.Location = new Point(24, 554);
        registo.Size = new Size(732, 112);
        Controls.Add(registo);

        Controls.Add(Estilo.Separador(DockStyle.Top));
        Controls.Add(Estilo.Cabecalho("Base de dados do servidor", "Escolha a base do LAMGest que este servidor abre.", largura));
        Controls.Add(Estilo.Separador(DockStyle.Bottom));
        Controls.Add(Estilo.Rodape(aplicar, fechar));

        autWindows.CheckedChanged += (_, _) => utilizadorSql.Enabled = palavraPasseSql.Enabled = autSql.Checked;
        procurar.Click += async (_, _) => await ProcurarAsync();
        bases.SelectedIndexChanged += (_, _) => Seleccionada();
        aplicar.Click += async (_, _) => await AplicarAsync();
        fechar.Click += (_, _) => Close();
        FormClosing += (_, e) => e.Cancel = aTrabalhar;

        Estilo.Escalar(this);
        MostrarActual();
        instancia.Items.AddRange(BasesSql.InstanciasLocais().Cast<object>().ToArray());
        instancia.Text = servidor.Instancia.Length > 0 ? servidor.Instancia : @".\SQLEXPRESS";
        aplicar.Enabled = false;
        if (!demonstracao)
            Shown += async (_, _) => await ProcurarAsync();
    }

    private void MostrarActual()
    {
        baseEmUso.Text = servidor.BaseDados.Length > 0 ? $"{servidor.BaseDados}   (instância {servidor.Instancia})" : "(sem configuração)";
        servicoEmUso.Text = $"{servidor.EstadoServico ?? "não encontrado"} · porta {servidor.Porta} · {servidor.PastaInstalacao}";
    }

    private LigacaoAdmin Admin() => new(instancia.Text, autWindows.Checked, utilizadorSql.Text, palavraPasseSql.Text);

    private async Task ProcurarAsync()
    {
        if (string.IsNullOrWhiteSpace(instancia.Text))
            return;
        procurar.Enabled = aplicar.Enabled = false;
        bases.Items.Clear();
        Estilo.Resultado(situacao, "A procurar as bases do LAMGest…", null);
        try
        {
            var admin = Admin();
            migracoes = await new TrocaBase(locais, servidor, new Progress<string>()).MigracoesDeReferenciaAsync(admin);
            Mostrar(await BasesSql.ListarAsync(admin, migracoes), admin.Instancia);
        }
        catch (SqlException ex)
        {
            Estilo.Resultado(situacao, "Não foi possível ligar ao SQL Server: " + ex.Message, false);
        }
        finally
        {
            procurar.Enabled = true;
        }
    }

    private void Mostrar(IReadOnlyList<InfoBase> lista, string instanciaLida)
    {
        bases.Items.Clear();
        foreach (var b in lista)
        {
            var emUso = string.Equals(b.Nome, servidor.BaseDados, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(instanciaLida, servidor.Instancia, StringComparison.OrdinalIgnoreCase);
            var item = new ListViewItem(new[] { b.Nome, b.Descricao + (emUso ? " · em uso" : ""), DataMigracao(b.UltimaMigracao) }) { Tag = b };
            if (!b.PodeSerUsada)
                item.ForeColor = Estilo.Cinzento;
            else if (b.Situacao == SituacaoBase.PrecisaActualizacao)
                item.ForeColor = Estilo.Ambar;
            if (emUso)
                item.Font = new Font(bases.Font, FontStyle.Bold);
            bases.Items.Add(item);
        }
        Estilo.Resultado(situacao, lista.Count == 0
            ? "Não há bases do LAMGest nesta instância (só são mostradas bases com nome «LAMGestRestaurant…»)."
            : "Escolha a base. Só são mostradas bases com nome «LAMGestRestaurant…»; as outras bases da instância não são abertas.", lista.Count == 0 ? false : null);
    }

    private static string DataMigracao(string? migracao) =>
        migracao is { Length: >= 8 } m && m.Take(8).All(char.IsDigit) ? $"{m.Substring(6, 2)}/{m.Substring(4, 2)}/{m.Substring(0, 4)}" : "—";

    private InfoBase? Escolhida => bases.SelectedItems.Count == 1 ? bases.SelectedItems[0].Tag as InfoBase : null;

    private bool EmUso(InfoBase b) =>
        string.Equals(b.Nome, servidor.BaseDados, StringComparison.OrdinalIgnoreCase) && string.Equals(instancia.Text.Trim(), servidor.Instancia, StringComparison.OrdinalIgnoreCase);

    private void Seleccionada()
    {
        var b = Escolhida;
        confirmar.Visible = b?.Situacao == SituacaoBase.PrecisaActualizacao;
        confirmar.Checked = false;
        aplicar.Enabled = b is not null && b.PodeSerUsada && !EmUso(b) && !aTrabalhar;
        if (b is null)
            return;
        if (EmUso(b))
            Estilo.Resultado(situacao, "Esta é a base em uso.", null);
        else if (!b.PodeSerUsada)
            Estilo.Resultado(situacao, $"«{b.Nome}» não pode ser usada: {b.Descricao}.", false);
        else
            Estilo.Resultado(situacao, $"O servidor passa a abrir «{b.Nome}».", true);
    }

    private async Task AplicarAsync()
    {
        var b = Escolhida;
        if (b is null || !b.PodeSerUsada)
            return;
        if (b.Situacao == SituacaoBase.PrecisaActualizacao && !confirmar.Checked)
        {
            Estilo.Resultado(situacao, "Confirme a actualização da base (caixa acima do aviso).", false);
            return;
        }
        if (MessageBox.Show(this, $"O servidor vai passar a abrir a base «{b.Nome}» (instância {instancia.Text.Trim()}).\n\nO serviço LAMGest vai ser reiniciado agora. Continuar?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        aTrabalhar = true;
        aplicar.Enabled = procurar.Enabled = fechar.Enabled = bases.Enabled = false;
        registo.Clear();
        var progresso = new Progress<string>(l => registo.AppendText(l + Environment.NewLine));
        try
        {
            await new TrocaBase(locais, servidor, progresso).ExecutarAsync(new PedidoTroca
            {
                Admin = Admin(), BaseDados = b.Nome, ActualizacaoConfirmada = confirmar.Checked,
            });
            MostrarActual();
            Estilo.Resultado(situacao, $"Concluído: o servidor abre «{b.Nome}». Registe de novo os agentes de impressão nos postos, se for preciso.", true);
        }
        catch (Exception ex)
        {
            registo.AppendText("✖ " + ex.Message + Environment.NewLine);
            Estilo.Resultado(situacao, "A troca não foi feita: " + ex.Message, false);
        }
        finally
        {
            aTrabalhar = false;
            procurar.Enabled = fechar.Enabled = bases.Enabled = true;
        }
        if (!demonstracao)
            await ProcurarAsync();
    }

    /// <summary>Dados de exemplo para a captura (não liga a nada).</summary>
    internal void Exemplo()
    {
        Mostrar(new[]
        {
            new InfoBase { Nome = "LAMGestRestaurantDB", Situacao = SituacaoBase.Actualizada, UltimaMigracao = "20261118120000_RelatoriosGrelha" },
            new InfoBase { Nome = "LAMGestRestaurant_Filial2", Situacao = SituacaoBase.Actualizada, UltimaMigracao = "20261118120000_RelatoriosGrelha" },
            new InfoBase { Nome = "LAMGestRestaurant_Formacao", Situacao = SituacaoBase.PrecisaActualizacao, Pendentes = new[] { "a", "b", "c" }, UltimaMigracao = "20261002090000_Balanca" },
            new InfoBase { Nome = "LAMGestRestaurant_Antiga", Situacao = SituacaoBase.MaisRecente, UltimaMigracao = "20261201000000_Futura" },
        }, servidor.Instancia);
        bases.Items[2].Selected = true;
        confirmar.Checked = true;
        registo.Text = "• A verificar a base LAMGestRestaurant_Formacao…\r\n• Cópia de segurança da base antes de a actualizar…\r\n";
    }
}
