using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using LAMGestRestaurant.Configurar.Motor;

namespace LAMGestRestaurant.Configurar.Ui;

/// <summary>
/// Posto: o posto não liga à base de dados, abre a página do servidor. Mudar a base aqui é mudar o servidor a que o posto
/// liga (atalhos e agente de impressão). A base de cada servidor escolhe-se no próprio servidor.
/// </summary>
internal sealed class JanelaPosto : Form
{
    private readonly Locais locais;
    private readonly PostoInstalado posto;

    private readonly Label servidorEmUso = Estilo.Valor("", 200, 112);
    private readonly Label agenteEmUso = Estilo.Valor("", 200, 136);
    private readonly TextBox servidor = new() { Width = 220, Location = new Point(200, 196) };
    private readonly NumericUpDown porta = new() { Minimum = 1, Maximum = 65535, Width = 90, Location = new Point(200, 228) };
    private readonly NumericUpDown numero = new() { Minimum = 1, Maximum = 999, Width = 90, Location = new Point(200, 260) };
    private readonly Button testar = Estilo.Botao("Testar ligação", 140);
    private readonly Label resultado = new() { AutoSize = false, Location = new Point(200, 292), Size = new Size(476, 40) };
    private readonly CheckBox registarAgente = new() { Text = "Registar de novo o agente de impressão deste posto no servidor", AutoSize = true, Location = new Point(24, 380) };
    private readonly TextBox utilizador = new() { Text = "admin", Width = 180, Location = new Point(200, 408) };
    private readonly TextBox palavraPasse = new() { UseSystemPasswordChar = true, Width = 180, Location = new Point(200, 440) };
    private readonly TextBox registo = Estilo.Registo();
    private readonly Button aplicar = Estilo.Botao("Aplicar", 120, principal: true);
    private readonly Button fechar = Estilo.Botao("Fechar", 100);
    private bool aTrabalhar;

    public JanelaPosto(Locais locais, PostoInstalado posto)
    {
        this.locais = locais;
        this.posto = posto;
        Estilo.Janela(this, "LAMGest Restaurant — Configurar posto", new Size(700, 654));
        var largura = ClientSize.Width;

        Controls.Add(Estilo.Seccao("Em uso agora", 24, 84));
        Controls.Add(Estilo.Rotulo("Servidor", 24, 112));
        Controls.Add(servidorEmUso);
        Controls.Add(Estilo.Rotulo("Agente de impressão", 24, 136));
        Controls.Add(agenteEmUso);

        Controls.Add(Estilo.Seccao("Mudar para", 24, 168));
        Controls.Add(Estilo.Rotulo("IP ou nome do servidor", 24, 196));
        Controls.Add(servidor);
        testar.Location = new Point(432, 194);
        testar.Height = 28;
        Controls.Add(testar);
        Controls.Add(Estilo.Rotulo("Porta", 24, 228));
        Controls.Add(porta);
        Controls.Add(Estilo.Rotulo("Número do posto", 24, 260));
        Controls.Add(numero);
        Controls.Add(resultado);
        Controls.Add(new Label
        {
            Text = "O posto não liga à base de dados: abre a página do servidor. A base de cada servidor escolhe-se no próprio servidor.",
            AutoSize = false, Location = new Point(24, 334), Size = new Size(652, 40), ForeColor = Estilo.Cinzento,
        });

        Controls.Add(registarAgente);
        Controls.Add(Estilo.Rotulo("Utilizador do LAMGest", 42, 408, 150));
        Controls.Add(utilizador);
        Controls.Add(Estilo.Rotulo("Palavra-passe", 42, 440, 150));
        Controls.Add(palavraPasse);
        registo.Location = new Point(24, 480);
        registo.Size = new Size(652, 108);
        Controls.Add(registo);

        Controls.Add(Estilo.Separador(DockStyle.Top));
        Controls.Add(Estilo.Cabecalho("Servidor deste posto", "Escolha o servidor LAMGest (e a sua base) que este posto abre.", largura));
        Controls.Add(Estilo.Separador(DockStyle.Bottom));
        Controls.Add(Estilo.Rodape(aplicar, fechar));

        Estilo.Escalar(this);
        servidorEmUso.Text = $"{posto.Servidor}:{posto.Porta} · posto n.º {posto.Numero}";
        agenteEmUso.Text = posto.TemAgente ? posto.ServidorAgente ?? "instalado" : "não instalado neste posto";
        servidor.Text = posto.Servidor;
        porta.Value = Math.Max(1, Math.Min(65535, posto.Porta));
        numero.Value = Math.Max(1, Math.Min(999, posto.Numero));
        registarAgente.Enabled = posto.TemAgente;
        registarAgente.Checked = false;
        AtualizarAgente();

        registarAgente.CheckedChanged += (_, _) => AtualizarAgente();
        foreach (var c in new Control[] { servidor, porta, numero })
            c.TextChanged += (_, _) =>
            {
                // Mudou o servidor: o agente tem de ser registado no novo (a chave é de cada servidor).
                if (posto.TemAgente && (servidor.Text.Trim() != posto.Servidor || (int)porta.Value != posto.Porta || (int)numero.Value != posto.Numero))
                    registarAgente.Checked = true;
            };
        testar.Click += async (_, _) => await TestarAsync();
        aplicar.Click += async (_, _) => await AplicarAsync();
        fechar.Click += (_, _) => Close();
        FormClosing += (_, e) => e.Cancel = aTrabalhar;
    }

