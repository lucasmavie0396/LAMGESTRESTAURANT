using System.Globalization;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary>Etiqueta da balança lida: código do produto na balança e o valor (peso em kg ou preço).</summary>
public sealed record EtiquetaBalanca(int Codigo, decimal Valor, bool ControloValido);

/// <summary>
/// Formato das etiquetas da balança (EAN-13): [2 algarismos de prefixo][código do produto][peso ou preço][controlo].
/// O peso vem em gramas (3 casas decimais do kg); o preço com <see cref="DecimaisPreco"/> casas decimais.
/// </summary>
public sealed record FormatoEtiquetaBalanca(IReadOnlyList<string> Prefixos, int DigitosCodigo, bool Preco, int DecimaisPreco)
{
    /// <summary>Prefixos de 1 ou 2 algarismos começados por 2 («2» = de 20 a 29), separados por vírgulas; null se inválidos.</summary>
    public static IReadOnlyList<string>? LerPrefixos(string? texto)
    {
        var partes = (texto ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return partes.Length > 0 && partes.Length <= 10 && partes.All(p => Regex.IsMatch(p, "^2[0-9]?$")) ? [.. partes.Distinct()] : null;
    }

    /// <summary>Null se o código não é uma etiqueta da balança (13 algarismos com um dos prefixos).</summary>
    public EtiquetaBalanca? Ler(string? codigo)
    {
        codigo = codigo?.Trim();
        if (codigo is not { Length: 13 } || !codigo.All(char.IsAsciiDigit) || !Prefixos.Any(p => codigo.StartsWith(p, StringComparison.Ordinal)))
            return null;
        var produto = int.Parse(codigo.AsSpan(2, DigitosCodigo), NumberStyles.None, CultureInfo.InvariantCulture);
        var valor = long.Parse(codigo.AsSpan(2 + DigitosCodigo, 10 - DigitosCodigo), NumberStyles.None, CultureInfo.InvariantCulture);
        var escala = Preco ? DecimaisPreco : 3;
        return new EtiquetaBalanca(produto, valor / (decimal)Math.Pow(10, escala), ServicoCatalogo.Ean13(codigo[..12]) == codigo);
    }
}

/// <summary>Produto e quantidade de uma etiqueta da balança, prontos a lançar no POS.</summary>
public sealed record ProdutoEtiquetaDto(ProdutoDto Produto, decimal Quantidade, decimal? Valor);

public sealed record ConfigurarBalanca(ModoBalanca Modo, string? Porta, int Velocidade, FormatoSerie Formato);

/// <summary>Balança do posto em Configurações → Postos.</summary>
public sealed record BalancaPostoDto(ModoBalanca Modo, string? Porta, int Velocidade, FormatoSerie Formato);

/// <summary>Leitura enviada pelo agente: peso em kg (null sem peso), estável e o erro (balança desligada, sobrecarga, etc.).</summary>
public sealed record LeituraAgente(decimal? Peso, bool Estavel, string? Erro);

/// <summary>Resposta à leitura do agente: continua a ler enquanto o POS pedir o peso.</summary>
public sealed record RespostaLeitura(bool Activa);

/// <summary>Balança para o agente: configuração e se o POS está a pedir o peso.</summary>
public sealed record BalancaAgenteDto(long Versao, ModoBalanca Modo, string? Porta, int Velocidade, FormatoSerie Formato, bool Activa);

/// <summary>Peso da balança do posto para o POS. <see cref="Configurada"/> falso = o posto não tem balança (escreve-se o peso).</summary>
public sealed record LeituraBalancaDto(long Versao, bool Configurada, decimal? Peso, bool Estavel, string? Erro);

/// <summary>
/// Balanças dos postos, em memória: se o POS está a pedir o peso (o agente só lê a balança enquanto isso) e a última leitura.
/// Duas versões: a do agente (configuração ou pedido do POS) e a das leituras (para o POS), cada uma acorda quem espera por ela.
/// </summary>
public sealed class EstadoBalancas
{
    /// <summary>Uma leitura mais antiga do que isto já não conta (o agente lê várias vezes por segundo).</summary>
    public static readonly TimeSpan LeituraValida = TimeSpan.FromSeconds(3);
    /// <summary>Sem leitura nenhuma passado este tempo depois de o POS pedir o peso: o agente ou a balança não respondem.</summary>
    public static readonly TimeSpan SemResposta = TimeSpan.FromSeconds(6);

