using System.Security.Claims;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LAMGestRestaurant.Infrastructure.Dados;

/// <summary>
/// Dados de referência mínimos e idempotentes (perfis, permissões, métodos de pagamento, unidades,
/// armazém principal, configurações por omissão). Não insere dados de negócio (produtos, mesas, clientes).
/// Só acrescenta o que falta; nunca altera valores que o utilizador já configurou.
/// </summary>
public class SemeadorDados(AppDbContext db, RoleManager<Perfil> perfis, ILogger<SemeadorDados> log)
{
    public async Task ExecutarAsync(CancellationToken ct = default)
    {
        await SemearPerfisAsync(ct);
        await SemearMetodosPagamentoAsync(ct);
        await SemearUnidadesAsync(ct);
        await SemearSeNaoExiste(db.Armazens, new Armazem { Nome = "Armazém principal", Principal = true }, a => a.Principal, ct);
        await SemearTaxasAsync(ct);
        await SemearConfiguracoesAsync(ct);
        foreach (var nome in new[] { "Água e electricidade", "Gás", "Transporte", "Manutenção", "Limpeza", "Diversos" })
            await SemearSeNaoExiste(db.CategoriasDespesa, new Domain.Pessoal.CategoriaDespesa { Nome = nome }, c => c.Nome == nome, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task SemearPerfisAsync(CancellationToken ct)
    {
        // Permissões que os perfis padrão já receberam numa versão anterior. Sem registo (bases anteriores a ele):
        // todas menos as criadas depois da primeira versão, que nunca chegavam aos perfis já existentes.
        var registo = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == ChavesConfiguracao.PermissoesDistribuidas, ct);
        var distribuidas = registo is null
            ? Permissoes.Todas.Keys.Except(PerfisPadrao.CriadasDepoisDaPrimeiraVersao).ToHashSet()
            : registo.Valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var acrescentadas = new Dictionary<string, List<string>>();

        foreach (var (nome, permissoes) in PerfisPadrao.PermissoesIniciais)
        {
            var perfil = await perfis.FindByNameAsync(nome);
            var novo = perfil is null;
            if (novo)
            {
                perfil = new Perfil(nome) { Sistema = true, Descricao = $"Perfil inicial {nome}" };
                Verificar(await perfis.CreateAsync(perfil));
                log.LogInformation("Perfil criado: {Perfil}", nome);
            }

            // Perfis novos recebem as permissões iniciais e o Administrador é sempre sincronizado com o catálogo completo.
            // Nos outros perfis padrão já existentes só entram as permissões novas desta versão que o perfil tem por
            // omissão: o que o administrador tirou ou deu nas outras mantém-se.
            var aDar = novo || nome == PerfisPadrao.Administrador ? permissoes : permissoes.Where(p => !distribuidas.Contains(p)).ToArray();
            var actuais = (await perfis.GetClaimsAsync(perfil!))
                .Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).ToHashSet();
            foreach (var permissao in aDar.Where(p => !actuais.Contains(p)))
            {
                Verificar(await perfis.AddClaimAsync(perfil!, new Claim(Permissoes.ClaimType, permissao)));
                if (!novo && nome != PerfisPadrao.Administrador)
                    (acrescentadas.TryGetValue(nome, out var lista) ? lista : acrescentadas[nome] = []).Add(permissao);
            }
        }

        var todas = string.Join(',', Permissoes.Todas.Keys.Order(StringComparer.Ordinal));
        if (registo is null)
            db.Configuracoes.Add(new Configuracao
            {
                Chave = ChavesConfiguracao.PermissoesDistribuidas, Valor = todas,
                Descricao = "Permissões já distribuídas pelos perfis padrão (gerido pelo sistema)",
            });
        else
            registo.Valor = todas;

        if (acrescentadas.Count > 0)
        {
            log.LogInformation("Permissões novas dadas aos perfis padrão: {Permissoes}",
                string.Join("; ", acrescentadas.Select(p => $"{p.Key}: {string.Join(", ", p.Value)}")));
            db.RegistosAuditoria.Add(new RegistoAuditoria
            {
                DataUtc = DateTime.UtcNow, NomeUtilizador = "Sistema (actualização)", Accao = "perfis.permissoes-novas", Entidade = "Perfil",
                Detalhes = System.Text.Json.JsonSerializer.Serialize(acrescentadas),
            });
        }
    }

