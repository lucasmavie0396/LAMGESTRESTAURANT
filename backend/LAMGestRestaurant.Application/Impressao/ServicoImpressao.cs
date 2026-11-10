using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Impressao;

/// <summary>
/// Decide o que imprimir e onde:
/// - talões: cada item vai para a impressora do produto ou, na falta dela, da categoria (as subcategorias herdam da principal);
///   sem nenhuma escolhida, para a impressora da sua estação de preparação, senão produtos do tipo Bar
///   vão para as impressoras «Bar» e os restantes que vão à cozinha para as «Cozinha» (as que não estão presas a uma estação);
/// - recibos: impressora «Caixa» associada ao terminal do pagamento ou, na falta dela, a impressora de caixa sem terminal;
///   o recibo de um pedido pago é a sua venda a dinheiro (pedidos antigos: recibo interno).
/// </summary>
public class ServicoImpressao(IAppDbContext db, ServicoPagamentos pagamentos, ServicoPedidos pedidos, Facturacao.ServicoDocumentos documentos,
    ConfiguracoesSistema config, IConsultaUtilizadores utilizadores)
{
    /// <summary>
    /// Talões por centro de produção. Pedido e reimpressão: itens não cancelados; anulação: os itens indicados já cancelados,
    /// que vão para os mesmos centros onde foram pedidos.
    /// </summary>
    public async Task<IReadOnlyList<TrabalhoImpressao>> TaloesAsync(int pedidoId, IReadOnlyCollection<int> itens, TipoTalao talao = TipoTalao.Pedido,
        string? motivo = null, CancellationToken ct = default)
    {
        var anulacao = talao == TipoTalao.Anulacao;
        var impressoras = await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao != FuncaoImpressora.Caixa && i.Funcao != FuncaoImpressora.Etiquetas).ToListAsync(ct);
        if (impressoras.Count == 0)
            return [];
        var pedido = await db.Pedidos.AsNoTracking().Where(p => p.Id == pedidoId)
            .Select(p => new { p.Numero, p.Tipo, Mesa = p.Mesa != null ? p.Mesa.Nome : null, Garcom = p.Garcom != null ? p.Garcom.Nome : null, p.Observacoes, p.CriadoPorId })
            .FirstOrDefaultAsync(ct);
        if (pedido is null)
            return [];
        var linhas = await db.PedidoItens.AsNoTracking()
            .Where(i => i.PedidoId == pedidoId && itens.Contains(i.Id) && i.Cancelado == anulacao)
            .OrderBy(i => i.Id)
            .Select(i => new
            {
                i.Quantidade, i.NomeProduto, i.Observacoes, i.EnviaCozinha, i.Produto.Tipo, i.Produto.CategoriaId,
                ImpressoraProduto = i.Produto.ImpressoraId, Estacao = i.Produto.Categoria.EstacaoPreparacaoId, i.EnviadoCozinhaEmUtc,
            })
            .ToListAsync(ct);
        var categorias = await db.Categorias.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => (c.CategoriaPaiId, c.ImpressoraId), ct);

        var porImpressora = new Dictionary<Impressora, List<ItemTalao>>();
        foreach (var item in linhas)
            foreach (var impressora in Destinos(item.ImpressoraProduto, ImpressoraDaCategoria(item.CategoriaId, categorias, impressoras),
                         item.Tipo, item.EnviaCozinha, item.Estacao, impressoras))
            {
                if (!porImpressora.TryGetValue(impressora, out var lista))
                    porImpressora[impressora] = lista = [];
                lista.Add(new ItemTalao(item.Quantidade, item.NomeProduto, item.Observacoes));
            }
        if (porImpressora.Count == 0)
            return [];

        var copias = await CopiasAsync(anulacao ? ChavesConfiguracao.CopiasAnulacao : ChavesConfiguracao.CopiasTalao);
        var fuso = await config.FusoAsync();
        // Anulação e reimpressão levam a hora actual; o pedido, a hora do envio.
        var enviado = TimeZoneInfo.ConvertTimeFromUtc(talao == TipoTalao.Pedido ? linhas.Max(i => i.EnviadoCozinhaEmUtc) ?? DateTime.UtcNow : DateTime.UtcNow, fuso);
        string? operador = null;
        if (pedido.CriadoPorId is int criador)
            operador = (await utilizadores.NomesAsync([criador], ct)).GetValueOrDefault(criador);

        return [.. porImpressora.Select(p => new TrabalhoImpressao(p.Key.Id, p.Key.NomeWindows, p.Key.LarguraPapelMm,
            ComposicaoImpressao.Talao(new DadosTalao(p.Key.Nome, pedido.Numero, pedido.Tipo, pedido.Mesa, pedido.Garcom, operador,
                enviado, pedido.Observacoes, p.Value, talao, motivo)), null, p.Key.Modo, p.Key.Nome, p.Key.PostoId, copias))];
    }

    /// <summary>Logótipo do estabelecimento (Configurações → Geral), ou null.</summary>
    public async Task<byte[]?> LogotipoAsync(CancellationToken ct = default)
    {
        var url = await config.TextoAsync(ChavesConfiguracao.LogotipoUrl);
        if (!EnderecoImagem.TentarId(url, out var id))
            return null;
        return await db.ImagensProduto.AsNoTracking().Where(i => i.Id == id).Select(i => i.Dados).FirstOrDefaultAsync(ct);
    }

    /// <summary>Cópias configuradas para o documento (1 a 10; 1 se não houver configuração).</summary>
    public async Task<int> CopiasAsync(string? chave) =>
        chave is not null && int.TryParse(await config.TextoAsync(chave), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, 10) : 1;

    /// <summary>Chave das cópias de um documento comercial/fiscal.</summary>
    public async Task<string> ChaveCopiasAsync(int documentoId, CancellationToken ct = default) =>
        await db.Documentos.AsNoTracking().Where(d => d.Id == documentoId).Select(d => d.Tipo).FirstOrDefaultAsync(ct) switch
        {
            Domain.Facturacao.TipoDocumento.Factura => ChavesConfiguracao.CopiasFactura,
            Domain.Facturacao.TipoDocumento.Recibo => ChavesConfiguracao.CopiasRecibo,
            Domain.Facturacao.TipoDocumento.Cotacao => ChavesConfiguracao.CopiasCotacao,
            Domain.Facturacao.TipoDocumento.NotaCredito => ChavesConfiguracao.CopiasNotaCredito,
            _ => ChavesConfiguracao.CopiasVendaDinheiro,
        };

    private async Task<TrabalhoImpressao> ComLogotipoAsync(Impressora impressora, DocumentoImpressao documento, string? chaveCopias, CancellationToken ct)
    {
        var semLogotipo = documento.Etiquetas is not null || impressora.LarguraPapelMm == Impressora.Largura80 && !await config.BoolAsync(ChavesConfiguracao.Logotipo80mm, padrao: true);
        return new(impressora.Id, impressora.NomeWindows, impressora.LarguraPapelMm, documento,
            semLogotipo ? null : await LogotipoAsync(ct), impressora.Modo, impressora.Nome, impressora.PostoId, await CopiasAsync(chaveCopias));
    }

    /// <summary>Documento já composto (relatório, fecho de caixa) para uma impressora activa, com o papel dessa impressora.</summary>
    public async Task<TrabalhoImpressao?> DocumentoAsync(int impressoraId, DocumentoImpressao documento, string? chaveCopias = null, CancellationToken ct = default) =>
        await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(i => i.Id == impressoraId && i.Activo, ct) is { } impressora
            ? await ComLogotipoAsync(impressora, documento, chaveCopias, ct)
            : null;

    /// <summary>Impressora activa definida na categoria ou, subindo, na categoria principal (subcategorias herdam).</summary>
    internal static int? ImpressoraDaCategoria(int categoriaId, IReadOnlyDictionary<int, (int? Pai, int? Impressora)> categorias, IReadOnlyList<Impressora> activas)
    {
        var visitadas = new HashSet<int>();
        for (int? id = categoriaId; id is int c && visitadas.Add(c) && categorias.TryGetValue(c, out var cat); id = cat.Pai)
            if (cat.Impressora is int i && activas.Any(a => a.Id == i))
                return i;
        return null;
    }

    /// <summary>
    /// Destino de um item: impressora do produto → da categoria (ou da categoria principal) → regras automáticas
    /// (estação de preparação; produtos Bar → impressoras Bar; restantes que vão à cozinha → impressoras Cozinha).
    /// Uma impressora escolhida no produto ou na categoria imprime mesmo os itens que não vão ao ecrã da cozinha.
    /// </summary>
    internal static IEnumerable<Impressora> Destinos(int? impressoraProduto, int? impressoraCategoria, TipoProduto tipo, bool enviaCozinha,
        int? estacao, IReadOnlyList<Impressora> impressoras)
    {
        foreach (var escolhida in new[] { impressoraProduto, impressoraCategoria })
            if (escolhida is int id && impressoras.FirstOrDefault(i => i.Id == id) is { } impressora)
                return [impressora];
        return DestinosAutomaticos(tipo, enviaCozinha, estacao, impressoras);
    }

    private static IEnumerable<Impressora> DestinosAutomaticos(TipoProduto tipo, bool enviaCozinha, int? estacao, IReadOnlyList<Impressora> impressoras)
    {
        if (estacao is int e && impressoras.Where(i => i.EstacaoPreparacaoId == e).ToList() is { Count: > 0 } daEstacao)
            return daEstacao;
        FuncaoImpressora? funcao = tipo == TipoProduto.Bar ? FuncaoImpressora.Bar : enviaCozinha ? FuncaoImpressora.Cozinha : null;
        return funcao is null ? [] : impressoras.Where(i => i.Funcao == funcao && i.EstacaoPreparacaoId == null);
    }

    public async Task<TrabalhoImpressao?> ReciboAsync(int pedidoId, int? terminalId, CancellationToken ct = default)
    {
        var impressora = await ImpressoraReciboAsync(pedidoId, terminalId, ct);
        if (impressora is null)
            return null;
        return await ComLogotipoAsync(impressora, await DocumentoReciboAsync(pedidoId), ChavesConfiguracao.CopiasVendaDinheiro, ct);
    }

    /// <summary>Documento comercial/fiscal composto (VD com os pagamentos do pedido).</summary>
    public async Task<DocumentoImpressao> DocumentoFiscalAsync(int documentoId, CancellationToken ct = default)
    {
        var documento = await documentos.ObterAsync(documentoId, ct);
        var pagamentosPedido = documento.PedidoId is int pedidoId
            ? (await pedidos.ObterAsync(pedidoId)).Pagamentos.Where(p => p.Estado == EstadoPagamento.Valido).ToList()
            : null;
        return ComposicaoDocumentos.Compor(documento, await config.FusoAsync(), await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT"),
            Vazio(await config.TextoAsync(ChavesConfiguracao.RodapeRecibo)), pagamentosPedido);
    }

    /// <summary>Impressora de caixa do terminal indicado (ou a de caixa sem terminal), ou null se não houver.</summary>
    public async Task<Impressora?> ImpressoraCaixaAsync(int? terminalId, CancellationToken ct = default)
    {
        var caixas = await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao == FuncaoImpressora.Caixa).OrderBy(i => i.Id).ToListAsync(ct);
        return caixas.FirstOrDefault(i => terminalId is not null && i.TerminalId == terminalId) ?? caixas.FirstOrDefault(i => i.TerminalId is null);
    }

    /// <summary>Conta (consulta antes do pagamento) do pedido, na hora actual.</summary>
    public async Task<DocumentoImpressao> DocumentoContaAsync(PedidoDto pedido)
    {
        var fuso = await config.FusoAsync();
        var estabelecimento = new DadosEstabelecimento(
            await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant"),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Endereco)),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Telefone)),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Nuit)),
            await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT"));
        return ComposicaoImpressao.Conta(pedido, estabelecimento, TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, fuso));
    }

    private static string? Vazio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;

    /// <summary>Recibo composto, para o posto imprimir pelo navegador quando não há impressora de caixa.</summary>
    public async Task<DocumentoImpressao> DocumentoReciboAsync(int pedidoId)
    {
        var recibo = await pagamentos.ObterReciboAsync(pedidoId); // 404 se o pedido ainda não está pago
        return await documentos.VendaDinheiroDoPedidoAsync(pedidoId) is int vd
            ? await DocumentoFiscalAsync(vd)
            : ComposicaoImpressao.Recibo(recibo, await config.FusoAsync());
    }

    /// <summary>Impressora de recibos para o terminal (o do pagamento, se não for indicado), ou null se não houver.</summary>
    public async Task<Impressora?> ImpressoraReciboAsync(int pedidoId, int? terminalId, CancellationToken ct = default)
    {
        var caixas = await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao == FuncaoImpressora.Caixa).OrderBy(i => i.Id).ToListAsync(ct);
        if (caixas.Count == 0)
            return null;
        terminalId ??= await db.Pedidos.Where(p => p.Id == pedidoId && p.SessaoCaixa != null).Select(p => (int?)p.SessaoCaixa!.TerminalId).FirstOrDefaultAsync(ct);
        return caixas.FirstOrDefault(i => terminalId is not null && i.TerminalId == terminalId) ?? caixas.FirstOrDefault(i => i.TerminalId is null);
    }

    public async Task<TrabalhoImpressao?> TesteAsync(int impressoraId, CancellationToken ct = default)
    {
        var i = await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(x => x.Id == impressoraId, ct);
        if (i is null)
            return null;
        var agora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await config.FusoAsync());
        return new TrabalhoImpressao(i.Id, i.NomeWindows, i.LarguraPapelMm, ComposicaoImpressao.Teste(i.Nome, i.NomeWindows, i.LarguraPapelMm, agora),
            null, i.Modo, i.Nome, i.PostoId);
    }
}

