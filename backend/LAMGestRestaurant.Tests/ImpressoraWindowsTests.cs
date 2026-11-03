using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Impressao;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Desenho real pelo spooler do Windows, para PDF («Microsoft Print to PDF», sem diálogo).
/// Sem essa impressora o teste não faz nada. Com LAMGEST_PDF_SAIDA definida, os PDFs ficam nessa pasta para inspecção visual.
/// </summary>
public class ImpressoraWindowsTests
{
    private const string Pdf = "Microsoft Print to PDF";

    [Fact]
    public void Recibo_talao_e_teste_sao_desenhados_em_80mm_58mm_e_A4()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var impressora = new ImpressoraWindows();
        if (!impressora.Instaladas().Contains(Pdf))
            return;

        var pasta = Environment.GetEnvironmentVariable("LAMGEST_PDF_SAIDA") is { Length: > 0 } p ? p : Path.Combine(Path.GetTempPath(), "lamgest-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var recibo = new ReciboDto("REC-2026-000123", "PED-2026-000456", DateTime.UtcNow, "Restaurante Exemplo", "Av. Julius Nyerere, 1234, Maputo",
                "84 000 0000", "123456789", "12", TipoPedido.Mesa, "Ana Caixa",
                [
                    new ReciboLinha("Frango grelhado com batata frita e salada da casa", 2, 450.50m, 901.00m),
                    new ReciboLinha("Refrigerante 330 ml", 3, 60m, 180m),
                    new ReciboLinha("Café", 1, 35m, 35m),
                ],
                1116m, 0, 153.93m, 0, 1116m,
                [new PagamentoDto(1, "Dinheiro", 1116m, 1200m, 84m, null, EstadoPagamento.Valido, DateTime.UtcNow, "Ana", null)], "MT",
                "Documento interno - não serve de factura. Obrigado e volte sempre!");
            var talao = new DadosTalao("Cozinha", "PED-2026-000456", TipoPedido.Mesa, "12", "Carlos", null, DateTime.Now, "Cliente com pressa",
                [new ItemTalao(2, "Frango grelhado", "sem sal, bem passado"), new ItemTalao(1, "Prego no pão", null)]);

            var ficheiros = new (string Nome, int Largura, DocumentoImpressao Documento)[]
            {
                ("recibo-80mm.pdf", 80, ComposicaoImpressao.Recibo(recibo, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"))),
                ("talao-58mm.pdf", 58, ComposicaoImpressao.Talao(talao)),
                ("teste-a4.pdf", 210, ComposicaoImpressao.Teste("Caixa 1", Pdf, 210, DateTime.Now)),
            };
            foreach (var (nome, largura, documento) in ficheiros)
            {
                var caminho = Path.Combine(pasta, nome);
                impressora.ImprimirParaFicheiro(Pdf, largura, documento, caminho);
                Assert.True(new FileInfo(caminho) is { Exists: true, Length: > 1000 }, caminho);
            }
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("LAMGEST_PDF_SAIDA") is not { Length: > 0 })
                Directory.Delete(pasta, recursive: true);
        }
    }
}
