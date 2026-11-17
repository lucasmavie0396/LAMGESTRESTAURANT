using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

public enum FaseVisor { Repouso, Venda, Pagamento, Pago, Teste, Peso }

/// <summary>Produto a ser pesado no POS: peso da balança, preço por unidade e valor (calculado no servidor).</summary>
public sealed record PesagemVisor(string Produto, decimal Peso, string Unidade, decimal PrecoUnitario, decimal Valor);

public sealed record LinhaVisor(string Descricao, decimal Quantidade, decimal Total);

/// <summary>O que o cliente vê: sem venda (mensagem), artigos e total, pagamento (recebido e troco), pago (troco e agradecimento).</summary>
public sealed record ConteudoVisor(FaseVisor Fase, string Mensagem, IReadOnlyList<LinhaVisor> Linhas, decimal Total, decimal? Recebido, decimal? Troco, string? Pedido,
    PesagemVisor? Pesagem = null)
{
    public static ConteudoVisor Repouso(string mensagem) => new(FaseVisor.Repouso, mensagem, [], 0, null, null, null);
}

/// <summary>Página do visor (segundo monitor ou tablet virado para o cliente).</summary>
public sealed record EcraVisorDto(long Versao, string Estabelecimento, string Moeda, bool TemLogotipo, ConteudoVisor Conteudo);

/// <summary>Visor para o agente do posto: configuração e as duas linhas já compostas para o visor de poste.</summary>
public sealed record VisorAgenteDto(long Versao, TipoVisor Tipo, string? Porta, ProtocoloVisor Protocolo, int Velocidade, int Colunas, string? Ecra,
    string? Endereco, string Linha1, string Linha2);

/// <summary>O POS deste posto mostra o pedido (vazio = sem venda); <paramref name="Recebido"/> preenchido durante o pagamento.</summary>
public sealed record ActualizarVisor(int TerminalId, int? PedidoId, decimal? Recebido, PesarVisor? Pesagem = null);

/// <summary>O POS está a pesar este produto (peso na unidade do produto): o visor mostra o peso e o valor.</summary>
public sealed record PesarVisor(int ProdutoId, decimal Peso);

public sealed record ConfigurarVisor(TipoVisor Tipo, string? Porta, ProtocoloVisor Protocolo, int Velocidade, int Colunas, string? Ecra);

/// <summary>Porta COM do computador do posto (descrição do Windows, ex.: «USB Serial Device»).</summary>
public sealed record PortaSerie(string Nome, string? Descricao);

/// <summary>Ecrã ligado ao computador do posto (nome do Windows, ex.: \\.\DISPLAY2).</summary>
public sealed record EcraPosto(string Nome, int Largura, int Altura, bool Principal);

/// <summary>Visor do posto em Configurações → Postos, com as portas e ecrãs detectados pelo agente.</summary>
public sealed record VisorPostoDto(TipoVisor Tipo, string? Porta, ProtocoloVisor Protocolo, int Velocidade, int Colunas, string? Ecra, string? Endereco,
    IReadOnlyList<PortaSerie> PortasDetectadas, IReadOnlyList<EcraPosto> EcrasDetectados);

/// <summary>
/// Conteúdo actual do visor de cada posto, em memória (não é histórico: depois de reiniciar o servidor o visor volta à mensagem).
/// Cada alteração tem uma versão nova e acorda quem espera (agente do posto e página do visor, por pedido longo).
/// </summary>
public sealed class EstadoVisores
{
    private readonly object trinco = new();
    private readonly Dictionary<int, (long Versao, ConteudoVisor? Conteudo, TaskCompletionSource Mudou)> visores = [];
    // Começa num número diferente a cada arranque: quem tinha uma versão antiga recebe logo a actual.
    // Em milissegundos (cabe nos números do JavaScript, ao contrário dos ticks).
    private long contador = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public (long Versao, ConteudoVisor? Conteudo, Task Mudou) Ler(int terminalId)
    {
        lock (trinco)
        {
            if (!visores.TryGetValue(terminalId, out var v))
                visores[terminalId] = v = (++contador, null, Novo());
            return (v.Versao, v.Conteudo, v.Mudou.Task);
        }
    }