    private async Task SemearMetodosPagamentoAsync(CancellationToken ct)
    {
        MetodoPagamento[] metodos =
        [
            new() { Codigo = "DINHEIRO", Nome = "Dinheiro", PermiteTroco = true, ContaNaGaveta = true, Ordem = 1 },
            new() { Codigo = "CARTAO", Nome = "Cartão", ExigeReferencia = false, Ordem = 2 },
            new() { Codigo = "MPESA", Nome = "M-Pesa", ExigeReferencia = true, Ordem = 3 },
            new() { Codigo = "EMOLA", Nome = "e-Mola", ExigeReferencia = true, Ordem = 4 },
            new() { Codigo = "TRANSF", Nome = "Transferência", ExigeReferencia = true, Ordem = 5 },
            new() { Codigo = "CHEQUE", Nome = "Cheque", ExigeReferencia = true, Ordem = 6 },
            new() { Codigo = "OUTRO", Nome = "Outro", Ordem = 7 },
            // Pré-pago: abate o saldo do cartão lido (o número fica como referência); não conta na gaveta.
            new() { Codigo = Application.Cartoes.ServicoCartoes.CodigoMetodo, Nome = "Cartão Evento", Ordem = 8 },
            // A crédito: não entra dinheiro; o pedido fecha com uma factura em dívida na conta corrente do cliente.
            new() { Codigo = Application.Facturacao.ServicoDocumentos.CodigoFacturaCredito, Nome = "Fatura a Crédito", Ordem = 9 },
        ];
        foreach (var m in metodos)
            await SemearSeNaoExiste(db.MetodosPagamento, m, x => x.Codigo == m.Codigo, ct);
    }

    private async Task SemearUnidadesAsync(CancellationToken ct)
    {
        UnidadeMedida[] unidades =
        [
            new() { Codigo = "un", Nome = "Unidade", Dimensao = DimensaoUnidade.Unidade, FactorParaBase = 1m },
            new() { Codigo = "kg", Nome = "Quilograma", Dimensao = DimensaoUnidade.Massa, FactorParaBase = 1m },
            new() { Codigo = "g", Nome = "Grama", Dimensao = DimensaoUnidade.Massa, FactorParaBase = 0.001m },
            new() { Codigo = "l", Nome = "Litro", Dimensao = DimensaoUnidade.Volume, FactorParaBase = 1m },
            new() { Codigo = "ml", Nome = "Mililitro", Dimensao = DimensaoUnidade.Volume, FactorParaBase = 0.001m },
        ];
        foreach (var u in unidades)
            await SemearSeNaoExiste(db.UnidadesMedida, u, x => x.Codigo == u.Codigo, ct);
    }

    private async Task SemearTaxasAsync(CancellationToken ct)
    {
        // Taxas de partida, editáveis em Configurações. Confirmar a taxa de IVA em vigor com o contabilista.
        await SemearSeNaoExiste(db.TaxasImposto, new TaxaImposto { Nome = "IVA 16%", Percentagem = 16m }, t => t.Nome == "IVA 16%", ct);
        await SemearSeNaoExiste(db.TaxasImposto, new TaxaImposto { Nome = "Isento", Percentagem = 0m }, t => t.Nome == "Isento", ct);
    }