    private sealed class Balanca
    {
        public long VersaoAgente;
        public TaskCompletionSource MudouAgente = Novo();
        public long VersaoLeitura;
        public TaskCompletionSource MudouLeitura = Novo();
        public DateTime ActivaDesde;
        public DateTime ActivaAte;
        public LeituraAgente? Leitura;
        public DateTime LidaEm;
    }

    private readonly object trinco = new();
    private readonly Dictionary<int, Balanca> balancas = [];
    // Como no visor: começa num número diferente a cada arranque e cabe nos números do JavaScript.
    private long contador = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private Balanca Obter(int terminalId)
    {
        if (!balancas.TryGetValue(terminalId, out var b))
            balancas[terminalId] = b = new Balanca { VersaoAgente = ++contador, VersaoLeitura = ++contador };
        return b;
    }

    public (long Versao, bool Activa, Task Mudou) LerAgente(int terminalId)
    {
        lock (trinco)
        {
            var b = Obter(terminalId);
            return (b.VersaoAgente, b.ActivaAte > DateTime.UtcNow, b.MudouAgente.Task);
        }
    }

    public (long Versao, LeituraAgente? Leitura, Task Mudou) LerLeitura(int terminalId)
    {
        lock (trinco)
        {
            var b = Obter(terminalId);
            var agora = DateTime.UtcNow;
            LeituraAgente? leitura = b.Leitura is not null && agora - b.LidaEm <= LeituraValida ? b.Leitura
                : agora - b.ActivaDesde >= SemResposta ? new LeituraAgente(null, false, "A balança não responde: confirme que está ligada e que o agente do posto está a correr.")
                : null;
            return (b.VersaoLeitura, leitura, b.MudouLeitura.Task);
        }
    }

    /// <summary>O POS está a pedir o peso: o agente lê a balança até <paramref name="duracao"/> depois do último pedido.</summary>
    public void Activar(int terminalId, TimeSpan duracao)
    {
        TaskCompletionSource? acordar = null;
        lock (trinco)
        {
            var b = Obter(terminalId);
            var agora = DateTime.UtcNow;
            if (b.ActivaAte <= agora)
            {
                b.ActivaDesde = agora;
                b.Leitura = null;
                b.VersaoAgente = ++contador;
                acordar = b.MudouAgente;
                b.MudouAgente = Novo();
            }
            b.ActivaAte = agora + duracao;
        }
        acordar?.TrySetResult();
    }

    /// <summary>Leitura do agente; devolve se o POS continua a pedir o peso.</summary>
    public bool Registar(int terminalId, LeituraAgente leitura)
    {
        TaskCompletionSource acordar;
        bool activa;
        lock (trinco)
        {
            var b = Obter(terminalId);
            b.Leitura = leitura;
            b.LidaEm = DateTime.UtcNow;
            b.VersaoLeitura = ++contador;
            acordar = b.MudouLeitura;
            b.MudouLeitura = Novo();
            activa = b.ActivaAte > DateTime.UtcNow;
        }
        acordar.TrySetResult();
        return activa;
    }

    /// <summary>A configuração da balança mudou: o agente recebe-a logo; o POS volta a perguntar.</summary>
    public void ConfiguracaoMudou(int terminalId)
    {
        TaskCompletionSource a, l;
        lock (trinco)
        {
            var b = Obter(terminalId);
            b.Leitura = null;
            b.VersaoAgente = ++contador;
            b.VersaoLeitura = ++contador;
            (a, l) = (b.MudouAgente, b.MudouLeitura);
            b.MudouAgente = Novo();
            b.MudouLeitura = Novo();
        }
        a.TrySetResult();
        l.TrySetResult();
    }

