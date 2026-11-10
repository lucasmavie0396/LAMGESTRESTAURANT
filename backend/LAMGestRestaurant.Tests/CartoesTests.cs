using System.Drawing;
using System.Net;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Cartões Evento: registo e recargas pelo caixa, pagamento no POS, devoluções, relatórios e etiqueta com QR code.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class CartoesTests(AplicacaoTeste app)
{
    private const string Url = "/api/cartoes";

    private static Task<HttpResponseMessage> RegistarAsync(HttpClient c, string? numero, decimal valor, int? metodo, string? chave = null) =>
        c.PostComChave(Url, chave ?? CenarioVenda.Chave(), new RegistarCartao(numero, "Cliente E2E", valor, metodo));

    private static Task<HttpResponseMessage> PagarComCartaoAsync(CenarioVenda c, int pedidoId, decimal valor, string lido) =>
        c.Gerente.PostComChave($"/api/pedidos/{pedidoId}/pagamentos", CenarioVenda.Chave(),
            new RegistarPagamento(c.Sessao.Id, c.Metodos[ServicoCartoes.CodigoMetodo], valor, valor, lido));

    private static string Numero() => "9" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

    [Theory]
    [InlineData(";123456789012=2512101?", "123456789012")]                 // pista 2 do cartão magnético
    [InlineData("%B123456789012^SILVA/ANA^2512101?", "123456789012")]     // pista 1
    [InlineData(" ab-12 34 ", "AB-1234")]                                   // código de barras / QR escrito à mão
    public void Numero_lido_pelo_leitor_e_normalizado(string lido, string esperado) => Assert.Equal(esperado, ServicoCartoes.Normalizar(lido));

    [Fact]
    public async Task Registo_e_recarga_entram_no_caixa_e_nao_duplicam_com_a_mesma_chave()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var antes = await app.EsperadoAsync(c.Sessao.Id, dinheiro);

        var chave = CenarioVenda.Chave();
        var cartao = await (await RegistarAsync(c.Gerente, null, 500m, dinheiro, chave).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        Assert.Matches("^[1-9][0-9]{11}$", cartao.Numero); // gerado: 12 algarismos
        Assert.Equal(500m, cartao.Saldo);
        Assert.Equal(cartao.Id, (await (await RegistarAsync(c.Gerente, null, 500m, dinheiro, chave).Esperar(HttpStatusCode.OK)).Ler<CartaoDto>()).Id);
        Assert.Equal(antes + 500m, await app.EsperadoAsync(c.Sessao.Id, dinheiro));

        var recarga = CenarioVenda.Chave();
        var corpo = new RecarregarCartao(250.5m, c.Metodos["MPESA"]);
        Assert.Equal(750.5m, (await (await c.Gerente.PostComChave($"{Url}/{cartao.Id}/recargas", recarga, corpo).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>()).Saldo);
        Assert.Equal(750.5m, (await (await c.Gerente.PostComChave($"{Url}/{cartao.Id}/recargas", recarga, corpo).Esperar(HttpStatusCode.OK)).Ler<CartaoDto>()).Saldo);
        await c.Gerente.PostComChave($"{Url}/{cartao.Id}/recargas", CenarioVenda.Chave(), new RecarregarCartao(10m, c.Metodos[ServicoCartoes.CodigoMetodo]))
            .Esperar(HttpStatusCode.UnprocessableEntity); // não se recarrega com o próprio cartão
        await RegistarAsync(c.Gerente, cartao.Numero, 0m, null).Esperar(HttpStatusCode.Conflict);

        var extrato = await c.Gerente.Obter<Pagina<MovimentoCartaoDto>>($"{Url}/{cartao.Id}/movimentos");
        Assert.Equal([TipoMovimentoCartao.Recarga, TipoMovimentoCartao.Carregamento], extrato.Itens.Select(m => m.Tipo));
        Assert.Equal(["M-Pesa", "Dinheiro"], extrato.Itens.Select(m => m.Metodo));

        // Sem caixa aberto não há onde pôr o dinheiro; sem valor, regista-se na mesma.
        var (semCaixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        await RegistarAsync(semCaixa, Numero(), 100m, dinheiro).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(0m, (await (await RegistarAsync(semCaixa, Numero(), 0m, null).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>()).Saldo);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await RegistarAsync(garcom, Numero(), 0m, null)).StatusCode);
    }

    [Fact]
    public async Task Pagamento_abate_o_saldo_e_anulacao_ou_estorno_devolvem_ao_cartao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var numero = Numero();
        var cartao = await (await RegistarAsync(c.Gerente, $";{numero}=2512?", 1000m, c.Metodos["DINHEIRO"]).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        Assert.Equal(numero, cartao.Numero);
        var esperadoGaveta = await app.EsperadoAsync(c.Sessao.Id, c.Metodos["DINHEIRO"]);

        // Pagamento parcial com o cartão (lido pela pista 1) e anulação: o saldo volta.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        pedido = await (await PagarComCartaoAsync(c, pedido.Id, 100m, $"%B{numero}^CLIENTE^2512?").Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        var pagamento = Assert.Single(pedido.Pagamentos);
        Assert.Equal(900m, (await c.Gerente.Obter<CartaoDto>($"{Url}/{cartao.Id}")).Saldo);
        await c.Gerente.Post($"/api/pagamentos/{pagamento.Id}/anular", new { motivo = "Engano" }).Esperar(HttpStatusCode.OK);
        Assert.Equal(1000m, (await c.Gerente.Obter<CartaoDto>($"{Url}/{cartao.Id}")).Saldo);

        // Saldo insuficiente e cartão desconhecido: recusados, nada muda.
        await PagarComCartaoAsync(c, pedido.Id, 450.5m, "0000").Esperar(HttpStatusCode.UnprocessableEntity);
        var pequeno = await (await RegistarAsync(c.Gerente, Numero(), 10m, c.Metodos["DINHEIRO"]).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        await PagarComCartaoAsync(c, pedido.Id, 450.5m, pequeno.Numero).Esperar(HttpStatusCode.UnprocessableEntity);

        // Pagamento total: fecha o pedido; não entra dinheiro na gaveta.
        pedido = await (await PagarComCartaoAsync(c, pedido.Id, 450.5m, numero).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Pago, pedido.Estado);
        Assert.Equal(549.5m, (await c.Gerente.Obter<CartaoDto>($"{Url}/{cartao.Id}")).Saldo);
        Assert.Equal(esperadoGaveta + 10m, await app.EsperadoAsync(c.Sessao.Id, c.Metodos["DINHEIRO"])); // só o carregamento do cartão pequeno

        // Estorno para o cartão: devolve o que pagou.
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", new EstornarPedido(c.Sessao.Id, c.Metodos[ServicoCartoes.CodigoMetodo], "Prato devolvido", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK);
        Assert.Equal(1000m, (await c.Gerente.Obter<CartaoDto>($"{Url}/{cartao.Id}")).Saldo);

        // Pedido pago em parte em dinheiro: o reembolso não pode ir para o cartão.
        var misto = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 1));
        misto = await (await PagarComCartaoAsync(c, misto.Id, 30m, numero).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        misto = await (await c.PagarAsync(c.Gerente, misto.Id, "DINHEIRO", misto.EmDivida).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        await c.Gerente.Post($"/api/pedidos/{misto.Id}/estornar", new EstornarPedido(c.Sessao.Id, c.Metodos[ServicoCartoes.CodigoMetodo], "Devolução", misto.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        // Bloqueado não paga.
        var actual = await c.Gerente.Obter<CartaoDto>($"{Url}/{cartao.Id}");
        await c.Gerente.Post($"{Url}/{cartao.Id}/activo", new AlterarCartao(false, actual.RowVersion)).Esperar(HttpStatusCode.OK);
        var outro = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 1));
        await PagarComCartaoAsync(c, outro.Id, 10m, numero).Esperar(HttpStatusCode.UnprocessableEntity);

        var tipos = (await c.Gerente.Obter<Pagina<MovimentoCartaoDto>>($"{Url}/{cartao.Id}/movimentos?tamanho=50")).Itens.Select(m => m.Tipo).ToList();
        Assert.Equal([TipoMovimentoCartao.Pagamento, TipoMovimentoCartao.Estorno, TipoMovimentoCartao.Pagamento, TipoMovimentoCartao.AnulacaoPagamento,
            TipoMovimentoCartao.Pagamento, TipoMovimentoCartao.Carregamento], tipos);
    }

    [Fact]
    public async Task Relatorio_e_documento_dos_cartoes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var antes = await c.Gerente.Obter<RelatorioCartoes>($"/api/relatorios/cartoes?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}");
        var numero = Numero();
        await RegistarAsync(c.Gerente, numero, 300m, c.Metodos["DINHEIRO"]).Esperar(HttpStatusCode.Created);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 2));
        await PagarComCartaoAsync(c, pedido.Id, 120m, numero).Esperar(HttpStatusCode.Created);

        var r = await c.Gerente.Obter<RelatorioCartoes>($"/api/relatorios/cartoes?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}");
        Assert.Equal(antes.Resumo.Carregado + 300m, r.Resumo.Carregado);
        Assert.Equal(antes.Resumo.Pago + 120m, r.Resumo.Pago);
        Assert.Equal(antes.Resumo.SaldoEmCirculacao + 180m, r.Resumo.SaldoEmCirculacao);
        Assert.Contains(r.Saldos, s => s.Numero == numero && s.Saldo == 180m);
        Assert.Contains(r.Movimentos, m => m.Numero == numero && m.Tipo == TipoMovimentoCartao.Pagamento && m.Valor == -120m && m.Pedido == pedido.Numero);
        Assert.Contains(r.PorMetodo, m => m.Metodo == "Dinheiro");

        var documento = TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.CartoesEvento}&desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}"));
        Assert.Contains("SALDO EM CIRCULAÇÃO", documento);
        Assert.Contains(numero, documento);
    }

    [Fact]
    public async Task Etiqueta_do_cartao_tem_qr_code_e_codigo_de_barras_legiveis()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var c = await CenarioVenda.CriarAsync(app);
        var cartao = await (await RegistarAsync(c.Gerente, null, 0m, null).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        var png = await (await c.Gerente.GetAsync($"{Url}/{cartao.Id}/etiqueta").Esperar(HttpStatusCode.OK)).Content.ReadAsByteArrayAsync();
        using var imagem = new Bitmap(new MemoryStream(png));
        var cinzento = new byte[imagem.Width * imagem.Height];
        for (var y = 0; y < imagem.Height; y++)
            for (var x = 0; x < imagem.Width; x++)
                cinzento[y * imagem.Width + x] = (byte)(imagem.GetPixel(x, y).GetBrightness() * 255);
        BinaryBitmap Bitmap() => new(new HybridBinarizer(new RGBLuminanceSource(cinzento, imagem.Width, imagem.Height, RGBLuminanceSource.BitmapFormat.Gray8)));
        Assert.Equal(cartao.Numero, new QRCodeReader().decode(Bitmap())?.Text);
        Assert.Equal(cartao.Numero, new ZXing.OneD.Code128Reader().decode(Bitmap())?.Text);
    }
}
