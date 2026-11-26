using System.Net;
using System.Net.Http.Json;

namespace LAMGestRestaurant.Tests.Infraestrutura;

public static class ExtensoesHttp
{
    public static Task<HttpResponseMessage> Post<T>(this HttpClient c, string url, T corpo) => c.PostAsJsonAsync(url, corpo, AplicacaoTeste.Json);
    public static Task<HttpResponseMessage> Put<T>(this HttpClient c, string url, T corpo) => c.PutAsJsonAsync(url, corpo, AplicacaoTeste.Json);
    public static async Task<T> Ler<T>(this HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(AplicacaoTeste.Json))!;
    public static async Task<T> Obter<T>(this HttpClient c, string url) => (await c.GetFromJsonAsync<T>(url, AplicacaoTeste.Json))!;

    /// <summary>Garante o código HTTP esperado, mostrando o corpo da resposta se falhar.</summary>
    public static async Task<HttpResponseMessage> Esperar(this Task<HttpResponseMessage> pedido, HttpStatusCode esperado)
    {
        var r = await pedido;
        if (r.StatusCode != esperado)
            Assert.Fail($"Esperado {(int)esperado}, obtido {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return r;
    }

    /// <summary>Prefixo + parte aleatória. Sem tamanho indicado, garante pelo menos 8 caracteres aleatórios
    /// (com prefixos longos, como «Pagamentos », cortar aos 12 deixava só 16 nomes possíveis e os testes colidiam).</summary>
    public static string Unico(string prefixo, int? tamanho = null) =>
        (prefixo + Guid.NewGuid().ToString("N"))[..(tamanho ?? Math.Max(12, prefixo.Length + 8))];
}