    /// <summary>Muda o conteúdo (null em <paramref name="manter"/> = só a configuração mudou: mantém o conteúdo e muda a versão).</summary>
    public long Definir(int terminalId, ConteudoVisor? conteudo, bool manter = false)
    {
        TaskCompletionSource? anterior;
        long versao;
        lock (trinco)
        {
            visores.TryGetValue(terminalId, out var v);
            anterior = v.Mudou;
            versao = ++contador;
            visores[terminalId] = (versao, manter ? v.Conteudo : conteudo, Novo());
        }
        anterior?.TrySetResult();
        return versao;
    }

    /// <summary>Espera até a versão ser diferente de <paramref name="versao"/> ou acabar o tempo.</summary>
    public async Task EsperarAsync(int terminalId, long versao, TimeSpan espera, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + espera;
        while (true)
        {
            var (actual, _, mudou) = Ler(terminalId);
            var falta = limite - DateTime.UtcNow;
            if (actual != versao || falta <= TimeSpan.Zero)
                return;
            try
            {
                await mudou.WaitAsync(falta, ct);
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    private static TaskCompletionSource Novo() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Visor do cliente dos postos. O POS diz que pedido está no ecrã (e o valor recebido no pagamento); o servidor compõe o conteúdo
/// a partir do pedido gravado (os totais nunca vêm do navegador). O agente do posto mostra-o no visor de poste (porta COM) ou abre
/// a página do visor no segundo monitor; a página também serve num tablet virado para o cliente.
/// </summary>
public class ServicoVisor(IAppDbContext db, ConfiguracoesSistema config, EstadoVisores estados, IAuditoria auditoria, TimeProvider relogio)
{
    public static readonly int[] Velocidades = [2400, 4800, 9600, 19200, 38400, 57600, 115200];
    /// <summary>Pagamentos feitos há menos do que isto contam para o troco mostrado depois de pago.</summary>
    private static readonly TimeSpan PagamentoRecente = TimeSpan.FromMinutes(2);
    private const int MaximoJson = 4000;

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public async Task ActualizarAsync(ActualizarVisor dados, CancellationToken ct = default)
    {
        var tipo = await db.Terminais.AsNoTracking().Where(t => t.Id == dados.TerminalId).Select(t => (TipoVisor?)t.VisorTipo).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (tipo == TipoVisor.Nenhum)
            return; // posto sem visor: nada a fazer
        estados.Definir(dados.TerminalId, dados.Pesagem is { } pesagem ? await PesagemAsync(pesagem, ct) : await ConteudoAsync(dados.PedidoId, dados.Recebido, ct));
    }

    /// <summary>Produto na balança: nome, peso e valor (peso × preço de venda, calculado aqui).</summary>
    private async Task<ConteudoVisor> PesagemAsync(PesarVisor pesagem, CancellationToken ct)
    {
        if (pesagem.Peso is < 0 or > ServicoBalanca.PesoMaximo)
            throw new RegraNegocioException("Peso inválido.");
        var p = await db.Produtos.AsNoTracking().Where(x => x.Id == pesagem.ProdutoId)
            .Select(x => new { x.Nome, x.PrecoVenda, Unidade = x.UnidadeMedida != null ? x.UnidadeMedida.Codigo : "un" }).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Produto não encontrado.");
        var peso = decimal.Round(pesagem.Peso, 3, MidpointRounding.AwayFromZero);
        var valor = decimal.Round(peso * p.PrecoVenda, 2, MidpointRounding.AwayFromZero);
        return new ConteudoVisor(FaseVisor.Peso, "", [new LinhaVisor(p.Nome, peso, valor)], valor, null, null, null,
            new PesagemVisor(p.Nome, peso, p.Unidade, p.PrecoVenda, valor));
    }

    private async Task<ConteudoVisor> ConteudoAsync(int? pedidoId, decimal? recebido, CancellationToken ct)
    {
        if (pedidoId is not int id)
            return ConteudoVisor.Repouso(await MensagemAsync(ChavesConfiguracao.VisorBoasVindas, "Bem-vindo!"));
        var p = await db.Pedidos.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Numero, x.Estado, x.Total, x.TotalPago }).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Pedido não encontrado.");
        var linhas = await db.PedidoItens.AsNoTracking().Where(i => i.PedidoId == id && !i.Cancelado).OrderBy(i => i.Id)
            .Select(i => new LinhaVisor(i.NomeProduto, i.Quantidade, i.Total)).ToListAsync(ct);

        if (p.Estado == EstadoPedido.Pago)
        {
            var desde = Agora - PagamentoRecente;
            var pagos = await db.Pagamentos.AsNoTracking().Where(x => x.PedidoId == id && x.Estado == EstadoPagamento.Valido && x.DataUtc >= desde)
                .Select(x => new { x.ValorRecebido, x.Troco }).ToListAsync(ct);
            return new ConteudoVisor(FaseVisor.Pago, await MensagemAsync(ChavesConfiguracao.VisorAgradecimento, "Obrigado, volte sempre!"), linhas, p.Total,
                pagos.Count > 0 ? pagos.Sum(x => x.ValorRecebido) : null, pagos.Sum(x => x.Troco), p.Numero);
        }
        var emDivida = Math.Max(0, p.Total - p.TotalPago);
        if (recebido is decimal r and >= 0)
        {
            r = Math.Round(r, 2);
            return new ConteudoVisor(FaseVisor.Pagamento, "", linhas, emDivida, r, Math.Max(0, r - emDivida), p.Numero);
        }
        return new ConteudoVisor(FaseVisor.Venda, "", linhas, emDivida, null, null, p.Numero);
    }

    private async Task<string> MensagemAsync(string chave, string padrao) =>
        await config.TextoAsync(chave, padrao) is { Length: > 0 } texto ? texto : padrao;

    /// <summary>Mensagem de teste no visor do posto (o visor tem de estar configurado).</summary>
    public async Task TestarAsync(int terminalId, CancellationToken ct = default)
    {
        var t = await db.Terminais.AsNoTracking().Where(x => x.Id == terminalId).Select(x => new { x.Nome, x.Numero, x.VisorTipo }).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (t.VisorTipo == TipoVisor.Nenhum)
            throw new RegraNegocioException("Escolha primeiro o tipo de visor do posto.");
        estados.Definir(terminalId, new ConteudoVisor(FaseVisor.Teste, $"Teste do visor | {t.Nome}", [], 0, null, null, null));
    }

    public async Task ConfigurarAsync(int terminalId, ConfigurarVisor dados, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        var porta = string.IsNullOrWhiteSpace(dados.Porta) ? null : dados.Porta.Trim().ToUpperInvariant();
        if (dados.Tipo == TipoVisor.Poste)
        {
            if (porta is null || !System.Text.RegularExpressions.Regex.IsMatch(porta, "^COM[0-9]{1,3}$"))
                throw new RegraNegocioException("Indique a porta COM do visor (ex.: COM3).");
            if (!Velocidades.Contains(dados.Velocidade))
                throw new RegraNegocioException("Velocidade inválida.");
            if (dados.Colunas is < 16 or > 40)
                throw new RegraNegocioException("O visor tem de ter entre 16 e 40 caracteres por linha.");
        }
        if (!Enum.IsDefined(dados.Tipo) || !Enum.IsDefined(dados.Protocolo))
            throw new RegraNegocioException("Tipo de visor inválido.");
        var ecra = string.IsNullOrWhiteSpace(dados.Ecra) ? null : dados.Ecra.Trim();
        if (ecra is { Length: > 64 })
            throw new RegraNegocioException("Nome do ecrã inválido.");

        t.VisorTipo = dados.Tipo;
        if (dados.Tipo == TipoVisor.Poste)
        {
            t.VisorPorta = porta;
            t.VisorProtocolo = dados.Protocolo;
            t.VisorVelocidade = dados.Velocidade;
            t.VisorColunas = dados.Colunas;
        }
        if (dados.Tipo == TipoVisor.Monitor)
            t.VisorEcra = ecra;
        if (dados.Tipo != TipoVisor.Nenhum)
            t.VisorChave ??= NovaChave();
        auditoria.Registar("posto.visor", "Terminal", t.Id, new { t.Nome, t.Numero, dados.Tipo, Porta = t.VisorPorta, t.VisorProtocolo, t.VisorVelocidade, t.VisorColunas, Ecra = t.VisorEcra });
        await db.SaveChangesAsync(ct);
        estados.Definir(terminalId, null, manter: true); // o agente recebe logo a configuração nova
    }

    /// <summary>Novo endereço da página do visor: o anterior deixa de abrir.</summary>
    public async Task NovoEnderecoAsync(int terminalId, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        t.VisorChave = NovaChave();
        auditoria.Registar("posto.visor-endereco", "Terminal", t.Id, new { t.Nome, t.Numero });
        await db.SaveChangesAsync(ct);
        estados.Definir(terminalId, null, manter: true);
    }

    private static string NovaChave() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Caminho da página do visor (relativo ao servidor).</summary>
    public static string? Endereco(int terminalId, string? chave) => chave is null ? null : $"/visor?terminal={terminalId}&chave={chave}";

    /// <summary>Configuração e conteúdo para o agente; espera (pedido longo) enquanto a versão for a que o agente já tem.</summary>
    public async Task<VisorAgenteDto> ParaAgenteAsync(int terminalId, long versao, TimeSpan espera, CancellationToken ct = default)
    {
        await estados.EsperarAsync(terminalId, versao, espera, ct);
        var (actual, conteudo, _) = estados.Ler(terminalId);
        var t = await db.Terminais.AsNoTracking().Where(x => x.Id == terminalId)
            .Select(x => new { x.VisorTipo, x.VisorPorta, x.VisorProtocolo, x.VisorVelocidade, x.VisorColunas, x.VisorEcra, x.VisorChave }).FirstAsync(ct);
        conteudo ??= ConteudoVisor.Repouso(await MensagemAsync(ChavesConfiguracao.VisorBoasVindas, "Bem-vindo!"));
        var (l1, l2) = LinhasPoste(conteudo, t.VisorColunas);
        return new VisorAgenteDto(actual, t.VisorTipo, t.VisorPorta, t.VisorProtocolo, t.VisorVelocidade, t.VisorColunas, t.VisorEcra,
            Endereco(terminalId, t.VisorChave), l1, l2);
    }

    /// <summary>Página do visor (sem sessão: só com a chave do endereço). Null se a chave não servir ou o posto não tiver visor.</summary>
    public async Task<EcraVisorDto?> ParaEcraAsync(int terminalId, string? chave, long versao, TimeSpan espera, CancellationToken ct = default)
    {
        if (!await ChaveValidaAsync(terminalId, chave, ct))
            return null;
        await estados.EsperarAsync(terminalId, versao, espera, ct);
        var (actual, conteudo, _) = estados.Ler(terminalId);
        conteudo ??= ConteudoVisor.Repouso(await MensagemAsync(ChavesConfiguracao.VisorBoasVindas, "Bem-vindo!"));
        return new EcraVisorDto(actual, await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento), await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT"),
            EnderecoImagem.TentarId(await config.TextoAsync(ChavesConfiguracao.LogotipoUrl), out _), conteudo);
    }

    public async Task<bool> ChaveValidaAsync(int terminalId, string? chave, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chave))
            return false;
        var t = await db.Terminais.AsNoTracking().Where(x => x.Id == terminalId && x.Activo && x.VisorTipo != TipoVisor.Nenhum)
            .Select(x => x.VisorChave).FirstOrDefaultAsync(ct);
        return t is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(t), Encoding.ASCII.GetBytes(chave.Trim()));
    }

    // ---------- Portas e ecrãs enviados pelo agente ----------

    public static string? GuardarPortas(IReadOnlyList<PortaSerie>? portas) => Limitar(portas?
        .Where(p => p.Nome is { Length: > 0 and <= 20 })
        .Select(p => new PortaSerie(p.Nome.Trim().ToUpperInvariant(), p.Descricao is { Length: > 0 } d ? d[..Math.Min(d.Length, 120)] : null))
        .DistinctBy(p => p.Nome).ToList());

    public static string? GuardarEcras(IReadOnlyList<EcraPosto>? ecras) => Limitar(ecras?
        .Where(e => e.Nome is { Length: > 0 and <= 64 }).DistinctBy(e => e.Nome).ToList());

    private static string? Limitar<T>(List<T>? lista)
    {
        if (lista is null)
            return null;
        while (lista.Count > 0 && JsonSerializer.Serialize(lista, Json).Length > MaximoJson)
            lista.RemoveAt(lista.Count - 1);
        return JsonSerializer.Serialize(lista, Json);
    }

    public static IReadOnlyList<T> Lista<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Visor de poste: duas linhas de texto simples ----------

    /// <summary>Duas linhas de <paramref name="colunas"/> caracteres, sem acentos (os visores de poste só têm ASCII).</summary>
    public static (string Linha1, string Linha2) LinhasPoste(ConteudoVisor c, int colunas)
    {
        colunas = Math.Clamp(colunas, 16, 40);
        (string, string) Texto(string mensagem)
        {
            var partes = mensagem.Split('|', 2, StringSplitOptions.TrimEntries);
            if (partes.Length == 2)
                return (Centrar(partes[0], colunas), Centrar(partes[1], colunas));
            var (a, b) = Partir(Simples(mensagem), colunas);
            return (Centrar(a, colunas), Centrar(b, colunas));
        }
        return c.Fase switch
        {
            FaseVisor.Venda when c.Linhas.Count > 0 => (Artigo(c.Linhas[^1], colunas), AmbosLados("TOTAL", Valor(c.Total), colunas)),
            FaseVisor.Venda => (Centrar(c.Pedido is null ? "" : $"Pedido {c.Pedido}", colunas), AmbosLados("TOTAL", Valor(c.Total), colunas)),
            FaseVisor.Pagamento => (AmbosLados("TOTAL", Valor(c.Total), colunas),
                c.Troco > 0 ? AmbosLados("TROCO", Valor(c.Troco.Value), colunas) : AmbosLados("RECEBIDO", Valor(c.Recebido ?? 0), colunas)),
            FaseVisor.Pago => (c.Troco > 0 ? AmbosLados("TROCO", Valor(c.Troco.Value), colunas) : AmbosLados("PAGO", Valor(c.Total), colunas),
                Centrar(Simples(c.Mensagem), colunas)),
            FaseVisor.Peso when c.Pesagem is { } pp => (AmbosLados(pp.Produto, $"{Quantidade(pp.Peso)} {Simples(pp.Unidade)}", colunas), AmbosLados("VALOR", Valor(pp.Valor), colunas)),
            _ => Texto(c.Mensagem),
        };
    }

    private static string Artigo(LinhaVisor l, int colunas)
    {
        var nome = l.Quantidade == 1 ? l.Descricao : $"{l.Quantidade.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',')}x {l.Descricao}";
        return AmbosLados(nome, Valor(l.Total), colunas);
    }

    /// <summary>Peso com 3 casas decimais e vírgula (ex.: 0,485).</summary>
    public static string Quantidade(decimal v) => v.ToString("0.000", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>1 250,00 (espaço nos milhares, vírgula decimal).</summary>
    public static string Valor(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(',', ' ').Replace('.', ',');

    private static string AmbosLados(string esquerda, string direita, int colunas)
    {
        esquerda = Simples(esquerda);
        if (direita.Length >= colunas)
            return direita[..colunas];
        var espaco = colunas - direita.Length - 1;
        if (esquerda.Length > espaco)
            esquerda = esquerda[..espaco].TrimEnd();
        return esquerda + new string(' ', colunas - esquerda.Length - direita.Length) + direita;
    }

    private static string Centrar(string texto, int colunas)
    {
        texto = Simples(texto);
        if (texto.Length >= colunas)
            return texto[..colunas];
        var esquerda = (colunas - texto.Length) / 2;
        return (new string(' ', esquerda) + texto).PadRight(colunas);
    }

    /// <summary>Divide em duas linhas pelas palavras.</summary>
    private static (string, string) Partir(string texto, int colunas)
    {
        if (texto.Length <= colunas)
            return (texto, "");
        var corte = texto.LastIndexOf(' ', colunas);
        if (corte <= 0)
            corte = colunas;
        return (texto[..corte].TrimEnd(), texto[corte..].Trim());
    }

    /// <summary>Sem acentos nem caracteres fora do ASCII visível.</summary>
    public static string Simples(string texto)
    {
        var s = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            s.Append(ch is >= ' ' and <= '~' ? ch : ch is '\u2014' or '\u2013' ? '-' : char.IsWhiteSpace(ch) ? ' ' : '?');
        }
        return s.ToString().Trim();
    }
}
