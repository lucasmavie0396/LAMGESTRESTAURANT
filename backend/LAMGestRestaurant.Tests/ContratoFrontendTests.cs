using System.Text.RegularExpressions;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Os tipos do frontend (frontend/src/types/*.ts) são escritos à mão. Este teste compara-os com o contrato que a API
/// realmente publica (OpenAPI do Swagger): um campo que o ecrã espera e o servidor deixou de enviar (renomeado, removido)
/// falha aqui, em vez de aparecer vazio no ecrã do cliente.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public partial class ContratoFrontendTests(AplicacaoTeste app)
{
    /// <summary>
    /// Campos só do frontend (calculados no navegador ou de outra origem) e tipos com o mesmo nome mas outro significado.
    /// Cada excepção tem de ter o motivo.
    /// </summary>
    private static readonly Dictionary<string, string> Excepcoes = new()
    {
    };

    [GeneratedRegex(@"^export interface (\w+)\s*(?:extends\s+([\w,\s<>]+))?\{", RegexOptions.Multiline)]
    private static partial Regex Interface();

    [GeneratedRegex(@"^\s{2}(?:readonly\s+)?(\w+)\??\s*:")]
    private static partial Regex Campo();

    [Fact]
    public void Campos_que_o_frontend_espera_existem_na_api()
    {
        var esquemas = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1").Components.Schemas;
        var pasta = Path.Combine(AplicacaoTeste.RaizRepositorio, "frontend", "src", "types");
        var interfaces = Directory.GetFiles(pasta, "*.ts").SelectMany(f => LerInterfaces(File.ReadAllText(f), Path.GetFileName(f))).ToList();

        var comparadas = 0;
        var faltam = new List<string>();
        foreach (var (nome, campos, ficheiro) in interfaces)
        {
            if (!esquemas.TryGetValue(nome, out var esquema) || esquema.Properties.Count == 0)
                continue;
            comparadas++;
            var daApi = esquema.Properties.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var campo in campos.Where(c => !daApi.Contains(c) && !Excepcoes.ContainsKey($"{nome}.{c}")))
                faltam.Add($"{ficheiro}: {nome}.{campo} (a API envia: {string.Join(", ", daApi.Order())})");
        }

        // Garante que a comparação está mesmo a funcionar (os nomes dos tipos coincidem com os DTOs da API).
        Assert.True(comparadas >= 80, $"Só {comparadas} tipos do frontend foram comparados com a API.");
        Assert.Empty(faltam);
    }

    /// <summary>Interfaces exportadas e os nomes dos seus campos de primeiro nível (os tipos genéricos ficam de fora).</summary>
    internal static IEnumerable<(string Nome, List<string> Campos, string Ficheiro)> LerInterfaces(string texto, string ficheiro)
    {
        foreach (Match m in Interface().Matches(texto))
        {
            var nome = m.Groups[1].Value;
            var inicio = m.Index + m.Length;
            var profundidade = 1;
            var fim = inicio;
            while (fim < texto.Length && profundidade > 0)
            {
                if (texto[fim] == '{') profundidade++;
                else if (texto[fim] == '}') profundidade--;
                fim++;
            }
            var corpo = texto[inicio..(fim - 1)];
            var campos = new List<string>();
            var nivel = 0;
            foreach (var linha in corpo.Split('\n'))
            {
                // Vários campos na mesma linha («a: number; b: string»): partem-se pelo «;» fora de chaves.
                var partes = nivel == 0 ? linha.Split(';') : [linha];
                foreach (var parte in partes)
                {
                    if (nivel == 0 && Campo().Match("  " + parte.TrimStart()) is { Success: true } c)
                        campos.Add(c.Groups[1].Value);
                    nivel += parte.Count(ch => ch == '{') - parte.Count(ch => ch == '}');
                }
            }
            yield return (nome, campos, ficheiro);
        }
    }
}
