using System.Collections.Concurrent;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Sistema;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>Outros servidores LAMGest simulados (por endereço), em vez de pedidos reais pela VPN.</summary>
public sealed class LojasTeste : IConsultaLojas
{
    public ConcurrentDictionary<string, ListaLojas> Servidores { get; } = new();

    public Task<IdentidadeLoja> IdentidadeAsync(string endereco, CancellationToken ct) => Task.FromResult(Obter(endereco).Actual);

    public Task<ListaLojas> ListaAsync(string endereco, CancellationToken ct) => Task.FromResult(Obter(endereco));

    private ListaLojas Obter(string endereco) => Servidores.TryGetValue(endereco, out var l) ? l
        : throw new RegraNegocioException($"Não foi possível contactar a loja em {endereco}. Confirme o endereço e que a VPN está ligada nos dois servidores.");
}
