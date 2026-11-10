using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

public sealed record CartaoDto(int Id, string Numero, string? Titular, decimal Saldo, bool Activo, DateTime CriadoEmUtc, DateTime? UltimoMovimentoUtc, byte[] RowVersion);

public sealed record MovimentoCartaoDto(int Id, DateTime DataUtc, TipoMovimentoCartao Tipo, decimal Valor, decimal SaldoApos, string? Metodo, string? Pedido, string? Motivo, string? Operador);

public sealed record RegistarCartao(
    [StringLength(80, ErrorMessage = "O número lido é demasiado longo.")] string? Numero,
    [StringLength(150, ErrorMessage = "O titular não pode exceder 150 caracteres.")] string? Titular,
    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor inicial vai de 0 a 1 000 000.")] decimal ValorInicial,
    int? MetodoPagamentoId);

public sealed record RecarregarCartao(
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor da recarga vai de 0,01 a 1 000 000.")] decimal Valor,
    [Range(1, int.MaxValue, ErrorMessage = "Escolha como foi pago.")] int MetodoPagamentoId);

public sealed record AlterarCartao(bool Activo, byte[]? RowVersion);

public sealed record EtiquetasCartoes([MinLength(1, ErrorMessage = "Escolha pelo menos um cartão.")] IReadOnlyList<int> Cartoes);