    private void AtualizarAgente() => utilizador.Enabled = palavraPasse.Enabled = registarAgente.Checked;

    private async Task<bool> TestarAsync()
    {
        if (ConfiguracaoPosto.ValidarServidor(servidor.Text) is { } erro)
        {
            Estilo.Resultado(resultado, erro, false);
            return false;
        }
        testar.Enabled = false;
        Estilo.Resultado(resultado, "A testar…", null);
        try
        {
            var nome = await ConfiguracaoPosto.TestarAsync(servidor.Text, (int)porta.Value, (int)numero.Value);
            Estilo.Resultado(resultado, $"✔ Servidor LAMGest a funcionar · posto {numero.Value}: {nome}", true);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            Estilo.Resultado(resultado, "✖ " + ex.Message, false);
            return false;
        }
        finally
        {
            testar.Enabled = true;
        }
    }

    private async Task AplicarAsync()
    {
        if (registarAgente.Checked && (utilizador.Text.Trim().Length == 0 || palavraPasse.Text.Length == 0))
        {
            Estilo.Resultado(resultado, "Indique o utilizador e a palavra-passe de um administrador do LAMGest para registar o agente.", false);
            return;
        }
        aTrabalhar = true;
        aplicar.Enabled = testar.Enabled = fechar.Enabled = false;
        registo.Clear();
        var progresso = new Progress<string>(l => registo.AppendText(l + Environment.NewLine));
        try
        {
            await ConfiguracaoPosto.AplicarAsync(locais, posto, new PedidoPosto
            {
                Servidor = servidor.Text.Trim(), Porta = (int)porta.Value, Numero = (int)numero.Value,
                RegistarAgente = registarAgente.Checked, UtilizadorLamgest = utilizador.Text, PalavraPasseLamgest = palavraPasse.Text,
            }, progresso);
            posto.Servidor = servidor.Text.Trim();
            posto.Porta = (int)porta.Value;
            posto.Numero = (int)numero.Value;
            servidorEmUso.Text = $"{posto.Servidor}:{posto.Porta} · posto n.º {posto.Numero}";
            if (registarAgente.Checked)
                agenteEmUso.Text = ConfiguracaoPosto.Endereco(posto.Servidor, posto.Porta).ToString().TrimEnd('/');
            Estilo.Resultado(resultado, "✔ Posto configurado. Feche e abra o «LAMGest Restaurant».", true);
        }
        catch (Exception ex)
        {
            registo.AppendText("✖ " + ex.Message + Environment.NewLine);
            Estilo.Resultado(resultado, "✖ " + ex.Message, false);
        }
        finally
        {
            palavraPasse.Clear();
            aTrabalhar = false;
            aplicar.Enabled = testar.Enabled = fechar.Enabled = true;
        }
    }

    /// <summary>Dados de exemplo para a captura (não liga a nada).</summary>
    internal void Exemplo()
    {
        servidor.Text = "192.168.1.20";
        numero.Value = 3;
        palavraPasse.Text = "exemplo";
        Estilo.Resultado(resultado, "✔ Servidor LAMGest a funcionar · posto 3: Caixa Bar", true);
    }
}
