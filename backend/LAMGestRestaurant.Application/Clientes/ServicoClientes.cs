using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Clientes;

public record ClienteDto(
    int Id,
    TipoCliente Tipo,
    string Nome,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Nuit,
    string? Observacoes,
    string? CartaoFidelidade,
    int SaldoPontos,
    bool Activo,
    int NumeroPedidos,
    decimal TotalGasto,
    DateTime? UltimaVisitaUtc,
    byte[] RowVersion);

public record GuardarCliente(
    TipoCliente Tipo,
    [Required(ErrorMessage = "Indique o nome.")][StringLength(150, ErrorMessage = "O nome não pode exceder 150 caracteres.")] string Nome,
    [RegularExpression(@"^\+?[0-9 ]{7,20}$", ErrorMessage = "Telefone inválido.")] string? Telefone,
    [EmailAddress(ErrorMessage = "E-mail inválido.")][StringLength(200)] string? Email,
    [StringLength(1000)] string? Endereco,
    [RegularExpression(@"^\d{9}$", ErrorMessage = "O NUIT tem 9 algarismos.")] string? Nuit,
    [StringLength(1000)] string? Observacoes,
    [StringLength(30)] string? CartaoFidelidade,
    byte[]? RowVersion);

public record PedidoCliente(int Id, string Numero, TipoPedido Tipo, EstadoPedido Estado, decimal Total, DateTime CriadoEmUtc);

public record MovimentoPontosDto(int Id, TipoMovimentoPontos Tipo, int Pontos, string? Motivo, string? Pedido, DateTime DataUtc);

