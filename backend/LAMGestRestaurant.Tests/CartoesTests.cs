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
    public async Task Registo_e_recarga_imprimem_recibo_de_80mm_na_impressora_de_pagamentos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var impressora = await (await c.Gerente.Post("/api/configuracoes/impressoras",
            new GuardarImpressora(Unico("Pagamentos "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, c.Sessao.TerminalId)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            var numero = Numero();
            var cartao = await (await RegistarAsync(c.Gerente, numero, 500m, dinheiro).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
            await c.Gerente.Post($"{Url}/{cartao.Id}/recibo", new Api.Controllers.ImprimirReciboNoTerminal(null)).Esperar(HttpStatusCode.OK);
            var impresso = Assert.Single(await app.Impressoras.EsperarAsync(numero));
            Assert.Equal((ImpressorasTeste.Caixa, 80), (impresso.Impressora, impresso.LarguraPapelMm));
            var texto = TextoDocumento.De(impresso.Documento);
            foreach (var esperado in new[] { "RECIBO DE CARTÃO EVENTO", "Carregamento inicial", "500,00", "Dinheiro" })
                Assert.Contains(esperado, texto);

            await c.Gerente.PostComChave($"{Url}/{cartao.Id}/recargas", CenarioVenda.Chave(), new RecarregarCartao(120m, dinheiro)).Esperar(HttpStatusCode.Created);
            await c.Gerente.Post($"{Url}/{cartao.Id}/recibo", new Api.Controllers.ImprimirReciboNoTerminal(null)).Esperar(HttpStatusCode.OK);
            var recarga = (await app.Impressoras.EsperarAsync(numero, quantos: 2))[^1];
            Assert.Contains("Recarga", TextoDocumento.De(recarga.Documento));
            Assert.Contains("620,00", TextoDocumento.De(recarga.Documento));

            // No PDA o servidor só compõe o recibo; cartão sem carregamentos não tem recibo.
            await c.Gerente.Post($"{Url}/{cartao.Id}/recibo", new Api.Controllers.ImprimirReciboNoTerminal(null, true)).Esperar(HttpStatusCode.OK);
            Assert.Equal(2, app.Impressoras.Com(numero).Count);
            var semSaldo = await (await RegistarAsync(c.Gerente, null, 0m, null).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
            await c.Gerente.Post($"{Url}/{semSaldo.Id}/recibo", new Api.Controllers.ImprimirReciboNoTerminal(null)).Esperar(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await c.Gerente.Post($"/api/configuracoes/impressoras/{impressora.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Descarga_mostra_saldo_e_acumulado_devolve_do_caixa_e_nao_duplica()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        async Task<CartaoDto> NovoAsync(decimal valor) => await (await RegistarAsync(c.Gerente, Numero(), valor, dinheiro).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        var (a, b, d) = (await NovoAsync(300m), await NovoAsync(200.5m), await NovoAsync(50m));
        var vazio = await NovoAsync(0m);

        // Saldos: de um cartão, de vários e de todos (cartão sem saldo não aparece); acumulado linha a linha.
        var saldos = await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos?id={a.Id}&id={b.Id}&id={vazio.Id}");
        Assert.Equal(500.5m, saldos.Total);
        Assert.Equal(2, saldos.Cartoes.Count);
        Assert.Equal(saldos.Cartoes[0].Saldo, saldos.Cartoes[0].Acumulado);
        Assert.Equal(500.5m, saldos.Cartoes[1].Acumulado);
        Assert.Equal(300m, Assert.Single((await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos?id={a.Id}")).Cartoes).Saldo);
        Assert.Contains((await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos")).Cartoes, x => x.Id == d.Id);

        NovaDescarga Corpo(bool devolver = true, decimal? saldoA = null) => new([new ItemDescarga(a.Id, saldoA ?? 300m), new ItemDescarga(b.Id, 200.5m)], devolver, devolver ? dinheiro : null, null, "Fim do evento");
        await c.Gerente.PostComChave($"{Url}/descargas", CenarioVenda.Chave(), Corpo(saldoA: 299m)).Esperar(HttpStatusCode.Conflict); // saldo mudou
        await c.Gerente.PostComChave($"{Url}/descargas", CenarioVenda.Chave(),
            new NovaDescarga([new ItemDescarga(a.Id, 300m)], true, c.Metodos[ServicoCartoes.CodigoMetodo], null, "Fim")).Esperar(HttpStatusCode.UnprocessableEntity);
        await (await app.ClienteComPermissoesAsync(Permissoes.CartoesVer)).GetAsync($"{Url}/descargas/saldos").Esperar(HttpStatusCode.Forbidden);

        // Devolução: sai do caixa, saldos a zero; o reenvio com a mesma chave não repete.
        var antes = await app.EsperadoAsync(c.Sessao.Id, dinheiro);
        var chave = CenarioVenda.Chave();
        var feita = await (await c.Gerente.PostComChave($"{Url}/descargas", chave, Corpo()).Esperar(HttpStatusCode.Created)).Ler<DescargaDto>();
        Assert.Equal((500.5m, true, c.Sessao.Id, 500.5m), (feita.Total, feita.DevolveuDinheiro, feita.SessaoCaixaId, feita.Cartoes[^1].Acumulado));
        Assert.Equal(feita.Id, (await (await c.Gerente.PostComChave($"{Url}/descargas", chave, Corpo()).Esperar(HttpStatusCode.OK)).Ler<DescargaDto>()).Id);
        Assert.Equal(antes - 500.5m, await app.EsperadoAsync(c.Sessao.Id, dinheiro));
        Assert.Equal(0m, (await c.Gerente.Obter<CartaoDto>($"{Url}/{a.Id}")).Saldo);
        var movimento = (await c.Gerente.Obter<ExtratoCartaoDto>($"{Url}/{a.Id}/movimentos")).Itens[0];
        Assert.Equal((TipoMovimentoCartao.Descarga, -300m, 0m), (movimento.Tipo, movimento.Valor, movimento.SaldoApos));
        await c.Gerente.PostComChave($"{Url}/descargas", CenarioVenda.Chave(), Corpo()).Esperar(HttpStatusCode.Conflict); // já sem saldo

        // Só retirar: não mexe no caixa.
        var semCaixa = await (await c.Gerente.PostComChave($"{Url}/descargas", CenarioVenda.Chave(),
            new NovaDescarga([new ItemDescarga(d.Id, 50m)], false, null, null, "Cartão esquecido")).Esperar(HttpStatusCode.Created)).Ler<DescargaDto>();
        Assert.False(semCaixa.DevolveuDinheiro);
        Assert.Equal(antes - 500.5m, await app.EsperadoAsync(c.Sessao.Id, dinheiro));

        // Relatório por caixa (só a devolução) e por datas (inclui a que só retirou); documento imprimível.
        var porCaixa = await c.Gerente.Obter<RelatorioDescargas>($"/api/relatorios/cartoes/descargas?sessaoCaixaId={c.Sessao.Id}");
        Assert.Equal((500.5m, 500.5m, 0m, 2, 1), (porCaixa.Total, porCaixa.Devolvido, porCaixa.Retirado, porCaixa.Cartoes, porCaixa.Operacoes));
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));
        var dia = $"desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";
        Assert.Contains((await c.Gerente.Obter<RelatorioDescargas>($"/api/relatorios/cartoes/descargas?{dia}")).Linhas, l => l.Numero == d.Numero && !l.DevolveuDinheiro);
        var documento = TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.DescargasCartoes}&{dia}&sessaoId={c.Sessao.Id}"));
        foreach (var esperado in new[] { "DESCARGAS DE CARTÕES", a.Numero, b.Numero, "TOTAL DESCARREGADO (2)" })
            Assert.Contains(esperado, documento);
        var resumo = await c.Gerente.Obter<RelatorioCartoes>($"/api/relatorios/cartoes?sessaoCaixaId={c.Sessao.Id}");
        Assert.Equal((500.5m, 2), (resumo.Resumo.Descarregado, resumo.Resumo.Descargas));
        Assert.Equal(550.5m, resumo.PorMetodo.Single(m => m.Metodo == "Dinheiro").Valor); // recebido: a devolução não entra aqui
    }

    [Fact]
    public async Task Etiqueta_do_cartao_leva_o_codigo_escolhido_codigo_de_barras_ou_qr_code()
    {
        // Composição: só um dos códigos; com QR code o número vai em texto (o código de barras já o tem por baixo).
        var barras = ServicoCartoes.Etiqueta("2001", "Ana", CodigoEtiquetaCartao.CodigoBarras);
        Assert.Equal(("2001", (string?)null), (barras.CodigoBarras, barras.QrCode));
        Assert.DoesNotContain(barras.Linhas, l => l.Texto == "2001");
        var qr = ServicoCartoes.Etiqueta("2001", "Ana", CodigoEtiquetaCartao.QrCode);
        Assert.Equal(((string?)null, "2001"), (qr.CodigoBarras, qr.QrCode));
        Assert.Contains(qr.Linhas, l => l.Texto == "2001");

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var c = await CenarioVenda.CriarAsync(app);
        var cartao = await (await RegistarAsync(c.Gerente, null, 0m, null).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        async Task<BinaryBitmap> ImagemAsync(string codigo)
        {
            var png = await (await c.Gerente.GetAsync($"{Url}/{cartao.Id}/etiqueta?codigo={codigo}").Esperar(HttpStatusCode.OK)).Content.ReadAsByteArrayAsync();
            using var imagem = new Bitmap(new MemoryStream(png));
            var cinzento = new byte[imagem.Width * imagem.Height];
            for (var y = 0; y < imagem.Height; y++)
                for (var x = 0; x < imagem.Width; x++)
                    cinzento[y * imagem.Width + x] = (byte)(imagem.GetPixel(x, y).GetBrightness() * 255);
            return new(new HybridBinarizer(new RGBLuminanceSource(cinzento, imagem.Width, imagem.Height, RGBLuminanceSource.BitmapFormat.Gray8)));
        }
        Assert.Equal(cartao.Numero, new QRCodeReader().decode(await ImagemAsync("QrCode"))?.Text);
        Assert.Null(new ZXing.OneD.Code128Reader().decode(await ImagemAsync("QrCode")));
        Assert.Equal(cartao.Numero, new ZXing.OneD.Code128Reader().decode(await ImagemAsync("CodigoBarras"))?.Text);
        Assert.Null(new QRCodeReader().decode(await ImagemAsync("CodigoBarras")));
    }
}
