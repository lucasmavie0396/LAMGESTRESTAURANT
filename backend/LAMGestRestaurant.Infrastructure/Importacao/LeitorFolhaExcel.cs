using System.Globalization;
using ClosedXML.Excel;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;

namespace LAMGestRestaurant.Infrastructure.Importacao;

/// <summary>Lê uma folha de um .xlsx pelos títulos da linha 1 (a ordem das colunas não interessa; colunas a mais são ignoradas).</summary>
public sealed class LeitorFolhaExcel : ILeitorFolhaExcel
{
    public IReadOnlyList<LinhaFolha> Ler(Stream ficheiro, string folha, IReadOnlyList<string> titulos, int maximoLinhas)
    {
        XLWorkbook livro;
        try
        {
            // O ClosedXML precisa de um ficheiro em que possa saltar de posição (o do pedido HTTP não deixa).
            var copia = new MemoryStream();
            ficheiro.CopyTo(copia);
            copia.Position = 0;
            livro = new XLWorkbook(copia);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RegraNegocioException("Não foi possível ler o ficheiro. Use o modelo em formato Excel (.xlsx).");
        }

        using (livro)
        {
            if (!livro.TryGetWorksheet(folha, out var folhaExcel))
                throw new RegraNegocioException($"O ficheiro não tem a folha «{folha}». Use o modelo de importação.");

            var colunas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var celula in folhaExcel.Row(1).CellsUsed())
                colunas.TryAdd(Texto(celula), celula.Address.ColumnNumber);
            var emFalta = titulos.Where(t => !colunas.ContainsKey(t)).ToList();
            if (emFalta.Count > 0)
                throw new RegraNegocioException($"Faltam colunas na folha «{folha}»: {string.Join(", ", emFalta)}. Use o modelo de importação sem alterar os títulos.");

            var linhas = new List<LinhaFolha>();
            var ultima = folhaExcel.LastRowUsed()?.RowNumber() ?? 1;
            for (var n = 2; n <= ultima; n++)
            {
                var valores = titulos.ToDictionary(t => t, t => Texto(folhaExcel.Cell(n, colunas[t])), StringComparer.OrdinalIgnoreCase);
                if (valores.Values.All(v => v.Length == 0))
                    continue;
                if (linhas.Count == maximoLinhas)
                    throw new RegraNegocioException($"O ficheiro tem mais de {maximoLinhas} produtos. Divida-o em vários ficheiros.");
                linhas.Add(new LinhaFolha(n, valores));
            }
            return linhas;
        }
    }

    /// <summary>Valor da célula como texto; números sem separadores e com ponto decimal (independente da cultura do servidor).</summary>
    private static string Texto(IXLCell celula)
    {
        var valor = celula.CachedValue;
        if (valor.IsText)
            return valor.GetText().Trim();
        if (valor.IsNumber)
        {
            var numero = valor.GetNumber();
            return Math.Abs(numero) < 7.9e27
                ? ((decimal)numero).ToString("0.############", CultureInfo.InvariantCulture)
                : numero.ToString("R", CultureInfo.InvariantCulture);
        }
        // Datas, verdadeiro/falso e erros de fórmula não são valores válidos: seguem como texto para a validação os recusar.
        return valor.IsBlank ? "" : valor.ToString(CultureInfo.InvariantCulture).Trim();
    }
}