/// <summary>
/// Cartões Evento (pré-pagos). O dinheiro dos carregamentos e recargas entra no caixa aberto de quem os faz, pelo método
/// escolhido; os pagamentos com o cartão só abatem o saldo (não contam na gaveta). Anular um pagamento ou estornar o pedido
/// devolve o valor ao cartão. Tudo fica em movimentos (só inserção) e na auditoria.
/// </summary>
public sealed partial class ServicoCartoes(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, TimeProvider relogio, ServicoEtiquetas etiquetas,
    IConsultaUtilizadores utilizadores)
{
    public const string CodigoMetodo = "CARTAO_EVENTO";

    [GeneratedRegex(@"%[A-Z]?([0-9]{4,19})\^")] private static partial Regex Pista1();
    [GeneratedRegex(@";([0-9]{4,19})[=?]")] private static partial Regex Pista2();

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Número a partir do que o leitor escreveu: pista 1 («%B1234…^NOME^…?») ou 2 («;1234…=…?») de um cartão magnético,
    /// código de barras ou QR code. Fica só com letras, algarismos e hífenes, em maiúsculas.
    /// </summary>
    public static string Normalizar(string? lido)
    {
        var texto = (lido ?? "").Trim();
        if (Pista1().Match(texto) is { Success: true } p1) return p1.Groups[1].Value;
        if (Pista2().Match(texto) is { Success: true } p2) return p2.Groups[1].Value;
        return new string([.. texto.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')]).ToUpperInvariant();
    }

    public async Task<Pagina<CartaoDto>> ListarAsync(string? pesquisa, bool? activo, int pagina, int tamanho, CancellationToken ct)
    {
        var (p, t) = Pagina<CartaoDto>.Normalizar(pagina, tamanho);
        var q = db.CartoesEvento.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            var termo = pesquisa.Trim();
            var numero = Normalizar(termo);
            q = q.Where(c => c.Numero.Contains(numero) || (c.Titular != null && c.Titular.Contains(termo)));
        }
        if (activo is bool a) q = q.Where(c => c.Activo == a);
        var total = await q.CountAsync(ct);
        var itens = await Projectar(q.OrderByDescending(c => c.Id).Skip((p - 1) * t).Take(t)).ToListAsync(ct);
        return new Pagina<CartaoDto>(itens, total, p, t);
    }

    /// <summary>Cartão pelo que o leitor escreveu (consulta de saldo e pagamento no POS).</summary>
    public async Task<CartaoDto> ConsultarAsync(string? lido, CancellationToken ct)
    {
        var numero = Normalizar(lido);
        return await Projectar(db.CartoesEvento.AsNoTracking().Where(c => c.Numero == numero)).FirstOrDefaultAsync(ct)
               ?? throw new NaoEncontradoException(numero.Length == 0 ? "Passe o cartão ou escreva o número." : $"Cartão {numero} não encontrado.");
    }

    public async Task<CartaoDto> ObterAsync(int id, CancellationToken ct = default) =>
        await Projectar(db.CartoesEvento.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NaoEncontradoException("Cartão não encontrado.");

    public async Task<Pagina<MovimentoCartaoDto>> MovimentosAsync(int id, int pagina, int tamanho, CancellationToken ct)
    {
        var (p, t) = Pagina<MovimentoCartaoDto>.Normalizar(pagina, tamanho);
        var q = db.MovimentosCartao.AsNoTracking().Where(m => m.CartaoEventoId == id);
        var total = await q.CountAsync(ct);
        var linhas = await q.OrderByDescending(m => m.Id).Skip((p - 1) * t).Take(t)
            .Select(m => new
            {
                m.Id, m.DataUtc, m.Tipo, m.Valor, m.SaldoApos, Metodo = m.MetodoPagamento != null ? m.MetodoPagamento.Nome : null,
                Pedido = db.Pedidos.Where(x => x.Id == m.PedidoId).Select(x => x.Numero).FirstOrDefault(), m.Motivo, m.UtilizadorId,
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Select(l => l.UtilizadorId).Distinct(), ct);
        return new Pagina<MovimentoCartaoDto>(
            [.. linhas.Select(l => new MovimentoCartaoDto(l.Id, l.DataUtc, l.Tipo, l.Valor, l.SaldoApos, l.Metodo, l.Pedido, l.Motivo, nomes.GetValueOrDefault(l.UtilizadorId)))],
            total, p, t);
    }

    /// <summary>Regista o cartão (número lido ou gerado) com o valor inicial, que entra no caixa aberto de quem o regista.</summary>
    public async Task<(CartaoDto Cartao, bool Criado)> RegistarAsync(RegistarCartao dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await CartaoDaChaveAsync(chave) is int existente)
            return (await ObterAsync(existente), false);

        var numero = string.IsNullOrWhiteSpace(dados.Numero) ? await GerarNumeroAsync() : Normalizar(dados.Numero);
        if (numero.Length is < 4 or > 40)
            throw new RegraNegocioException("O número do cartão tem de ter 4 a 40 letras ou algarismos.");
        if (await db.CartoesEvento.AnyAsync(c => c.Numero == numero))
            throw new ConflitoException($"Já existe o cartão {numero}.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var cartao = new CartaoEvento { Numero = numero, Titular = string.IsNullOrWhiteSpace(dados.Titular) ? null : dados.Titular.Trim() };
        db.CartoesEvento.Add(cartao);
        await db.SaveChangesAsync();
        if (dados.ValorInicial > 0)
            await CarregarAsync(cartao, TipoMovimentoCartao.Carregamento, dados.ValorInicial, dados.MetodoPagamentoId, chave);
        else
            db.MovimentosCartao.Add(Movimento(cartao, TipoMovimentoCartao.Carregamento, 0, "Cartão registado sem saldo", chave));
        auditoria.Registar("cartao.registar", "CartaoEvento", cartao.Id, new { cartao.Numero, cartao.Titular, dados.ValorInicial });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ObterAsync(cartao.Id), true);
    }

    public async Task<(CartaoDto Cartao, bool Criado)> RecarregarAsync(int id, RecarregarCartao dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await CartaoDaChaveAsync(chave) is int existente)
            return existente == id ? (await ObterAsync(existente), false) : throw new ConflitoException("Esta chave já foi usada noutro cartão.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var cartao = await db.CartoesEvento.FindAsync(id) ?? throw new NaoEncontradoException("Cartão não encontrado.");
        if (!cartao.Activo)
            throw new RegraNegocioException($"O cartão {cartao.Numero} está bloqueado.");
        await CarregarAsync(cartao, TipoMovimentoCartao.Recarga, dados.Valor, dados.MetodoPagamentoId, chave);
        auditoria.Registar("cartao.recarregar", "CartaoEvento", cartao.Id, new { cartao.Numero, dados.Valor, Saldo = cartao.Saldo });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ObterAsync(cartao.Id), true);
    }

    /// <summary>Bloqueia (ex.: cartão perdido) ou desbloqueia. O saldo fica guardado.</summary>
    public async Task<CartaoDto> DefinirActivoAsync(int id, AlterarCartao dados)
    {
        var cartao = await db.CartoesEvento.FindAsync(id) ?? throw new NaoEncontradoException("Cartão não encontrado.");
        db.VerificarVersao(cartao, dados.RowVersion);
        cartao.Activo = dados.Activo;
        auditoria.Registar(dados.Activo ? "cartao.desbloquear" : "cartao.bloquear", "CartaoEvento", id, new { cartao.Numero, cartao.Saldo });
        await db.SaveChangesAsync();
        return await ObterAsync(id);
    }

    /// <summary>Etiquetas dos cartões (QR code, código de barras e número) na impressora de etiquetas.</summary>
    public async Task<string> ImprimirEtiquetasAsync(EtiquetasCartoes dados, CancellationToken ct)
    {
        var cartoes = await db.CartoesEvento.AsNoTracking().Where(c => dados.Cartoes.Contains(c.Id)).Select(c => new { c.Numero, c.Titular }).ToListAsync(ct);
        if (cartoes.Count == 0)
            throw new NaoEncontradoException("Cartão não encontrado.");
        return await etiquetas.ImprimirAsync([.. cartoes.Select(c => Etiqueta(c.Numero, c.Titular))], ct);
    }

    public async Task<byte[]?> PrevisaoEtiquetaAsync(int id, CancellationToken ct)
    {
        var c = await ObterAsync(id, ct);
        return await etiquetas.PrevisaoAsync(Etiqueta(c.Numero, c.Titular));
    }

    private static EtiquetaImpressao Etiqueta(string numero, string? titular)
    {
        var linhas = new List<LinhaImpressao> { new("CARTÃO EVENTO", Alinhamento: Alinhamento.Centro, Negrito: true) };
        if (titular is not null) linhas.Add(new(titular, Alinhamento: Alinhamento.Centro));
        return new EtiquetaImpressao(linhas, numero, numero);
    }

    // ---------- Pagamentos (chamados dentro da transacção do pagamento) ----------

    /// <summary>Abate o pagamento no saldo do cartão lido. Devolve o número normalizado (fica como referência do pagamento).</summary>
    internal async Task<string> DebitarAsync(string? lido, decimal valor, Pagamento pagamento, int pedidoId, string pedidoNumero)
    {
        var numero = Normalizar(lido);
        if (numero.Length == 0)
            throw new RegraNegocioException("Passe o Cartão Evento no leitor (ou escreva o número).");
        var cartao = await db.CartoesEvento.FirstOrDefaultAsync(c => c.Numero == numero)
                     ?? throw new RegraNegocioException($"Cartão Evento {numero} não encontrado.");
        if (!cartao.Activo)
            throw new RegraNegocioException($"O cartão {numero} está bloqueado.");
        if (cartao.Saldo < valor)
            throw new RegraNegocioException($"Saldo insuficiente no cartão {numero}: tem {cartao.Saldo:N2}, faltam {valor - cartao.Saldo:N2}.");
        cartao.Saldo -= valor;
        var m = Movimento(cartao, TipoMovimentoCartao.Pagamento, -valor, $"Pedido {pedidoNumero}", null);
        m.Pagamento = pagamento;
        m.PedidoId = pedidoId;
        db.MovimentosCartao.Add(m);
        return numero;
    }

    /// <summary>Devolve ao cartão o valor de um pagamento (anulação ou estorno do pedido).</summary>
    internal async Task CreditarAsync(Pagamento pagamento, TipoMovimentoCartao tipo, string motivo)
    {
        var origem = await db.MovimentosCartao.Where(m => m.PagamentoId == pagamento.Id && m.Tipo == TipoMovimentoCartao.Pagamento)
                         .Select(m => (int?)m.CartaoEventoId).FirstOrDefaultAsync()
                     ?? throw new RegraNegocioException("Este pagamento não tem Cartão Evento associado.");
        var cartao = await db.CartoesEvento.FindAsync(origem) ?? throw new NaoEncontradoException("Cartão não encontrado.");
        cartao.Saldo += pagamento.Valor;
        var m = Movimento(cartao, tipo, pagamento.Valor, motivo, null);
        m.PagamentoId = pagamento.Id;
        m.PedidoId = pagamento.PedidoId;
        db.MovimentosCartao.Add(m);
    }

    // ---------- Auxiliares ----------

    private async Task CarregarAsync(CartaoEvento cartao, TipoMovimentoCartao tipo, decimal valor, int? metodoId, string chave)
    {
        valor = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        var metodo = metodoId is int id ? await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == id && m.Activo) : null;
        if (metodo is null || metodo.Codigo == CodigoMetodo)
            throw new RegraNegocioException("Escolha como foi pago o carregamento (dinheiro, M-Pesa…).");
        var utilizador = actual.Id ?? throw new RegraNegocioException("Sessão de utilizador inválida.");
        var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.UtilizadorAberturaId == utilizador && s.Estado == EstadoSessaoCaixa.Aberta)
                     ?? throw new RegraNegocioException("Abra o seu caixa para receber o dinheiro do cartão.");
        var texto = tipo == TipoMovimentoCartao.Recarga ? "Recarga" : "Carregamento";
        var movimentoCaixa = new MovimentoCaixa
        {
            SessaoCaixaId = sessao.Id,
            Tipo = TipoMovimentoCaixa.Entrada,
            MetodoPagamentoId = metodo.Id,
            Valor = valor,
            Motivo = $"{texto} do Cartão Evento {cartao.Numero}",
            UtilizadorId = utilizador,
            DataUtc = Agora,
        };
        db.MovimentosCaixa.Add(movimentoCaixa);
        sessao.AlteradoEmUtc = Agora;
        cartao.Saldo += valor;
        var m = Movimento(cartao, tipo, valor, null, chave);
        m.MetodoPagamentoId = metodo.Id;
        m.SessaoCaixaId = sessao.Id;
        m.MovimentoCaixa = movimentoCaixa;
        db.MovimentosCartao.Add(m);
    }

    private MovimentoCartao Movimento(CartaoEvento cartao, TipoMovimentoCartao tipo, decimal valor, string? motivo, string? chave) => new()
    {
        CartaoEventoId = cartao.Id,
        CartaoEvento = cartao,
        Tipo = tipo,
        Valor = valor,
        SaldoApos = cartao.Saldo,
        Motivo = motivo,
        UtilizadorId = actual.Id ?? 0,
        DataUtc = Agora,
        IdempotencyKey = chave,
    };

    private async Task<int?> CartaoDaChaveAsync(string chave) =>
        await db.MovimentosCartao.Where(m => m.IdempotencyKey == chave).Select(m => (int?)m.CartaoEventoId).FirstOrDefaultAsync();

    /// <summary>12 algarismos aleatórios (o primeiro nunca 0): curto no código de barras e difícil de adivinhar.</summary>
    private async Task<string> GerarNumeroAsync()
    {
        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            var numero = RandomNumberGenerator.GetInt32(1, 10) + string.Concat(Enumerable.Range(0, 11).Select(_ => RandomNumberGenerator.GetInt32(0, 10)));
            if (!await db.CartoesEvento.AnyAsync(c => c.Numero == numero))
                return numero;
        }
        throw new InvalidOperationException("Não foi possível gerar um número de cartão livre.");
    }

    private IQueryable<CartaoDto> Projectar(IQueryable<CartaoEvento> q) => q.Select(c => new CartaoDto(c.Id, c.Numero, c.Titular, c.Saldo, c.Activo, c.CriadoEmUtc,
        db.MovimentosCartao.Where(m => m.CartaoEventoId == c.Id).Max(m => (DateTime?)m.DataUtc), c.RowVersion));
}
