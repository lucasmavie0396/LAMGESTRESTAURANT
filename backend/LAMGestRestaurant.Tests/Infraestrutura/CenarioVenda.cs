using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>Cenário de venda independente por teste: categoria, produtos, zona, 2 mesas, terminal e caixa aberto.</summary>
public class CenarioVenda
{
    public required HttpClient Gerente { get; init; }
    public required ProdutoDto Prato { get; init; }
    public required ProdutoDto Bebida { get; init; }
    public required MesaDto Mesa1 { get; init; }
    public required MesaDto Mesa2 { get; init; }
    public required SessaoCaixaDto Sessao { get; init; }
    public required Dictionary<string, int> Metodos { get; init; }

    public static async Task<CenarioVenda> CriarAsync(AplicacaoTeste app)
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var taxas = await gerente.Obter<List<TaxaImpostoDto>>("/api/catalogo/taxas");
        var iva = taxas.Single(t => t.Percentagem == 16m).Id;

        var cat = await (await gerente.Post("/api/categorias", new GuardarCategoria(Unico("Cat "), null, null, null, null, 0))
            .Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();
        var prato = await (await gerente.Post("/api/produtos", new GuardarProduto(Unico("V", 10), null, "Frango grelhado", null, cat.Id,
            TipoProduto.Restaurante, 450.50m, 180m, iva, null, true, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var bebida = await (await gerente.Post("/api/produtos", new GuardarProduto(Unico("B", 10), null, "Refrigerante", null, cat.Id,
            TipoProduto.Bar, 60m, 30m, iva, null, false, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

        var zona = await (await gerente.Post("/api/zonas", new GuardarZona(Unico("Zona "), 0)).Esperar(HttpStatusCode.Created)).Ler<ZonaDto>();
        var mesa1 = await (await gerente.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 4, null)).Esperar(HttpStatusCode.Created)).Ler<MesaDto>();
        var mesa2 = await (await gerente.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 4, null)).Esperar(HttpStatusCode.Created)).Ler<MesaDto>();

        var terminal = await (await gerente.Post("/api/terminais", new GuardarTerminal(Unico("T "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        var sessao = await (await gerente.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 1000m, null)).Esperar(HttpStatusCode.Created)).Ler<SessaoCaixaDto>();
        var metodos = (await gerente.Obter<List<MetodoPagamentoDto>>("/api/caixa/metodos-pagamento")).ToDictionary(m => m.Codigo, m => m.Id);

        return new CenarioVenda { Gerente = gerente, Prato = prato, Bebida = bebida, Mesa1 = mesa1, Mesa2 = mesa2, Sessao = sessao, Metodos = metodos };
    }

    public static string Chave() => Guid.NewGuid().ToString("N");

    public async Task<PedidoDto> AbrirMesaAsync(HttpClient c, MesaDto mesa, params (ProdutoDto produto, decimal qtd)[] itens)
    {
        var r = await c.PostComChave("/api/pedidos", Chave(), new NovoPedido(TipoPedido.Mesa, mesa.Id, null, null, 2, null, null, null, false,
            [.. itens.Select(i => new NovoItem(i.produto.Id, i.qtd, null))])).Esperar(HttpStatusCode.Created);
        return await r.Ler<PedidoDto>();
    }

    public Task<HttpResponseMessage> PagarAsync(HttpClient c, int pedidoId, string metodo, decimal valor, decimal? recebido = null, string? referencia = null, string? chave = null) =>
        c.PostComChave($"/api/pedidos/{pedidoId}/pagamentos", chave ?? Chave(),
            new RegistarPagamento(Sessao.Id, Metodos[metodo], valor, recebido ?? valor, referencia));
}

public static class ExtensoesIdempotencia
{
    public static Task<HttpResponseMessage> PostComChave<T>(this HttpClient c, string url, string chave, T corpo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = System.Net.Http.Json.JsonContent.Create(corpo, options: AplicacaoTeste.Json),
        };
        pedido.Headers.Add("Idempotency-Key", chave);
        return c.SendAsync(pedido);
    }
}
