using System.Globalization;

namespace LAMGest.GeradorLicencas;

/// <summary>Meses pagos de cada vez (o valor é sempre por mês).</summary>
public enum Plano { Mensal = 1, Trimestral = 3, Semestral = 6, Anual = 12 }

/// <summary>Activo: recebe licenças. Suspenso (ex.: falta de pagamento) e Cancelado: o gerador recusa emitir.</summary>
public enum EstadoCliente { Activo, Suspenso, Cancelado }

public enum Situacao { SemLicenca, EmDia, AVencer, EmAtraso, Suspenso, Cancelado }

/// <summary>
/// Regras da cobrança das licenças mensais: a licença vale até ao fim do período pago; sem pagamento não se renova e o sistema
/// do cliente bloqueia as vendas quando ela expira (depois da tolerância do sistema).
/// </summary>
public static class RegrasCobranca
{
    /// <summary>Tolerância do sistema LAMGest depois de a licença expirar (igual a ServicoLicenca.DiasTolerancia).</summary>
    public const int DiasToleranciaSistema = 7;

    /// <summary>Faltando estes dias ou menos, o cliente aparece «a vencer».</summary>
    public const int DiasAVencer = 7;

    /// <summary>
    /// Nova validade depois de um pagamento de <paramref name="meses"/> meses. Pago antes do fim, ou dentro da tolerância do
    /// sistema, continua a partir do fim do período anterior (sem dias perdidos nem oferecidos); pago depois de o sistema
    /// bloquear, conta a partir da data do pagamento.
    /// </summary>
    public static DateOnly NovaValidade(DateOnly? validaAteActual, DateOnly dataPagamento, int meses)
    {
        if (meses is < 1 or > 36)
            throw new ArgumentException("Indique de 1 a 36 meses.");
        var inicio = validaAteActual is { } v && v.AddDays(DiasToleranciaSistema) >= dataPagamento ? v : dataPagamento;
        return inicio.AddMonths(meses);
    }

    public static Situacao SituacaoDe(EstadoCliente estado, DateOnly? validaAte, DateOnly hoje) => estado switch
    {
        EstadoCliente.Suspenso => Situacao.Suspenso,
        EstadoCliente.Cancelado => Situacao.Cancelado,
        _ when validaAte is null => Situacao.SemLicenca,
        _ when validaAte < hoje => Situacao.EmAtraso,
        _ when validaAte.Value.DayNumber - hoje.DayNumber <= DiasAVencer => Situacao.AVencer,
        _ => Situacao.EmDia,
    };

    /// <summary>Texto curto da situação para a lista de clientes.</summary>
    public static string Descrever(Situacao s, DateOnly? validaAte, DateOnly hoje) => s switch
    {
        Situacao.SemLicenca => "Sem licença",
        Situacao.Suspenso => "Suspenso",
        Situacao.Cancelado => "Cancelado",
        Situacao.EmAtraso when hoje.DayNumber - validaAte!.Value.DayNumber is var d =>
            d <= DiasToleranciaSistema
                ? $"Em atraso há {Dias(d)} (bloqueia dentro de {Dias(DiasToleranciaSistema - d + 1)})"
                : $"Em atraso há {Dias(d)} (sistema bloqueado)",
        Situacao.AVencer => validaAte!.Value == hoje ? "Vence hoje" : $"Vence em {Dias(validaAte.Value.DayNumber - hoje.DayNumber)}",
        _ => "Em dia",
    };

    private static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    /// <summary>Valor sugerido para um pagamento: valor por mês × meses.</summary>
    public static decimal ValorSugerido(decimal valorMensal, int meses) => decimal.Round(valorMensal * meses, 2);

    /// <summary>Mensagem pronta para WhatsApp ou e-mail, com a licença e como a activar.</summary>
    public static string Mensagem(string nome, DateOnly validaAte, string licenca) =>
        $"Olá. Segue a licença do sistema LAMGest para {nome}, válida até {validaAte.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.\n" +
        "Para activar: no sistema, Configurações → Licença → cole o texto abaixo (todo) → Activar.\n\n" + licenca;

    /// <summary>Lê um valor em MT escrito com vírgula ou ponto decimal (ex.: 2500, 2500,50 ou 2 500,50).</summary>
    public static bool LerValor(string texto, out decimal valor)
    {
        var t = texto.Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor) && valor >= 0 && decimal.Round(valor, 2) == valor;
    }

    public static string Valor(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("pt-PT")) + " MT";
}
