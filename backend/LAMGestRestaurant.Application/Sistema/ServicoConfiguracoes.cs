using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Sistema;

public record ConfiguracaoDto(string Chave, string Valor, string? Descricao);

/// <summary>Configurações sem segredos, usadas por qualquer terminal (nome, moeda, fuso, logótipo para impressão pelo navegador).</summary>
public record ConfiguracoesPublicas(string NomeEstabelecimento, string Moeda, string SimboloMoeda, string FusoHorario, string? Logotipo,
    OpcoesPos? Pos = null, bool Logotipo80mm = true, IReadOnlyDictionary<string, int>? Copias = null);

/// <summary>Botões activos no ecrã POS e leitura de código de barras.</summary>
public record OpcoesPos(bool Mesa, bool TakeAway, bool Delivery, bool Balcao, bool CodigoBarras);

public record GuardarMetodoPagamento(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")] string Nome,
    bool PermiteTroco,
    bool ExigeReferencia,
    bool ContaNaGaveta,
    [Range(0, 999, ErrorMessage = "A ordem deve estar entre 0 e 999.")] int Ordem);

public record GuardarTaxa(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(200)] string Nome,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A percentagem deve estar entre 0 e 100.")] decimal Percentagem);

/// <summary>
/// Configurações do sistema. Só são aceites as chaves conhecidas, cada uma validada pelo seu tipo; as alterações
/// ficam auditadas (valor anterior e novo). Taxas de imposto alteradas só afectam vendas futuras (os itens
/// vendidos guardam a taxa aplicada).
/// </summary>
public class ServicoConfiguracoes(IAppDbContext db, IAuditoria auditoria, TimeProvider relogio, IUtilizadorActual actual)
{
    private static readonly Dictionary<string, Func<string, string?>> Validadores = CriarValidadores();

    private static Dictionary<string, Func<string, string?>> CriarValidadores()
    {
        var validadores = ValidadoresBase();
        foreach (var chave in new[] { ChavesConfiguracao.FechoImprimeProdutos, ChavesConfiguracao.FechoContagemCega, ChavesConfiguracao.ExigirDiaAberto, ChavesConfiguracao.Logotipo80mm, ChavesConfiguracao.PosCodigoBarras }.Concat(ChavesConfiguracao.ModosPos))
            validadores[chave] = v => v is "true" or "false" ? null : "Use true ou false.";
        validadores[ChavesConfiguracao.EtiquetaLargura] = v => Decimal(v, Impressao.ServicoEtiquetas.LarguraMinima) && Impressao.ServicoEtiquetas.Mm(v) <= Impressao.ServicoEtiquetas.LarguraMaxima
            ? null : "A largura da etiqueta é de 20 a 150 mm.";
        validadores[ChavesConfiguracao.EtiquetaAltura] = v => Decimal(v, Impressao.ServicoEtiquetas.AlturaMinima) && Impressao.ServicoEtiquetas.Mm(v) <= Impressao.ServicoEtiquetas.AlturaMaxima
            ? null : "A altura da etiqueta é de 10 a 150 mm.";
        validadores[ChavesConfiguracao.EtiquetaCampos] = Impressao.ServicoEtiquetas.ValidarCampos;
        foreach (var chave in ChavesConfiguracao.Copias)
            validadores[chave] = v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 10 ? null : "O número de cópias é de 1 a 10.";
        return validadores;
    }

