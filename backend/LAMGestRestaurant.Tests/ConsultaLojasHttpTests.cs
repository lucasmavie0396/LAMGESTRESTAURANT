using System.Net;
using System.Text;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Infrastructure.Lojas;

namespace LAMGestRestaurant.Tests;

/// <summary>Consulta a outro servidor pela VPN: lê a identidade e explica os erros (sem ligação, servidor antigo, não é LAMGest).</summary>
public class ConsultaLojasHttpTests
{
    private sealed class Resposta(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) => Task.FromResult(f(pedido));
    }

    private static ConsultaLojasHttp Com(Func<HttpRequestMessage, HttpResponseMessage> f) => new(new HttpClient(new Resposta(f)));

    [Fact]
    public async Task Le_a_identidade_da_outra_loja()
    {
        string? pedido = null;
        var c = Com(p =>
        {
            pedido = p.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"identificador":"abc","nome":"Loja Baixa","nuit":"400000001","cor":"#16a34a"}""", Encoding.UTF8, "application/json"),
            };
        });
        var id = await c.IdentidadeAsync("http://100.70.1.11:8080", default);
        Assert.Equal(("abc", "Loja Baixa", "http://100.70.1.11:8080/api/loja"), (id.Identificador, id.Nome, pedido));
    }

    [Fact]
    public async Task Erros_explicam_o_que_verificar()
    {
        var semLigacao = await Assert.ThrowsAsync<RegraNegocioException>(() => Com(_ => throw new HttpRequestException("x")).IdentidadeAsync("http://100.70.1.99:8080", default));
        Assert.Contains("VPN", semLigacao.Message);
        var antigo = await Assert.ThrowsAsync<RegraNegocioException>(() => Com(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).IdentidadeAsync("http://100.70.1.11:8080", default));
        Assert.Contains("Setup mais recente", antigo.Message);
        var outro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
            Com(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>router</html>", Encoding.UTF8, "text/html") }).IdentidadeAsync("http://100.70.1.11", default));
        Assert.Contains("não é um servidor LAMGest", outro.Message);
    }
}
