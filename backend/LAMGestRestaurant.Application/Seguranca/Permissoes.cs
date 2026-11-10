namespace LAMGestRestaurant.Application.Seguranca;

/// <summary>
/// Catálogo de permissões no formato modulo.operacao. São guardadas como claims "perm" dos perfis
/// e verificadas no backend com [Authorize(Policy = "perm:modulo.operacao")].
/// </summary>
public static class Permissoes
{
    public const string ClaimType = "perm";
    public const string PrefixoPolicy = "perm:";

    public const string DashboardVer = "dashboard.ver";

    public const string UtilizadoresGerir = "utilizadores.gerir";
    public const string PerfisGerir = "perfis.gerir";

    public const string ProdutosVer = "produtos.ver";
    public const string ProdutosGerir = "produtos.gerir";
    public const string ProdutosAlterarPreco = "produtos.alterar-preco";

    public const string PosUsar = "pos.usar";
    public const string PosDesconto = "pos.desconto";
    public const string PosCancelarItem = "pos.cancelar-item";
    public const string PosCancelarPedido = "pos.cancelar-pedido";
    public const string PosTransferirMesa = "pos.transferir-mesa";

    public const string MesasVer = "mesas.ver";
    public const string MesasGerir = "mesas.gerir";

    public const string CozinhaVer = "cozinha.ver";
    public const string CozinhaActualizar = "cozinha.actualizar";

    public const string PagamentosRegistar = "pagamentos.registar";
    public const string PagamentosAnular = "pagamentos.anular";

    public const string CaixaOperar = "caixa.operar";
    public const string CaixaMovimentos = "caixa.movimentos";
    public const string CaixaVerTodas = "caixa.ver-todas";

    public const string StockVer = "stock.ver";
    public const string StockGerir = "stock.gerir";
    public const string StockAjustar = "stock.ajustar";
    public const string ReceitasGerir = "receitas.gerir";

    public const string ComprasVer = "compras.ver";
    public const string ComprasGerir = "compras.gerir";
    public const string ComprasAnular = "compras.anular";

    public const string ClientesVer = "clientes.ver";
    public const string ClientesGerir = "clientes.gerir";

    public const string ReservasGerir = "reservas.gerir";
    public const string DeliveryGerir = "delivery.gerir";

    public const string DespesasRegistar = "despesas.registar";
    public const string DespesasAnular = "despesas.anular";

    public const string FuncionariosVer = "funcionarios.ver";
    public const string FuncionariosGerir = "funcionarios.gerir";
    public const string FuncionariosDadosPessoais = "funcionarios.dados-pessoais";

    public const string CartoesVer = "cartoes.ver";
    public const string CartoesGerir = "cartoes.gerir";

    public const string FacturacaoVer = "facturacao.ver";
    public const string FacturacaoEmitir = "facturacao.emitir";
    public const string FacturacaoAnular = "facturacao.anular";

    public const string RelatoriosVendas = "relatorios.vendas";
    public const string RelatoriosFinanceiros = "relatorios.financeiros";
    public const string RelatoriosStock = "relatorios.stock";

    public const string AuditoriaVer = "auditoria.ver";
    public const string ConfiguracoesGerir = "configuracoes.gerir";

    /// <summary>Todas as permissões com descrição para o ecrã de gestão de perfis.</summary>
    public static readonly IReadOnlyDictionary<string, string> Todas = new Dictionary<string, string>
    {
        [DashboardVer] = "Ver o dashboard",
        [UtilizadoresGerir] = "Gerir utilizadores",
        [PerfisGerir] = "Gerir perfis e permissões",
        [ProdutosVer] = "Consultar produtos",
        [ProdutosGerir] = "Criar e editar produtos e categorias",
        [ProdutosAlterarPreco] = "Alterar preços de venda",
        [PosUsar] = "Usar o POS (criar e editar pedidos)",
        [PosDesconto] = "Aplicar descontos",
        [PosCancelarItem] = "Cancelar itens já enviados",
        [PosCancelarPedido] = "Cancelar pedidos",
        [PosTransferirMesa] = "Transferir pedidos entre mesas",
        [MesasVer] = "Ver mapa de mesas",
        [MesasGerir] = "Criar e editar mesas e zonas",
        [CozinhaVer] = "Ver o ecrã da cozinha",
        [CozinhaActualizar] = "Actualizar estados na cozinha",
        [PagamentosRegistar] = "Registar pagamentos",
        [PagamentosAnular] = "Anular pagamentos e recibos",
        [CaixaOperar] = "Abrir e fechar caixa",
        [CaixaMovimentos] = "Registar entradas e saídas de caixa",
        [CaixaVerTodas] = "Consultar sessões de caixa de todos os utilizadores",
        [StockVer] = "Consultar stock",
        [StockGerir] = "Gerir artigos e movimentos de stock",
        [StockAjustar] = "Ajustes, perdas e inventários",
        [ReceitasGerir] = "Gerir fichas técnicas",
        [ComprasVer] = "Consultar compras e fornecedores",
        [ComprasGerir] = "Registar e confirmar compras",
        [ComprasAnular] = "Anular compras confirmadas",
        [ClientesVer] = "Consultar clientes",
        [ClientesGerir] = "Criar e editar clientes",
        [ReservasGerir] = "Gerir reservas",
        [DeliveryGerir] = "Gerir delivery e take away",
        [DespesasRegistar] = "Registar despesas",
        [DespesasAnular] = "Editar ou anular despesas confirmadas",
        [FuncionariosVer] = "Consultar funcionários",
        [FuncionariosGerir] = "Gerir funcionários",
        [FuncionariosDadosPessoais] = "Ver dados pessoais de funcionários",
        [CartoesVer] = "Consultar Cartões Evento e o seu saldo",
        [CartoesGerir] = "Registar, carregar, recarregar e bloquear Cartões Evento",
        [FacturacaoVer] = "Consultar documentos (facturas, VD, recibos, cotações, notas de crédito)",
        [FacturacaoEmitir] = "Emitir facturas, recibos e cotações",
        [FacturacaoAnular] = "Emitir notas de crédito e anular recibos e cotações",
        [RelatoriosVendas] = "Relatórios de vendas",
        [RelatoriosFinanceiros] = "Relatórios financeiros e de caixa",
        [RelatoriosStock] = "Relatórios de stock e compras",
        [AuditoriaVer] = "Consultar auditoria",
        [ConfiguracoesGerir] = "Alterar configurações do sistema",
    };
}

