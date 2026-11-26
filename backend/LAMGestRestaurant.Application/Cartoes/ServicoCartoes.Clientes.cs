using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Cartoes;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

/// <summary><see cref="ClienteId"/> vazio: o cartão deixa de pertencer a um cliente.</summary>
public sealed record AssociarClienteCartao(int? ClienteId, byte[]? RowVersion);

/// <summary>
/// Passa o saldo todo do cartão para o cartão <see cref="Destino"/> (número lido pelo leitor, câmara ou NFC). Com
/// <see cref="BloquearOrigem"/>, o cartão de origem fica bloqueado (ex.: perdido).
/// </summary>
public sealed record TransferirSaldo(
    [Required(ErrorMessage = "Leia o cartão que recebe o saldo.")]
    [StringLength(80, ErrorMessage = "O número lido é demasiado longo.")] string Destino,
    byte[]? RowVersion,
    bool BloquearOrigem = true);

/// <summary>
/// Cartões de clientes (adiantamentos): o cartão pode pertencer a um cliente e cada movimento guarda o cliente do momento,
/// para a conta de adiantamentos do cliente ser a soma dos seus movimentos. Por isso, quando um cartão com saldo muda de
/// cliente, o saldo sai da conta do anterior e entra na do novo (dois movimentos que no cartão se anulam).
/// </summary>
public sealed partial class ServicoCartoes
{
    public async Task<CartaoDto> AssociarClienteAsync(int id, AssociarClienteCartao dados)
    {
        ExigirPermissaoCliente();
        var novo = dados.ClienteId is int c ? await ValidarClienteAsync(c) : null;

        await using var transacao = await db.Database.BeginTransactionAsync();
        var cartao = await db.CartoesEvento.FindAsync(id) ?? throw new NaoEncontradoException("Cartão não encontrado.");
        db.VerificarVersao(cartao, dados.RowVersion);
        if (cartao.ClienteId == dados.ClienteId)
            return await ObterAsync(id);
        var anterior = cartao.ClienteId is int a ? await db.Clientes.Where(x => x.Id == a).Select(x => x.Nome).FirstOrDefaultAsync() : null;

        // O saldo do cartão não muda (o saldo após fica igual); só muda a conta de adiantamentos onde conta.
        var saldo = cartao.Saldo;
        if (saldo > 0)
        {
            var motivo = novo is null ? $"Retirado do cliente {anterior}" : anterior is null ? $"Associado ao cliente {novo}" : $"Passou de {anterior} para {novo}";
            if (cartao.ClienteId is not null)
                db.MovimentosCartao.Add(Movimento(cartao, TipoMovimentoCartao.MudancaCliente, -saldo, motivo, null));
            cartao.ClienteId = dados.ClienteId;
            if (cartao.ClienteId is not null)
                db.MovimentosCartao.Add(Movimento(cartao, TipoMovimentoCartao.MudancaCliente, saldo, motivo, null));
        }
        cartao.ClienteId = dados.ClienteId;
        auditoria.Registar(dados.ClienteId is null ? "cartao.desassociar-cliente" : "cartao.associar-cliente", "CartaoEvento", id,
            new { cartao.Numero, Saldo = saldo, Anterior = anterior, Cliente = novo, dados.ClienteId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    /// <summary>
    /// Transfere o saldo todo para outro cartão do mesmo cliente (ou sem cliente e sem saldo, que fica do mesmo cliente).
    /// Nunca passa dinheiro de um cliente para outro: para isso muda-se o cliente do cartão.
    /// </summary>
    public async Task<(CartaoDto Destino, bool Criada)> TransferirSaldoAsync(int id, TransferirSaldo dados, string? chaveIdempotencia)
    {
        ExigirPermissaoCliente();
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await db.MovimentosCartao.AsNoTracking().Where(m => m.IdempotencyKey == chave)
                .Select(m => new { m.CartaoEventoId, m.Tipo, m.Motivo }).FirstOrDefaultAsync() is { } feito)
        {
            if (feito.CartaoEventoId != id || feito.Tipo != TipoMovimentoCartao.TransferenciaSaida)
                throw new ConflitoException("Esta chave já foi usada noutra operação.");
            var destinoFeito = await db.MovimentosCartao.AsNoTracking()
                .Where(m => m.IdempotencyKey == chave + "#2").Select(m => m.CartaoEventoId).FirstAsync();
            return (await ObterAsync(destinoFeito), false);
        }

        var numero = Normalizar(dados.Destino);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var origem = await db.CartoesEvento.FindAsync(id) ?? throw new NaoEncontradoException("Cartão não encontrado.");
        db.VerificarVersao(origem, dados.RowVersion);
        var destino = await db.CartoesEvento.FirstOrDefaultAsync(c => c.Numero == numero)
                      ?? throw new NaoEncontradoException(numero.Length == 0 ? "Leia o cartão que recebe o saldo." : $"Cartão {numero} não encontrado. Registe-o primeiro (sem saldo).");
        if (destino.Id == origem.Id)
            throw new RegraNegocioException("Escolha outro cartão para receber o saldo.");
        if (!destino.Activo)
            throw new RegraNegocioException($"O cartão {destino.Numero} está bloqueado.");
        if (origem.Saldo <= 0)
            throw new RegraNegocioException($"O cartão {origem.Numero} não tem saldo para transferir.");
        if (destino.ClienteId != origem.ClienteId)
        {
            if (destino.ClienteId is not null)
                throw new RegraNegocioException($"O cartão {destino.Numero} pertence a outro cliente. O saldo só passa entre cartões do mesmo cliente.");
            if (destino.Saldo > 0)
                throw new RegraNegocioException($"O cartão {destino.Numero} tem saldo e não é deste cliente. Associe-o primeiro ao cliente.");
            destino.ClienteId = origem.ClienteId; // cartão novo, sem saldo: passa a ser do mesmo cliente
        }

        var valor = origem.Saldo;
        origem.Saldo = 0;
        db.MovimentosCartao.Add(Movimento(origem, TipoMovimentoCartao.TransferenciaSaida, -valor, $"Para o cartão {destino.Numero}", chave));
        destino.Saldo += valor;
        db.MovimentosCartao.Add(Movimento(destino, TipoMovimentoCartao.TransferenciaEntrada, valor, $"Do cartão {origem.Numero}", chave + "#2"));
        if (dados.BloquearOrigem)
            origem.Activo = false;
        auditoria.Registar("cartao.transferir", "CartaoEvento", origem.Id,
            new { Origem = origem.Numero, Destino = destino.Numero, Valor = valor, destino.ClienteId, OrigemBloqueada = dados.BloquearOrigem });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ObterAsync(destino.Id), true);
    }

    private void ExigirPermissaoCliente()
    {
        if (!actual.TemPermissao(Permissoes.CartoesCliente))
            throw new AcessoNegadoException("Não tem permissão para associar cartões a clientes nem transferir saldo.");
    }

    /// <summary>Quem associa tem a permissão e o cliente existe e está activo. Devolve o nome do cliente.</summary>
    private async Task<string> ValidarClienteAsync(int clienteId)
    {
        ExigirPermissaoCliente();
        var cliente = await db.Clientes.Where(c => c.Id == clienteId).Select(c => new { c.Nome, c.Activo }).FirstOrDefaultAsync()
                      ?? throw new NaoEncontradoException("Cliente não encontrado.");
        if (!cliente.Activo)
            throw new RegraNegocioException($"O cliente {cliente.Nome} está desactivado.");
        return cliente.Nome;
    }
}
