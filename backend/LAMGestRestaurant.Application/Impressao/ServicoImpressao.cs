using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Impressao;

/// <summary>
/// Decide o que imprimir e onde:
/// - talões: cada item enviado vai para a impressora da sua estação de preparação, se houver; senão produtos do tipo Bar
///   vão para as impressoras «Bar» e os restantes que vão à cozinha para as «Cozinha» (as que não estão presas a uma estação);
/// - recibos: impressora «Caixa» associada ao terminal do pagamento ou, na falta dela, a impressora de caixa sem terminal.
/// </summary>
public class ServicoImpressao(IAppDbContext db, ServicoPagamentos pagamentos, ConfiguracoesSistema config, IConsultaUtilizadores utilizadores)
{
    public async Task<IReadOnlyList<TrabalhoImpressao>> TaloesAsync(int pedidoId, IReadOnlyCollection<int> itens, CancellationToken ct = default)
    {
        var impressoras = await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao != FuncaoImpressora.Caixa).ToListAsync(ct);
        if (impressoras.Count == 0)
            return [];
        var pedido = await db.Pedidos.AsNoTracking().Where(p => p.Id == pedidoId)
            .Select(p => new { p.Numero, p.Tipo, Mesa = p.Mesa != null ? p.Mesa.Nome : null, Garcom = p.Garcom != null ? p.Garcom.Nome : null, p.Observacoes, p.CriadoPorId })
            .FirstOrDefaultAsync(ct);
        if (pedido is null)
            return [];
        var linhas = await db.PedidoItens.AsNoTracking()
            .Where(i => i.PedidoId == pedidoId && itens.Contains(i.Id) && !i.Cancelado)
            .OrderBy(i => i.Id)
            .Select(i => new { i.Quantidade, i.NomeProduto, i.Observacoes, i.EnviaCozinha, i.Produto.Tipo, Estacao = i.Produto.Categoria.EstacaoPreparacaoId, i.EnviadoCozinhaEmUtc })
            .ToListAsync(ct);

        var porImpressora = new Dictionary<Impressora, List<ItemTalao>>();
        foreach (var item in linhas)
            foreach (var impressora in Destinos(item.Tipo, item.EnviaCozinha, item.Estacao, impressoras))
            {
                if (!porImpressora.TryGetValue(impressora, out var lista))
                    porImpressora[impressora] = lista = [];
                lista.Add(new ItemTalao(item.Quantidade, item.NomeProduto, item.Observacoes));
            }
        if (porImpressora.Count == 0)
            return [];

        var fuso = await config.FusoAsync();
        var enviado = TimeZoneInfo.ConvertTimeFromUtc(linhas.Max(i => i.EnviadoCozinhaEmUtc) ?? DateTime.UtcNow, fuso);
        string? operador = null;
        if (pedido.CriadoPorId is int criador)
            operador = (await utilizadores.NomesAsync([criador], ct)).GetValueOrDefault(criador);

        return [.. porImpressora.Select(p => new TrabalhoImpressao(p.Key.Id, p.Key.NomeWindows, p.Key.LarguraPapelMm,
            ComposicaoImpressao.Talao(new DadosTalao(p.Key.Nome, pedido.Numero, pedido.Tipo, pedido.Mesa, pedido.Garcom, operador,
                enviado, pedido.Observacoes, p.Value))))];
    }

    internal static IEnumerable<Impressora> Destinos(TipoProduto tipo, bool enviaCozinha, int? estacao, IReadOnlyList<Impressora> impressoras)
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
        var recibo = await pagamentos.ObterReciboAsync(pedidoId);
        return new TrabalhoImpressao(impressora.Id, impressora.NomeWindows, impressora.LarguraPapelMm,
            ComposicaoImpressao.Recibo(recibo, await config.FusoAsync()));
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
        return new TrabalhoImpressao(i.Id, i.NomeWindows, i.LarguraPapelMm, ComposicaoImpressao.Teste(i.Nome, i.NomeWindows, i.LarguraPapelMm, agora));
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
    string? UltimoErro);

public record GuardarImpressora(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")] string Nome,
    FuncaoImpressora Funcao,
    [Required(ErrorMessage = "Escolha a impressora do Windows.")][StringLength(260)] string NomeWindows,
    int LarguraPapelMm,
    int? EstacaoPreparacaoId,
    int? TerminalId);

/// <summary>Configuração das impressoras (permissão configuracoes.gerir). Só aceita impressoras instaladas no Windows do servidor.</summary>
public class ServicoImpressoras(IAppDbContext db, IAuditoria auditoria, IDispositivoImpressao dispositivo, IFilaImpressao fila, EstadoImpressoras estados)
{
    private static readonly int[] Larguras = [Impressora.Largura58, Impressora.Largura80, Impressora.LarguraA4];

    public async Task<IReadOnlyList<ImpressoraDto>> ListarAsync(CancellationToken ct)
    {
        var lista = await db.Impressoras.AsNoTracking()
            .OrderBy(i => i.Funcao).ThenBy(i => i.Nome)
            .Select(i => new { i.Id, i.Nome, i.Funcao, i.NomeWindows, i.LarguraPapelMm, i.EstacaoPreparacaoId,
                Estacao = i.EstacaoPreparacao != null ? i.EstacaoPreparacao.Nome : null, i.TerminalId, Terminal = i.Terminal != null ? i.Terminal.Nome : null, i.Activo })
            .ToListAsync(ct);
        return [.. lista.Select(i =>
        {
            var estado = estados.Obter(i.Id);
            return new ImpressoraDto(i.Id, i.Nome, i.Funcao, i.NomeWindows, i.LarguraPapelMm, i.EstacaoPreparacaoId, i.Estacao, i.TerminalId, i.Terminal, i.Activo,
                estado?.EmUtc, estado?.Erro);
        })];
    }

    public IReadOnlyList<string> Instaladas() => dispositivo.Instaladas();

    public async Task<ImpressoraDto> GuardarAsync(int? id, GuardarImpressora dados)
    {
        var nome = dados.Nome.Trim();
        var nomeWindows = dados.NomeWindows.Trim();
        if (!Larguras.Contains(dados.LarguraPapelMm))
            throw new RegraNegocioException("Escolha o papel: 80 mm, 58 mm ou A4.");
        var instaladas = dispositivo.Instaladas();
        var instalada = instaladas.FirstOrDefault(n => string.Equals(n, nomeWindows, StringComparison.OrdinalIgnoreCase))
            ?? throw new RegraNegocioException(instaladas.Count == 0
                ? "Não há impressoras instaladas no Windows do servidor."
                : $"A impressora «{nomeWindows}» não está instalada no Windows do servidor.");
        var estacao = dados.Funcao == FuncaoImpressora.Caixa ? null : dados.EstacaoPreparacaoId;
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
        impressora.NomeWindows = instalada;
        impressora.LarguraPapelMm = dados.LarguraPapelMm;
        impressora.EstacaoPreparacaoId = estacao;
        impressora.TerminalId = terminal;
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "impressora.criar" : "impressora.editar", "Impressora", impressora.Id,
            new { impressora.Nome, impressora.Funcao, impressora.NomeWindows, impressora.LarguraPapelMm, impressora.EstacaoPreparacaoId, impressora.TerminalId });
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