    private static Dictionary<string, Func<string, string?>> ValidadoresBase() => new()
    {
        [ChavesConfiguracao.NomeEstabelecimento] = v => Texto(v, 200),
        [ChavesConfiguracao.Endereco] = v => Texto(v, 1000),
        [ChavesConfiguracao.Telefone] = v => Texto(v, 200),
        [ChavesConfiguracao.Nuit] = v => v.Length == 0 || (v.Length == 9 && v.All(char.IsDigit)) ? null : "O NUIT tem 9 algarismos.",
        [ChavesConfiguracao.LogotipoUrl] = v => v.Length == 0 || EnderecoImagem.TentarId(v, out _) ? null : "Logótipo inválido: carregue a imagem nas configurações.",
        [ChavesConfiguracao.Moeda] = v => v.Length == 3 && v.All(char.IsLetter) ? null : "Código de moeda ISO com 3 letras (ex.: MZN).",
        [ChavesConfiguracao.SimboloMoeda] = v => v.Length is >= 1 and <= 5 ? null : "Símbolo com 1 a 5 caracteres.",
        [ChavesConfiguracao.FusoHorario] = v => FusoValido(v) ? null : "Fuso horário desconhecido (ex.: Africa/Maputo).",
        [ChavesConfiguracao.TaxaEntregaPadrao] = v => Decimal(v, 0) ? null : "Taxa de entrega inválida (use ponto decimal, zero ou positiva).",
        [ChavesConfiguracao.PermitirStockNegativo] = v => v is "true" or "false" ? null : "Use true ou false.",
        [ChavesConfiguracao.FidelizacaoActiva] = v => v is "true" or "false" ? null : "Use true ou false.",
        [ChavesConfiguracao.PontosPorMoeda] = v => Decimal(v, 0.01m) ? null : "O valor por ponto tem de ser positivo.",
        [ChavesConfiguracao.RodapeRecibo] = v => Texto(v, 1000),
        [ChavesConfiguracao.ImprimirReciboAoPagar] = v => v is "true" or "false" ? null : "Use true ou false.",
        [ChavesConfiguracao.MotivoIsencao] = v => v.Length is >= 3 and <= 300 ? null : "Indique o motivo da isenção (3 a 300 caracteres).",
        [ChavesConfiguracao.PrazoFacturaDias] = v => int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var d) && d is >= 0 and <= 365 ? null : "Prazo entre 0 e 365 dias.",
        [ChavesConfiguracao.ValidadeCotacaoDias] = v => int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var d) && d is >= 1 and <= 365 ? null : "Validade entre 1 e 365 dias.",
    };

    public async Task<IReadOnlyList<ConfiguracaoDto>> ListarAsync(CancellationToken ct) =>
        await db.Configuracoes.AsNoTracking().OrderBy(c => c.Chave).Select(c => new ConfiguracaoDto(c.Chave, c.Valor, c.Descricao)).ToListAsync(ct);

    public async Task<ConfiguracoesPublicas> PublicasAsync(CancellationToken ct)
    {
        var mapa = await db.Configuracoes.AsNoTracking().ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);
        string Ler(string k, string p) => mapa.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : p;
        bool Sim(string k) => !bool.TryParse(Ler(k, ""), out var v) || v;
        return new ConfiguracoesPublicas(Ler(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant"), Ler(ChavesConfiguracao.Moeda, "MZN"),
            Ler(ChavesConfiguracao.SimboloMoeda, "MT"), Ler(ChavesConfiguracao.FusoHorario, "Africa/Maputo"),
            EnderecoImagem.TentarId(Ler(ChavesConfiguracao.LogotipoUrl, ""), out _) ? Ler(ChavesConfiguracao.LogotipoUrl, "") : null,
            new OpcoesPos(Sim(ChavesConfiguracao.PosMesa), Sim(ChavesConfiguracao.PosTakeAway), Sim(ChavesConfiguracao.PosDelivery), Sim(ChavesConfiguracao.PosBalcao), Sim(ChavesConfiguracao.PosCodigoBarras)),
            Sim(ChavesConfiguracao.Logotipo80mm),
            ChavesConfiguracao.Copias.ToDictionary(k => k[ChavesConfiguracao.PrefixoCopias.Length..],
                k => int.TryParse(Ler(k, "1"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, 10) : 1));
    }

    public async Task<IReadOnlyList<ConfiguracaoDto>> GuardarAsync(IReadOnlyDictionary<string, string> valores)
    {
        var erros = new List<string>();
        foreach (var (chave, valor) in valores)
        {
            if (!Validadores.TryGetValue(chave, out var validar)) erros.Add($"Configuração desconhecida: {chave}.");
            else if (validar((valor ?? "").Trim()) is string erro) erros.Add($"{chave}: {erro}");
        }
        if (erros.Count > 0) throw new RegraNegocioException(string.Join(" ", erros));
        // O NUIT vem da licença: mudá-lo deixaria o sistema sem licença válida.
        if (valores.TryGetValue(ChavesConfiguracao.Nuit, out var novoNuit)
            && await db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).ThenByDescending(l => l.Id).Select(l => l.Nuit).FirstOrDefaultAsync() is { } licenciado
            && (novoNuit ?? "").Trim() != licenciado)
            throw new RegraNegocioException($"O NUIT é o da licença ({licenciado}). Para o mudar, active uma licença emitida para o novo NUIT.");

        var existentes = await db.Configuracoes.ToDictionaryAsync(c => c.Chave);
        // O POS tem de ficar com pelo menos uma forma de vender.
        if (ChavesConfiguracao.ModosPos.Any(valores.ContainsKey)
            && ChavesConfiguracao.ModosPos.All(k => (valores.TryGetValue(k, out var novo) ? novo?.Trim() : existentes.GetValueOrDefault(k)?.Valor) == "false"))
            throw new RegraNegocioException("Deixe pelo menos um botão activo no POS (Mesa, Take Away, Delivery ou Balcão).");
        var alteracoes = new List<object>();
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var (chave, valorBruto) in valores)
        {
            var valor = (valorBruto ?? "").Trim();
            if (!existentes.TryGetValue(chave, out var c))
                c = db.Configuracoes.Add(new Configuracao { Chave = chave }).Entity;
            if (c.Valor == valor) continue;
            alteracoes.Add(new { chave, Anterior = c.Valor, Novo = valor });
            c.Valor = valor;
            c.AlteradoEmUtc = agora;
            c.AlteradoPorId = actual.Id;
        }
        if (alteracoes.Count > 0)
        {
            auditoria.Registar("configuracoes.alterar", "Configuracao", null, alteracoes);
            await db.SaveChangesAsync();
        }
        return await ListarAsync(default);
    }

    public async Task<TaxaImposto> GuardarTaxaAsync(int? id, GuardarTaxa dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.TaxasImposto.AnyAsync(t => t.Id != id && t.Nome == nome))
            throw new ConflitoException("Já existe uma taxa com este nome.");
        var taxa = id is int e ? await db.TaxasImposto.FindAsync(e) ?? throw new NaoEncontradoException("Taxa não encontrada.") : db.TaxasImposto.Add(new TaxaImposto()).Entity;
        var anterior = taxa.Percentagem;
        taxa.Nome = nome;
        taxa.Percentagem = dados.Percentagem;
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "taxa.criar" : "taxa.editar", "TaxaImposto", taxa.Id, new { nome, Anterior = id is null ? (decimal?)null : anterior, Nova = dados.Percentagem });
        await db.SaveChangesAsync();
        return taxa;
    }

    public async Task DefinirTaxaActivaAsync(int id, bool activa)
    {
        var taxa = await db.TaxasImposto.FindAsync(id) ?? throw new NaoEncontradoException("Taxa não encontrada.");
        if (!activa && await db.Produtos.AnyAsync(p => p.TaxaImpostoId == id && p.Activo))
            throw new RegraNegocioException("Há produtos activos com esta taxa. Altere-os primeiro.");
        taxa.Activo = activa;
        auditoria.Registar(activa ? "taxa.activar" : "taxa.desactivar", "TaxaImposto", id, null);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cria ou edita um modo de pagamento. O código (usado nos relatórios e na auditoria) é gerado a partir do nome na criação
    /// e não muda depois. Modos de pagamento nunca são eliminados: desactivam-se (mantêm o histórico de pagamentos).
    /// </summary>
    public async Task<MetodoPagamentoDto> GuardarMetodoAsync(int? id, GuardarMetodoPagamento dados)
    {
        var nome = dados.Nome.Trim();
        if (dados.PermiteTroco && !dados.ContaNaGaveta)
            throw new RegraNegocioException("Só um modo de pagamento que conta na gaveta (dinheiro físico) pode dar troco.");
        if (await db.MetodosPagamento.AnyAsync(m => m.Id != id && m.Nome == nome))
            throw new ConflitoException("Já existe um modo de pagamento com este nome.");

        var metodo = id is int e
            ? await db.MetodosPagamento.FindAsync(e) ?? throw new NaoEncontradoException("Modo de pagamento não encontrado.")
            : db.MetodosPagamento.Add(new MetodoPagamento { Codigo = await CodigoLivreAsync(nome) }).Entity;
        var anterior = id is null ? null : new { metodo.Nome, metodo.PermiteTroco, metodo.ExigeReferencia, metodo.ContaNaGaveta, metodo.Ordem };
        metodo.Nome = nome;
        metodo.PermiteTroco = dados.PermiteTroco;
        metodo.ExigeReferencia = dados.ExigeReferencia;
        metodo.ContaNaGaveta = dados.ContaNaGaveta;
        metodo.Ordem = dados.Ordem;
        await using var transacao = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "metodo-pagamento.criar" : "metodo-pagamento.editar", "MetodoPagamento", metodo.Id,
            new { metodo.Codigo, Anterior = anterior, Novo = dados });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new MetodoPagamentoDto(metodo.Id, metodo.Codigo, metodo.Nome, metodo.PermiteTroco, metodo.ExigeReferencia, metodo.ContaNaGaveta, metodo.Activo, metodo.Ordem);
    }

    /// <summary>Código em maiúsculas sem acentos nem espaços (ex.: «Cartão VISA» → CARTAOVISA), único.</summary>
    private async Task<string> CodigoLivreAsync(string nome)
    {
        var semAcentos = new string(nome.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsAsciiLetterOrDigit(c))
            .ToArray()).ToUpperInvariant();
        var baseCodigo = semAcentos.Length == 0 ? "METODO" : semAcentos[..Math.Min(semAcentos.Length, 20)];
        var codigo = baseCodigo;
        for (var n = 2; await db.MetodosPagamento.AnyAsync(m => m.Codigo == codigo); n++)
            codigo = $"{baseCodigo[..Math.Min(baseCodigo.Length, 17)]}{n}";
        return codigo;
    }

    public async Task DefinirMetodoActivoAsync(int id, bool activo)
    {
        var metodo = await db.MetodosPagamento.FindAsync(id) ?? throw new NaoEncontradoException("Método não encontrado.");
        if (!activo && !await db.MetodosPagamento.AnyAsync(m => m.Id != id && m.Activo))
            throw new RegraNegocioException("Tem de existir pelo menos um método de pagamento activo.");
        metodo.Activo = activo;
        auditoria.Registar(activo ? "metodo-pagamento.activar" : "metodo-pagamento.desactivar", "MetodoPagamento", id, new { metodo.Codigo });
        await db.SaveChangesAsync();
    }

    private static string? Texto(string v, int max) => v.Length <= max ? null : $"Máximo {max} caracteres.";
    // Só ponto decimal, sem separadores de milhares: com NumberStyles.Number, "50,5" seria lido como 505.
    private static bool Decimal(string v, decimal minimo) =>
        decimal.TryParse(v, ConfiguracoesSistema.EstiloDecimal, CultureInfo.InvariantCulture, out var d) && d >= minimo;
    private static bool FusoValido(string v)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(v); return true; }
        catch (Exception) { return false; }
    }
}