public record ImpressoraDto(
    int Id,
    string Nome,
    FuncaoImpressora Funcao,
    string NomeWindows,
    int LarguraPapelMm,
    int? EstacaoPreparacaoId,
    string? Estacao,
    int? TerminalId,
    string? Terminal,
    bool Activo,
    DateTime? UltimaImpressaoUtc,
    string? UltimoErro,
    ModoImpressora Modo,
    int? PostoId = null,
    string? Posto = null);

/// <summary>Modo omitido (clientes antigos) = Directo. Em modo PDF a impressora do Windows é opcional.</summary>
public record GuardarImpressora(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")] string Nome,
    FuncaoImpressora Funcao,
    [StringLength(260)] string? NomeWindows,
    int LarguraPapelMm,
    int? EstacaoPreparacaoId,
    int? TerminalId,
    ModoImpressora Modo = ModoImpressora.Directo,
    int? PostoId = null);

/// <summary>Configuração das impressoras (permissão configuracoes.gerir). Só aceita impressoras instaladas no Windows do servidor.</summary>
public class ServicoImpressoras(IAppDbContext db, IAuditoria auditoria, IDispositivoImpressao dispositivo, IFilaImpressao fila, EstadoImpressoras estados,
    ServicoAgentes agentes)
{
    private static readonly int[] Larguras = [Impressora.Largura58, Impressora.Largura80, Impressora.LarguraA4];

    public async Task<IReadOnlyList<ImpressoraDto>> ListarAsync(CancellationToken ct)
    {
        var lista = await db.Impressoras.AsNoTracking()
            .OrderBy(i => i.Funcao).ThenBy(i => i.Nome)
            .Select(i => new { i.Id, i.Nome, i.Funcao, i.NomeWindows, i.LarguraPapelMm, i.EstacaoPreparacaoId,
                Estacao = i.EstacaoPreparacao != null ? i.EstacaoPreparacao.Nome : null, i.TerminalId, Terminal = i.Terminal != null ? i.Terminal.Nome : null, i.Activo, i.Modo,
                i.PostoId, Posto = i.Posto != null ? i.Posto.Nome : null })
            .ToListAsync(ct);
        return [.. lista.Select(i =>
        {
            var estado = estados.Obter(i.Id);
            return new ImpressoraDto(i.Id, i.Nome, i.Funcao, i.NomeWindows, i.LarguraPapelMm, i.EstacaoPreparacaoId, i.Estacao, i.TerminalId, i.Terminal, i.Activo,
                estado?.EmUtc, estado?.Erro, i.Modo, i.PostoId, i.Posto);
        })];
    }

    public IReadOnlyList<string> Instaladas() => dispositivo.Instaladas();

    /// <summary>Servidor e postos, cada um com as impressoras instaladas no seu Windows.</summary>
    public Task<IReadOnlyList<LocalImpressoras>> LocaisAsync(CancellationToken ct) => agentes.LocaisAsync(dispositivo.Instaladas(), ct);

    public async Task<ImpressoraDto> GuardarAsync(int? id, GuardarImpressora dados)
    {
        var nome = dados.Nome.Trim();
        var nomeWindows = dados.NomeWindows?.Trim() ?? "";
        if (!Larguras.Contains(dados.LarguraPapelMm))
            throw new RegraNegocioException("Escolha o papel: 80 mm, 58 mm ou A4.");
        if (!Enum.IsDefined(dados.Modo))
            throw new RegraNegocioException("Escolha o modo: imprimir directo ou abrir em PDF.");
        if (dados.Funcao == FuncaoImpressora.Etiquetas && dados.Modo != ModoImpressora.Directo)
            throw new RegraNegocioException("A impressora de etiquetas imprime directo (sem PDF).");
        // Directo: tem de ser uma impressora instalada no Windows do servidor ou do posto escolhido. PDF: dispensa-a
        // (fica guardada se for indicada) e é sempre o servidor que gera o PDF.
        var posto = dados.Modo == ModoImpressora.Pdf ? null : dados.PostoId;
        IReadOnlyList<string> instaladas;
        var onde = "no Windows do servidor";
        if (posto is int p)
        {
            var local = await db.Terminais.AsNoTracking().Where(t => t.Id == p && t.Activo && t.Numero != null)
                .Select(t => new { t.Nome, t.Numero, t.AgenteTokenHash, t.AgenteImpressoras }).FirstOrDefaultAsync()
                ?? throw new RegraNegocioException("Posto inexistente, inactivo ou sem número.");
            if (local.AgenteTokenHash is null)
                throw new RegraNegocioException($"O posto «{local.Nome}» não tem o agente de impressão instalado (instale-o com o LAMGest-Setup, modo Posto).");
            instaladas = ServicoAgentes.Impressoras(local.AgenteImpressoras);
            onde = $"no posto «{local.Nome}»";
        }
        else
            instaladas = dispositivo.Instaladas();
        var instalada = instaladas.FirstOrDefault(n => string.Equals(n, nomeWindows, StringComparison.OrdinalIgnoreCase));
        if (instalada is null && (dados.Modo == ModoImpressora.Directo || nomeWindows.Length > 0))
            throw new RegraNegocioException(nomeWindows.Length == 0 ? "Escolha a impressora do Windows."
                : instaladas.Count == 0 ? $"Não há impressoras instaladas {onde}."
                : $"A impressora «{nomeWindows}» não está instalada {onde}.");
        var estacao = dados.Funcao is FuncaoImpressora.Caixa or FuncaoImpressora.Etiquetas ? null : dados.EstacaoPreparacaoId;
        var terminal = dados.Funcao == FuncaoImpressora.Caixa ? dados.TerminalId : null;
        if (estacao is int e && !await db.EstacoesPreparacao.AnyAsync(x => x.Id == e && x.Activo))
            throw new RegraNegocioException("Estação de preparação inexistente ou inactiva.");
        if (terminal is int t && !await db.Terminais.AnyAsync(x => x.Id == t && x.Activo))
            throw new RegraNegocioException("Terminal inexistente ou inactivo.");
        if (await db.Impressoras.AnyAsync(i => i.Id != id && i.Nome == nome))
            throw new ConflitoException("Já existe uma impressora com este nome.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var impressora = id is int existente
            ? await db.Impressoras.FindAsync(existente) ?? throw new NaoEncontradoException("Impressora não encontrada.")
            : db.Impressoras.Add(new Impressora()).Entity;
        impressora.Nome = nome;
        impressora.Funcao = dados.Funcao;
        impressora.Ligacao = LigacaoImpressora.Windows;
        impressora.NomeWindows = instalada ?? "";
        impressora.LarguraPapelMm = dados.LarguraPapelMm;
        impressora.Modo = dados.Modo;
        impressora.EstacaoPreparacaoId = estacao;
        impressora.TerminalId = terminal;
        impressora.PostoId = posto;
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "impressora.criar" : "impressora.editar", "Impressora", impressora.Id,
            new { impressora.Nome, impressora.Funcao, impressora.NomeWindows, impressora.LarguraPapelMm, impressora.Modo, impressora.EstacaoPreparacaoId, impressora.TerminalId, impressora.PostoId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ListarAsync(default)).Single(i => i.Id == impressora.Id);
    }

    public async Task DefinirActivaAsync(int id, bool activa)
    {
        var impressora = await db.Impressoras.FindAsync(id) ?? throw new NaoEncontradoException("Impressora não encontrada.");
        impressora.Activo = activa;
        auditoria.Registar(activa ? "impressora.activar" : "impressora.desactivar", "Impressora", id, new { impressora.Nome });
        await db.SaveChangesAsync();
    }

    public async Task TestarAsync(int id)
    {
        if (!await db.Impressoras.AnyAsync(i => i.Id == id))
            throw new NaoEncontradoException("Impressora não encontrada.");
        fila.Enfileirar(new ImprimirTeste(id));
    }
}
