using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using LAMGestRestaurant.Configurar.Motor;

namespace LAMGestRestaurant.Configurar.Ui;

/// <summary>Primeira janela: só quem souber o código de acesso do LAMGest entra no configurador.</summary>
internal sealed class JanelaCodigo : Form
{
    private readonly TextBox codigo = new() { UseSystemPasswordChar = true, Width = 240, Location = new Point(28, 150) };
    private readonly Button entrar = Estilo.Botao("Entrar", 110, principal: true);
    private readonly Label mensagem = new() { AutoSize = false, Size = new Size(420, 44), Location = new Point(28, 186) };
    private readonly BloqueioTentativas? bloqueio;
    private readonly Locais locais;

    public JanelaCodigo(Locais locais, BloqueioTentativas? bloqueio)
    {
        this.locais = locais;
        this.bloqueio = bloqueio;
        Estilo.Janela(this, "LAMGest Restaurant — Configurar base de dados", new Size(480, 300));
        var largura = ClientSize.Width;
        Controls.Add(new Label
        {
            Text = "Esta ferramenta muda a base de dados que o sistema abre. Só o administrador do LAMGest a pode usar.",
            AutoSize = false, Size = new Size(largura - 56, 42), Location = new Point(28, 82), ForeColor = Estilo.Cinzento,
        });
        Controls.Add(Estilo.Rotulo("Código de acesso", 28, 124));
        Controls.Add(codigo);
        entrar.Location = new Point(280, 148);
        entrar.Height = 28;
        Controls.Add(entrar);
        Controls.Add(mensagem);
        Controls.Add(Estilo.Separador(DockStyle.Top));
        Controls.Add(Estilo.Cabecalho("Acesso do administrador", "Introduza o código de acesso do LAMGest.", largura));
        AcceptButton = entrar;
        entrar.Click += async (_, _) => await EntrarAsync();
        Estilo.Escalar(this);
        Shown += (_, _) =>
        {
            codigo.Focus();
            MostrarBloqueio();
        };
    }

    private void MostrarBloqueio()
    {
        if (bloqueio?.Restante() is TimeSpan falta)
        {
            Estilo.Resultado(mensagem, $"Demasiadas tentativas erradas. Tente de novo dentro de {Math.Ceiling(falta.TotalMinutes)} minuto(s).", false);
            codigo.Enabled = entrar.Enabled = false;
        }
    }

    private async Task EntrarAsync()
    {
        if (bloqueio?.Restante() is not null)
        {
            MostrarBloqueio();
            return;
        }
        entrar.Enabled = false;
        Estilo.Resultado(mensagem, "A verificar…", null);
        var texto = codigo.Text;
        // A verificação (PBKDF2) demora um pouco: não bloqueia a janela.
        var valido = await Task.Run(() => CodigoAcesso.Valido(texto));
        if (valido)
        {
            bloqueio?.Limpar();
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        codigo.Clear();
        codigo.Focus();
        entrar.Enabled = true;
        RegistoAlteracoes.Escrever(locais, "Código de acesso errado");
        if (bloqueio is null)
        {
            Estilo.Resultado(mensagem, "Código errado.", false);
            return;
        }
        if (bloqueio.RegistarFalha())
        {
            RegistoAlteracoes.Escrever(locais, $"Configurador bloqueado durante {BloqueioTentativas.Duracao.TotalMinutes} minutos");
            MostrarBloqueio();
        }
        else
            Estilo.Resultado(mensagem, $"Código errado. Restam {bloqueio.TentativasRestantes} tentativa(s).", false);
    }

    /// <summary>Estado de exemplo para a captura (código errado uma vez).</summary>
    internal void Exemplo()
    {
        codigo.Text = "exemplo";
        Estilo.Resultado(mensagem, "Código errado. Restam 4 tentativa(s).", false);
    }
}