/// <summary>Entrada com código (PIN) no ecrã táctil: só para quem não gere utilizadores, perfis nem configurações.</summary>
public static class RegrasPin
{
    /// <summary>Quem tem alguma destas permissões entra sempre com a palavra-passe.</summary>
    public static readonly IReadOnlySet<string> SoComPalavraPasse = new HashSet<string>
    {
        Permissoes.UtilizadoresGerir, Permissoes.PerfisGerir, Permissoes.ConfiguracoesGerir,
    };

    /// <summary>4 a 6 algarismos, sem todos iguais (1111) nem sequências (1234, 4321). Devolve a mensagem de erro ou null.</summary>
    public static string? Validar(string? pin)
    {
        if (pin is null || pin.Length is < 4 or > 6 || !pin.All(char.IsAsciiDigit))
            return "O código tem de ter 4 a 6 algarismos.";
        if (pin.Distinct().Count() == 1)
            return "O código não pode ter os algarismos todos iguais.";
        var passos = pin.Zip(pin.Skip(1), (a, b) => b - a).Distinct().ToList();
        if (passos is [1] or [-1])
            return "O código não pode ser uma sequência (ex.: 1234).";
        return null;
    }
}

/// <summary>Perfis iniciais. O Administrador recebe sempre todas as permissões.</summary>
public static class PerfisPadrao
{
    public const string Administrador = "Administrador";
    public const string Gerente = "Gerente";
    public const string Caixa = "Caixa";
    public const string Garcom = "Garçom";
    public const string Cozinha = "Cozinha";
    public const string StockCompras = "Stock/Compras";

    public static readonly IReadOnlyDictionary<string, string[]> PermissoesIniciais = new Dictionary<string, string[]>
    {
        [Administrador] = [.. Permissoes.Todas.Keys],
        [Gerente] = [.. Permissoes.Todas.Keys.Where(p => p is not Permissoes.UtilizadoresGerir and not Permissoes.PerfisGerir and not Permissoes.ConfiguracoesGerir)],
        [Caixa] =
        [
            Permissoes.DashboardVer, Permissoes.ProdutosVer, Permissoes.PosUsar, Permissoes.MesasVer,
            Permissoes.PagamentosRegistar, Permissoes.CaixaOperar, Permissoes.CaixaMovimentos,
            Permissoes.ClientesVer, Permissoes.ClientesGerir, Permissoes.DeliveryGerir, Permissoes.ReservasGerir,
            Permissoes.DespesasRegistar, Permissoes.CozinhaVer, Permissoes.FacturacaoVer,
            Permissoes.CartoesVer, Permissoes.CartoesGerir,
        ],
        [Garcom] =
        [
            Permissoes.ProdutosVer, Permissoes.PosUsar, Permissoes.MesasVer, Permissoes.ClientesVer,
            Permissoes.ReservasGerir, Permissoes.CozinhaVer,
        ],
        [Cozinha] = [Permissoes.CozinhaVer, Permissoes.CozinhaActualizar, Permissoes.ProdutosVer, Permissoes.StockVer],
        [StockCompras] =
        [
            Permissoes.ProdutosVer, Permissoes.StockVer, Permissoes.StockGerir, Permissoes.StockAjustar,
            Permissoes.ReceitasGerir, Permissoes.ComprasVer, Permissoes.ComprasGerir, Permissoes.RelatoriosStock,
        ],
    };
}