    /// <summary>Espera até a versão lida por <paramref name="ler"/> ser diferente de <paramref name="versao"/> ou acabar o tempo.</summary>
    public static async Task EsperarAsync(Func<(long Versao, Task Mudou)> ler, long versao, TimeSpan espera, CancellationToken ct)
    {
        var limite = DateTime.UtcNow + espera;
        while (true)
        {
            var (actual, mudou) = ler();
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
/// Balança: etiquetas com peso ou preço lidas pelo leitor de código de barras (o servidor descodifica e escolhe o produto)
/// e balança ligada ao posto (o agente do posto lê-a na porta COM enquanto o POS pede o peso; o peso chega ao POS por pedido longo).
/// </summary>
public class ServicoBalanca(IAppDbContext db, ConfiguracoesSistema config, ServicoCatalogo catalogo, EstadoBalancas estados, IAuditoria auditoria)
{
    public static readonly int[] Velocidades = [1200, 2400, 4800, 9600, 19200, 38400];
    /// <summary>Peso máximo aceite de uma leitura (kg); acima disto é lixo da porta série.</summary>
    public const decimal PesoMaximo = 9999m;

    public async Task<FormatoEtiquetaBalanca?> FormatoAsync()
    {
        if (!await config.BoolAsync(ChavesConfiguracao.BalancaEtiquetas))
            return null;
        var prefixos = FormatoEtiquetaBalanca.LerPrefixos(await config.TextoAsync(ChavesConfiguracao.BalancaPrefixos, "2")) ?? ["2"];
        var digitos = int.TryParse(await config.TextoAsync(ChavesConfiguracao.BalancaDigitosCodigo, "5"), NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d is >= 4 and <= 6 ? d : 5;
        var preco = await config.TextoAsync(ChavesConfiguracao.BalancaConteudo, "peso") == "preco";
        var decimais = int.TryParse(await config.TextoAsync(ChavesConfiguracao.BalancaDecimaisPreco, "0"), NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c is >= 0 and <= 2 ? c : 0;
        return new FormatoEtiquetaBalanca(prefixos, digitos, preco, decimais);
    }

    /// <summary>Produto e quantidade da etiqueta. 404 se o código não for uma etiqueta da balança (ou as etiquetas estiverem desligadas).</summary>
    public async Task<ProdutoEtiquetaDto> EtiquetaAsync(string codigo, CancellationToken ct = default)
    {
        var formato = await FormatoAsync();
        var etiqueta = formato?.Ler(codigo) ?? throw new NaoEncontradoException($"Código {codigo} não encontrado.");
        if (!etiqueta.ControloValido)
            throw new RegraNegocioException("Etiqueta da balança mal lida (o algarismo de controlo não confere). Leia-a de novo.");
        var p = await db.Produtos.AsNoTracking().Where(x => x.CodigoBalanca == etiqueta.Codigo && x.Activo)
            .Select(x => new { x.Id, x.Nome, x.PrecoVenda, Dimensao = x.UnidadeMedida != null ? x.UnidadeMedida.Dimensao : (DimensaoUnidade?)null, Factor = x.UnidadeMedida != null ? x.UnidadeMedida.FactorParaBase : 1m })
            .FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException($"Nenhum produto activo tem o código de balança {etiqueta.Codigo}. Indique-o na ficha do produto («Código na balança»).");
        if (etiqueta.Valor <= 0)
            throw new RegraNegocioException($"A etiqueta de {p.Nome} não tem {(formato!.Preco ? "preço" : "peso")}.");

        decimal quantidade;
        if (formato!.Preco)
        {
            if (p.PrecoVenda <= 0)
                throw new RegraNegocioException($"{p.Nome} não tem preço de venda: não é possível calcular a quantidade da etiqueta.");
            quantidade = etiqueta.Valor / p.PrecoVenda;
        }
        else
        {
            if (p.Dimensao != DimensaoUnidade.Massa || p.Factor <= 0)
                throw new RegraNegocioException($"{p.Nome} não é vendido a peso: mude a unidade do produto para kg.");
            quantidade = etiqueta.Valor / p.Factor; // kg → unidade do produto (ex.: g)
        }
        quantidade = Math.Max(0.001m, decimal.Round(quantidade, 3, MidpointRounding.AwayFromZero));
        return new ProdutoEtiquetaDto(await catalogo.ObterProdutoAsync(p.Id), quantidade, formato.Preco ? etiqueta.Valor : null);
    }

    public async Task ConfigurarAsync(int terminalId, ConfigurarBalanca dados, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (!Enum.IsDefined(dados.Modo) || !Enum.IsDefined(dados.Formato))
            throw new RegraNegocioException("Tipo de balança inválido.");
        var porta = string.IsNullOrWhiteSpace(dados.Porta) ? null : dados.Porta.Trim().ToUpperInvariant();
        if (dados.Modo != ModoBalanca.Nenhuma)
        {
            if (porta is null || !Regex.IsMatch(porta, "^COM[0-9]{1,3}$"))
                throw new RegraNegocioException("Indique a porta COM da balança (ex.: COM4).");
            if (!Velocidades.Contains(dados.Velocidade))
                throw new RegraNegocioException("Velocidade inválida.");
            if (porta == t.VisorPorta && t.VisorTipo == TipoVisor.Poste)
                throw new RegraNegocioException($"A porta {porta} é a do visor do cliente: a balança tem de estar noutra porta.");
            t.BalancaPorta = porta;
            t.BalancaVelocidade = dados.Velocidade;
            t.BalancaFormato = dados.Formato;
        }
        t.BalancaModo = dados.Modo;
        auditoria.Registar("posto.balanca", "Terminal", t.Id, new { t.Nome, t.Numero, dados.Modo, Porta = t.BalancaPorta, t.BalancaVelocidade, t.BalancaFormato });
        await db.SaveChangesAsync(ct);
        estados.ConfiguracaoMudou(terminalId);
    }

    /// <summary>Configuração para o agente; espera (pedido longo) enquanto não mudar nem o POS pedir o peso.</summary>
    public async Task<BalancaAgenteDto> ParaAgenteAsync(int terminalId, long versao, TimeSpan espera, CancellationToken ct = default)
    {
        await EstadoBalancas.EsperarAsync(() => { var (v, _, m) = estados.LerAgente(terminalId); return (v, m); }, versao, espera, ct);
        var (actual, activa, _) = estados.LerAgente(terminalId);
        var t = await db.Terminais.AsNoTracking().Where(x => x.Id == terminalId)
            .Select(x => new { x.BalancaModo, x.BalancaPorta, x.BalancaVelocidade, x.BalancaFormato }).FirstAsync(ct);
        return new BalancaAgenteDto(actual, t.BalancaModo, t.BalancaPorta, t.BalancaVelocidade, t.BalancaFormato, activa && t.BalancaModo != ModoBalanca.Nenhuma);
    }

    /// <summary>Leitura do agente (peso arredondado ao grama; valores fora do razoável contam como erro).</summary>
    public RespostaLeitura RegistarLeitura(int terminalId, LeituraAgente leitura)
    {
        var erro = leitura.Erro is { Length: > 0 } e ? e[..Math.Min(e.Length, 200)] : null;
        decimal? peso = leitura.Peso is decimal p ? decimal.Round(p, 3, MidpointRounding.AwayFromZero) : null;
        if (peso is < 0 or > PesoMaximo)
            (peso, erro) = (null, peso < 0 ? "Peso negativo: verifique a tara da balança." : "Peso acima do máximo da balança.");
        return new RespostaLeitura(estados.Registar(terminalId, new LeituraAgente(erro is null ? peso : null, erro is null && peso is not null && leitura.Estavel, erro)));
    }

    /// <summary>
    /// Peso para o POS: pede-o ao agente (que lê a balança enquanto houver pedidos) e espera até haver uma leitura nova
    /// ou acabar o tempo. Sem balança configurada responde logo (o POS deixa escrever o peso).
    /// </summary>
    public async Task<LeituraBalancaDto> LeituraParaPosAsync(int terminalId, long versao, TimeSpan espera, CancellationToken ct = default)
    {
        var modo = await db.Terminais.AsNoTracking().Where(x => x.Id == terminalId && x.Activo).Select(x => (ModoBalanca?)x.BalancaModo).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (modo == ModoBalanca.Nenhuma)
            return new LeituraBalancaDto(0, false, null, false, null);
        estados.Activar(terminalId, espera + TimeSpan.FromSeconds(10));
        await EstadoBalancas.EsperarAsync(() => { var (v, _, m) = estados.LerLeitura(terminalId); return (v, m); }, versao, espera, ct);
        var (actual, leitura, _) = estados.LerLeitura(terminalId);
        return new LeituraBalancaDto(actual, true, leitura?.Peso, leitura?.Estavel ?? false, leitura?.Erro);
    }
}