    private async Task SemearConfiguracoesAsync(CancellationToken ct)
    {
        (string chave, string valor, string descricao)[] configs =
        [
            (ChavesConfiguracao.NomeEstabelecimento, "", "Nome do estabelecimento"),
            (ChavesConfiguracao.Endereco, "", "Endereço"),
            (ChavesConfiguracao.Telefone, "", "Contactos"),
            (ChavesConfiguracao.Nuit, "", "NUIT do estabelecimento"),
            (ChavesConfiguracao.LogotipoUrl, "", "Logótipo (URL relativo)"),
            (ChavesConfiguracao.Moeda, "MZN", "Código ISO da moeda"),
            (ChavesConfiguracao.SimboloMoeda, "MT", "Símbolo da moeda"),
            (ChavesConfiguracao.FusoHorario, "Africa/Maputo", "Fuso horário (IANA)"),
            (ChavesConfiguracao.TaxaEntregaPadrao, "0", "Taxa de entrega por omissão"),
            (ChavesConfiguracao.PermitirStockNegativo, "false", "Permitir stock negativo (global)"),
            (ChavesConfiguracao.FidelizacaoActiva, "false", "Programa de fidelização activo"),
            (ChavesConfiguracao.PontosPorMoeda, "100", "Valor gasto para ganhar 1 ponto"),
            (ChavesConfiguracao.RodapeRecibo, "Obrigado pela preferência.", "Texto no rodapé da venda a dinheiro e do recibo"),
            (ChavesConfiguracao.ImprimirReciboAoPagar, "true", "Imprimir o recibo automaticamente na impressora do caixa ao fechar o pedido"),
            (ChavesConfiguracao.MotivoIsencao, "Isento nos termos do artigo 9 do Código do IVA", "Motivo legal impresso nas linhas com IVA a 0 %"),
            (ChavesConfiguracao.PrazoFacturaDias, "30", "Prazo de pagamento das facturas (dias)"),
            (ChavesConfiguracao.ValidadeCotacaoDias, "15", "Validade das cotações (dias)"),
            (ChavesConfiguracao.FechoImprimeProdutos, "true", "O fecho de caixa imprime os produtos vendidos"),
            (ChavesConfiguracao.FechoContagemCega, "false", "O fecho de caixa imprime só o valor contado"),
            (ChavesConfiguracao.FechoAutomatico, "true", "Imprimir o fecho de caixa e o fecho do dia ao confirmar"),
            (ChavesConfiguracao.ExigirDiaAberto, "true", "Os caixas só abrem depois de «Abrir Dia»"),
            (ChavesConfiguracao.FecharDiaComContasAbertas, "true", "«Fechar Dia» com contas abertas (transitam para o dia seguinte)"),
            (ChavesConfiguracao.Logotipo80mm, "true", "Os documentos de 80 mm imprimem o logótipo"),
            (ChavesConfiguracao.ContaDadosFactura, "true", "A conta imprime os dados para factura (nome comercial e NUIT, a preencher pelo cliente)"),
            (ChavesConfiguracao.PublicidadeProvedor, "true", "A conta e o recibo (58 e 80 mm) imprimem o provedor do sistema e os contactos"),
            (ChavesConfiguracao.VisorBoasVindas, "Bem-vindo!", "Visor do cliente: mensagem quando não há venda"),
            (ChavesConfiguracao.VisorAgradecimento, "Obrigado, volte sempre!", "Visor do cliente: mensagem depois de pago"),
            (ChavesConfiguracao.BalancaEtiquetas, "false", "Ler no POS as etiquetas da balança (código de barras com peso ou preço)"),
            (ChavesConfiguracao.BalancaPrefixos, "2", "Etiquetas da balança: prefixos do código (separados por vírgulas; 2 = de 20 a 29)"),
            (ChavesConfiguracao.BalancaDigitosCodigo, "5", "Etiquetas da balança: algarismos do código do produto (4 a 6)"),
            (ChavesConfiguracao.BalancaConteudo, "peso", "Etiquetas da balança: o código leva o peso (gramas) ou o preço"),
            (ChavesConfiguracao.BalancaDecimaisPreco, "0", "Etiquetas da balança: casas decimais do preço (0 a 2)"),
            (ChavesConfiguracao.CopiasTalao, "1", "Cópias do talão de pedido (cozinha/bar)"),
            (ChavesConfiguracao.CopiasAnulacao, "1", "Cópias do talão de anulação"),
            (ChavesConfiguracao.CopiasConta, "1", "Cópias da conta"),
            (ChavesConfiguracao.CopiasVendaDinheiro, "1", "Cópias da venda a dinheiro"),
            (ChavesConfiguracao.CopiasFactura, "1", "Cópias da factura"),
            (ChavesConfiguracao.CopiasRecibo, "1", "Cópias do recibo"),
            (ChavesConfiguracao.CopiasCotacao, "1", "Cópias da cotação"),
            (ChavesConfiguracao.CopiasNotaCredito, "1", "Cópias da nota de crédito"),
            (ChavesConfiguracao.CopiasFecho, "1", "Cópias do fecho de caixa"),
            (ChavesConfiguracao.CopiasRelatorios, "1", "Cópias dos relatórios"),
            (ChavesConfiguracao.EtiquetaLargura, "50", "Largura da etiqueta de produto (mm)"),
            (ChavesConfiguracao.EtiquetaAltura, "30", "Altura da etiqueta de produto (mm)"),
            (ChavesConfiguracao.EtiquetaCampos, "nome,preco,codigo-barras", "Dados do produto na etiqueta"),
            (ChavesConfiguracao.PosMesa, "true", "Botão Mesa no POS"),
            (ChavesConfiguracao.PosTakeAway, "true", "Botão Take Away no POS"),
            (ChavesConfiguracao.PosDelivery, "true", "Botão Delivery no POS"),
            (ChavesConfiguracao.PosBalcao, "true", "Venda rápida ao Balcão no POS"),
            (ChavesConfiguracao.PosCodigoBarras, "true", "Leitor de código de barras no POS"),
            (ChavesConfiguracao.PosMostrarStock, "false", "Mostrar o stock nos botões dos produtos do POS"),
            (ChavesConfiguracao.EnderecoExternoTablets, "", "Endereço do servidor para os tablets fora do restaurante (VPN)"),
            (ChavesConfiguracao.LojaIdentificador, Guid.NewGuid().ToString("N"), "Identificador único deste servidor (não alterar)"),
            (ChavesConfiguracao.LojaCor, "#2563eb", "Cor desta loja no topo do ecrã e no seletor de lojas"),
            (ChavesConfiguracao.PosSugestoesComentario, "Sem cebola\nSem picante\nBem passado\nMal passado\nSem gelo\nPara levar", "Sugestões dos comentários dos produtos no POS (uma por linha)"),
        ];
        foreach (var (chave, valor, descricao) in configs)
            await SemearSeNaoExiste(db.Configuracoes, new Configuracao { Chave = chave, Valor = valor, Descricao = descricao }, c => c.Chave == chave, ct);
    }

    private static async Task SemearSeNaoExiste<T>(DbSet<T> conjunto, T entidade, System.Linq.Expressions.Expression<Func<T, bool>> existe, CancellationToken ct)
        where T : class
    {
        if (!await conjunto.AnyAsync(existe, ct) && !conjunto.Local.AsQueryable().Any(existe))
            conjunto.Add(entidade);
    }

    private static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(string.Join("; ", resultado.Errors.Select(e => e.Description)));
    }
}