public record AjustarPontos(
    [Range(-1000000, 1000000, ErrorMessage = "Quantidade de pontos inválida.")] int Pontos,
    [Required(ErrorMessage = "Indique o motivo.")][StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")] string Motivo,
    byte[]? RowVersion);

/// <summary>
/// Clientes (particulares e empresas). Telefone e cartão de fidelidade são únicos quando preenchidos,
/// para evitar fichas duplicadas. Clientes com pedidos não se eliminam: desactivam-se.
/// </summary>
public class ServicoClientes(IAppDbContext db, IAuditoria auditoria, TimeProvider relogio)
{
    public async Task<Pagina<ClienteDto>> ListarAsync(string? pesquisa, bool? activo, int pagina, int tamanho, CancellationToken ct)
    {
        (pagina, tamanho) = Pagina<ClienteDto>.Normalizar(pagina, tamanho);
        var q = db.Clientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            var p = pesquisa.Trim();
            var tel = NormalizarTelefone(p);
            q = q.Where(c => c.Nome.Contains(p) || (tel != null && c.Telefone!.Contains(tel)) || c.Nuit == p || c.CartaoFidelidade == p);
        }
        if (activo is bool a) q = q.Where(c => c.Activo == a);
        var total = await q.CountAsync(ct);
        var itens = await Projectar(q.OrderBy(c => c.Nome).ThenBy(c => c.Id).Skip((pagina - 1) * tamanho).Take(tamanho)).ToListAsync(ct);
        return new Pagina<ClienteDto>(itens, total, pagina, tamanho);
    }

    public async Task<ClienteDto> ObterAsync(int id) =>
        await Projectar(db.Clientes.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync() ?? throw new NaoEncontradoException("Cliente não encontrado.");

    public async Task<ClienteDto> CriarAsync(GuardarCliente dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var cliente = new Cliente { Activo = true };
        await AplicarAsync(cliente, dados);
        db.Clientes.Add(cliente);
        await GravarAsync();
        auditoria.Registar("cliente.criar", "Cliente", cliente.Id, new { cliente.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(cliente.Id);
    }

    public async Task<ClienteDto> EditarAsync(int id, GuardarCliente dados)
    {
        var cliente = await db.Clientes.FindAsync(id) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        db.VerificarVersao(cliente, dados.RowVersion);
        await AplicarAsync(cliente, dados);
        auditoria.Registar("cliente.editar", "Cliente", id, new { cliente.Nome });
        await GravarAsync();
        return await ObterAsync(id);
    }

    public async Task DefinirActivoAsync(int id, bool activo)
    {
        var cliente = await db.Clientes.FindAsync(id) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        if (!activo && await db.Pedidos.AnyAsync(p => p.ClienteId == id && p.Estado == EstadoPedido.Aberto))
            throw new RegraNegocioException("O cliente tem pedidos em aberto.");
        cliente.Activo = activo;
        auditoria.Registar(activo ? "cliente.activar" : "cliente.desactivar", "Cliente", id, null);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<PedidoCliente>> PedidosAsync(int id, CancellationToken ct) =>
        await db.Pedidos.AsNoTracking().Where(p => p.ClienteId == id).OrderByDescending(p => p.CriadoEmUtc).Take(100)
            .Select(p => new PedidoCliente(p.Id, p.Numero, p.Tipo, p.Estado, p.Total, p.CriadoEmUtc)).ToListAsync(ct);

    public async Task<IReadOnlyList<MovimentoPontosDto>> PontosAsync(int id, CancellationToken ct) =>
        await db.MovimentosPontos.AsNoTracking().Where(m => m.ClienteId == id).OrderByDescending(m => m.DataUtc).Take(200)
            .Select(m => new MovimentoPontosDto(m.Id, m.Tipo, m.Pontos, m.Motivo,
                db.Pedidos.Where(p => p.Id == m.PedidoId).Select(p => p.Numero).FirstOrDefault(), m.DataUtc))
            .ToListAsync(ct);

    public async Task<ClienteDto> AjustarPontosAsync(int id, AjustarPontos dados)
    {
        if (dados.Pontos == 0) throw new RegraNegocioException("Indique uma quantidade de pontos diferente de zero.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var cliente = await db.Clientes.FindAsync(id) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        db.VerificarVersao(cliente, dados.RowVersion);
        if (cliente.SaldoPontos + dados.Pontos < 0)
            throw new RegraNegocioException($"O cliente só tem {cliente.SaldoPontos} pontos.");
        cliente.SaldoPontos += dados.Pontos;
        db.MovimentosPontos.Add(new MovimentoPontos
        {
            ClienteId = id, Tipo = TipoMovimentoPontos.Ajuste, Pontos = dados.Pontos, Motivo = dados.Motivo.Trim(),
            DataUtc = relogio.GetUtcNow().UtcDateTime,
        });
        auditoria.Registar("cliente.ajustar-pontos", "Cliente", id, new { dados.Pontos, Motivo = dados.Motivo.Trim() });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    private async Task AplicarAsync(Cliente cliente, GuardarCliente dados)
    {
        var telefone = NormalizarTelefone(dados.Telefone);
        var cartao = string.IsNullOrWhiteSpace(dados.CartaoFidelidade) ? null : dados.CartaoFidelidade.Trim().ToUpperInvariant();
        if (telefone is not null && await db.Clientes.AnyAsync(c => c.Id != cliente.Id && c.Telefone == telefone))
            throw new ConflitoException("Já existe um cliente com este telefone.");
        if (cartao is not null && await db.Clientes.AnyAsync(c => c.Id != cliente.Id && c.CartaoFidelidade == cartao))
            throw new ConflitoException("Este cartão de fidelidade já está atribuído a outro cliente.");
        cliente.Tipo = dados.Tipo;
        cliente.Nome = dados.Nome.Trim();
        cliente.Telefone = telefone;
        cliente.Email = Limpar(dados.Email);
        cliente.Endereco = Limpar(dados.Endereco);
        cliente.Nuit = Limpar(dados.Nuit);
        cliente.Observacoes = Limpar(dados.Observacoes);
        cliente.CartaoFidelidade = cartao;
    }

    private async Task GravarAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflitoException("Já existe um cliente com este telefone ou cartão.");
        }
    }

    private IQueryable<ClienteDto> Projectar(IQueryable<Cliente> q) => q.Select(c => new ClienteDto(
        c.Id, c.Tipo, c.Nome, c.Telefone, c.Email, c.Endereco, c.Nuit, c.Observacoes, c.CartaoFidelidade, c.SaldoPontos, c.Activo,
        db.Pedidos.Count(p => p.ClienteId == c.Id && p.Estado == EstadoPedido.Pago),
        db.Pedidos.Where(p => p.ClienteId == c.Id && p.Estado == EstadoPedido.Pago).Sum(p => (decimal?)p.Total) ?? 0m,
        db.Pedidos.Where(p => p.ClienteId == c.Id).Max(p => (DateTime?)p.CriadoEmUtc),
        c.RowVersion));

    /// <summary>Telefone só com algarismos (e «+» inicial), para comparar sem espaços.</summary>
    public static string? NormalizarTelefone(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        var t = new string(telefone.Trim().Where((ch, i) => char.IsDigit(ch) || (ch == '+' && i == 0)).ToArray());
        return t.Length == 0 ? null : t;
    }

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}

/// <summary>
/// Programa de fidelização (activo nas Configurações): 1 ponto por cada «valor por ponto» pago num pedido
/// com cliente associado. O estorno do pedido retira os pontos ganhos (sem deixar o saldo negativo).
/// O resgate de pontos ainda não está implementado (ver docs/TAREFAS.md). Chamado dentro da transacção do fecho/estorno.
/// </summary>
public class ServicoFidelizacao(IAppDbContext db, ConfiguracoesSistema config, IUtilizadorActual actual, TimeProvider relogio)
{
    public async Task AcumularAsync(Pedido pedido)
    {
        if (pedido.ClienteId is not int clienteId || !await config.BoolAsync(ChavesConfiguracao.FidelizacaoActiva))
            return;
        var valorPorPonto = await config.DecimalAsync(ChavesConfiguracao.PontosPorMoeda, 100m);
        if (valorPorPonto <= 0) return;
        var pontos = (int)Math.Floor(pedido.TotalPago / valorPorPonto);
        if (pontos <= 0) return;
        var cliente = await db.Clientes.FirstAsync(c => c.Id == clienteId);
        cliente.SaldoPontos += pontos;
        db.MovimentosPontos.Add(new MovimentoPontos
        {
            ClienteId = clienteId, PedidoId = pedido.Id, Tipo = TipoMovimentoPontos.Acumulacao, Pontos = pontos,
            Motivo = $"Pedido {pedido.Numero}", UtilizadorId = actual.Id, DataUtc = relogio.GetUtcNow().UtcDateTime,
        });
    }

    public async Task EstornarAsync(Pedido pedido)
    {
        if (pedido.ClienteId is not int clienteId) return;
        var ganhos = await db.MovimentosPontos.Where(m => m.PedidoId == pedido.Id && m.ClienteId == clienteId).SumAsync(m => (int?)m.Pontos) ?? 0;
        if (ganhos <= 0) return;
        var cliente = await db.Clientes.FirstAsync(c => c.Id == clienteId);
        var retirar = Math.Min(ganhos, cliente.SaldoPontos);
        if (retirar <= 0) return;
        cliente.SaldoPontos -= retirar;
        db.MovimentosPontos.Add(new MovimentoPontos
        {
            ClienteId = clienteId, PedidoId = pedido.Id, Tipo = TipoMovimentoPontos.Estorno, Pontos = -retirar,
            Motivo = $"Estorno do pedido {pedido.Numero}", UtilizadorId = actual.Id, DataUtc = relogio.GetUtcNow().UtcDateTime,
        });
    }
}
