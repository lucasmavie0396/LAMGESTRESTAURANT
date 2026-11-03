using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary>
/// Sessões de caixa (turnos). Regras:
/// - uma sessão aberta por terminal (índice único na BD) e uma sessão aberta por utilizador;
/// - só quem abriu, ou quem tem caixa.ver-todas (gerente), consulta, movimenta e fecha a sessão;
/// - o valor esperado é calculado no servidor apenas com operações da própria sessão
///   (fundo + pagamentos válidos + entradas − saídas), nunca com o histórico global;
/// - o fecho grava a contagem por método e as diferenças; uma sessão fechada não se reabre.
/// </summary>
public class ServicoCaixa(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    IConsultaUtilizadores utilizadores,
    TimeProvider relogio)
{
    // ---------- Terminais e métodos ----------

    public async Task<IReadOnlyList<TerminalDto>> ListarTerminaisAsync(bool incluirInactivos, CancellationToken ct)
    {
        var terminais = await db.Terminais.AsNoTracking()
            .Where(t => incluirInactivos || t.Activo)
            .OrderBy(t => t.Numero == null).ThenBy(t => t.Numero).ThenBy(t => t.Nome)
            .Select(t => new
            {
                t.Id, t.Nome, t.Descricao, t.Activo, t.Numero,
                Sessao = db.SessoesCaixa.Where(s => s.TerminalId == t.Id && s.Estado == EstadoSessaoCaixa.Aberta)
                    .Select(s => new { s.Id, s.UtilizadorAberturaId }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(terminais.Where(t => t.Sessao != null).Select(t => t.Sessao!.UtilizadorAberturaId), ct);
        return [.. terminais.Select(t => new TerminalDto(t.Id, t.Nome, t.Descricao, t.Activo, t.Sessao?.Id,
            t.Sessao is null ? null : nomes.GetValueOrDefault(t.Sessao.UtilizadorAberturaId), t.Numero))];
    }

    public async Task<TerminalDto> CriarTerminalAsync(GuardarTerminal dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.Terminais.AnyAsync(t => t.Nome == nome))
            throw new ConflitoException("Já existe um terminal com este nome.");
        await VerificarNumeroLivreAsync(dados.Numero, null);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var terminal = new Terminal { Nome = nome, Descricao = dados.Descricao?.Trim(), Numero = dados.Numero };
        db.Terminais.Add(terminal);
        await db.SaveChangesAsync();
        auditoria.Registar("terminal.criar", "Terminal", terminal.Id, new { terminal.Nome, terminal.Numero });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new TerminalDto(terminal.Id, terminal.Nome, terminal.Descricao, true, null, null, terminal.Numero);
    }

    public async Task<TerminalDto> EditarTerminalAsync(int id, GuardarTerminal dados)
    {
        var terminal = await db.Terminais.FindAsync(id) ?? throw new NaoEncontradoException("Terminal não encontrado.");
        var nome = dados.Nome.Trim();
        if (await db.Terminais.AnyAsync(t => t.Id != id && t.Nome == nome))
            throw new ConflitoException("Já existe um terminal com este nome.");
        await VerificarNumeroLivreAsync(dados.Numero, id);
        terminal.Nome = nome;
        terminal.Descricao = dados.Descricao?.Trim();
        terminal.Numero = dados.Numero;
        auditoria.Registar("terminal.editar", "Terminal", id, new { terminal.Nome, terminal.Numero });
        await db.SaveChangesAsync();
        return (await ListarTerminaisAsync(true, default)).Single(t => t.Id == id);
    }

    private async Task VerificarNumeroLivreAsync(int? numero, int? excepto)
    {
        if (numero is int n && await db.Terminais.AnyAsync(t => t.Numero == n && t.Id != excepto))
            throw new ConflitoException($"Já existe um posto com o número {n}.");
    }

    /// <summary>Posto activo com este número (usado sem sessão pelo instalador e pelo atalho do posto).</summary>
    public async Task<PostoPublico> ObterPostoAsync(int numero, CancellationToken ct) =>
        await db.Terminais.AsNoTracking()
            .Where(t => t.Numero == numero && t.Activo)
            .Select(t => new PostoPublico(numero, t.Id, t.Nome))
            .FirstOrDefaultAsync(ct)
        ?? throw new NaoEncontradoException($"O posto {numero} não está registado no servidor (Caixa → Terminais).");

    public async Task DefinirTerminalActivoAsync(int id, bool activo)
    {
        var terminal = await db.Terminais.FindAsync(id) ?? throw new NaoEncontradoException("Terminal não encontrado.");
        if (!activo && await db.SessoesCaixa.AnyAsync(s => s.TerminalId == id && s.Estado == EstadoSessaoCaixa.Aberta))
            throw new RegraNegocioException("O terminal tem uma sessão de caixa aberta. Feche-a primeiro.");
        terminal.Activo = activo;
        auditoria.Registar(activo ? "terminal.activar" : "terminal.desactivar", "Terminal", id, new { terminal.Nome });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<MetodoPagamentoDto>> ListarMetodosAsync(CancellationToken ct) =>
        await db.MetodosPagamento.AsNoTracking().OrderBy(m => m.Ordem)
            .Select(m => new MetodoPagamentoDto(m.Id, m.Codigo, m.Nome, m.PermiteTroco, m.ExigeReferencia, m.ContaNaGaveta, m.Activo))
            .ToListAsync(ct);

    // ---------- Sessões ----------

    public async Task<SessaoCaixaDto> AbrirAsync(AbrirCaixa dados)
    {
        var utilizadorId = actual.Id ?? throw new RegraNegocioException("Sessão de utilizador inválida.");
        if (!await db.Terminais.AnyAsync(t => t.Id == dados.TerminalId && t.Activo))
            throw new RegraNegocioException("Terminal inexistente ou inactivo.");
        if (await db.SessoesCaixa.AnyAsync(s => s.TerminalId == dados.TerminalId && s.Estado == EstadoSessaoCaixa.Aberta))
            throw new ConflitoException("Já existe uma sessão de caixa aberta neste terminal.");
        if (await db.SessoesCaixa.AnyAsync(s => s.UtilizadorAberturaId == utilizadorId && s.Estado == EstadoSessaoCaixa.Aberta))
            throw new ConflitoException("Já tem uma sessão de caixa aberta noutro terminal. Feche-a antes de abrir outra.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var sessao = new SessaoCaixa
        {
            TerminalId = dados.TerminalId,
            UtilizadorAberturaId = utilizadorId,
            AbertaEmUtc = relogio.GetUtcNow().UtcDateTime,
            FundoInicial = decimal.Round(dados.FundoInicial, 2, MidpointRounding.AwayFromZero),
            ObservacoesAbertura = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim(),
        };
        db.SessoesCaixa.Add(sessao);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Duas aberturas simultâneas no mesmo terminal: o índice único da BD rejeita a segunda.
            throw new ConflitoException("Já existe uma sessão de caixa aberta neste terminal.");
        }
        auditoria.Registar("caixa.abrir", "SessaoCaixa", sessao.Id, new { sessao.TerminalId, sessao.FundoInicial });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(sessao.Id);
    }

    /// <summary>Sessão aberta no terminal indicado (ou null).</summary>
    public async Task<SessaoCaixaDto?> ObterAbertaDoTerminalAsync(int terminalId)
    {
        var id = await db.SessoesCaixa
            .Where(s => s.TerminalId == terminalId && s.Estado == EstadoSessaoCaixa.Aberta)
            .Select(s => (int?)s.Id).FirstOrDefaultAsync();
        return id is null ? null : await ObterAsync(id.Value);
    }

    public async Task<SessaoCaixaDto> ObterAsync(int id)
    {
        var sessao = await db.SessoesCaixa.AsNoTracking()
            .Include(s => s.Terminal)
            .Include(s => s.LinhasFecho)
            .FirstOrDefaultAsync(s => s.Id == id) ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.");
        GarantirAcesso(sessao);

        var resumo = await CalcularResumoAsync(sessao);
        var movimentos = await db.MovimentosCaixa.AsNoTracking()
            .Where(m => m.SessaoCaixaId == id)
            .OrderBy(m => m.DataUtc)
            .Select(m => new { m.Id, m.Tipo, Metodo = m.MetodoPagamento.Nome, m.Valor, m.Motivo, m.UtilizadorId, m.DataUtc })
            .ToListAsync();
        var pedidosPagos = await db.Pagamentos
            .Where(p => p.SessaoCaixaId == id && p.Estado == EstadoPagamento.Valido)
            .Select(p => p.PedidoId).Distinct().CountAsync();

        var ids = movimentos.Select(m => m.UtilizadorId).Append(sessao.UtilizadorAberturaId).ToList();
        if (sessao.UtilizadorFechoId is int fechoId)
            ids.Add(fechoId);
        var nomes = await utilizadores.NomesAsync(ids);

        var fechada = sessao.Estado == EstadoSessaoCaixa.Fechada;
        return new SessaoCaixaDto(
            sessao.Id, sessao.TerminalId, sessao.Terminal.Nome, sessao.Estado,
            nomes.GetValueOrDefault(sessao.UtilizadorAberturaId, "?"), sessao.AbertaEmUtc, sessao.FundoInicial, sessao.ObservacoesAbertura,
            sessao.UtilizadorFechoId is int uf ? nomes.GetValueOrDefault(uf) : null, sessao.FechadaEmUtc, sessao.ObservacoesFecho,
            pedidosPagos,
            resumo.Sum(r => r.Recebimentos),
            resumo.Sum(r => r.Esperado),
            fechada ? resumo.Sum(r => r.Contado ?? 0) : null,
            fechada ? resumo.Sum(r => r.Diferenca ?? 0) : null,
            resumo,
            [.. movimentos.Select(m => new MovimentoCaixaDto(m.Id, m.Tipo, m.Metodo, m.Valor, m.Motivo, nomes.GetValueOrDefault(m.UtilizadorId), m.DataUtc))],
            sessao.RowVersion);
    }

    public async Task<SessaoCaixaDto> RegistarMovimentoAsync(int sessaoId, RegistarMovimentoCaixa dados)
    {
        if (dados.Tipo is not (TipoMovimentoCaixa.Entrada or TipoMovimentoCaixa.Saida))
            throw new RegraNegocioException("Despesas pagas pela caixa registam-se no módulo de Despesas.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.");
        GarantirAcesso(sessao);
        if (sessao.Estado != EstadoSessaoCaixa.Aberta)
            throw new RegraNegocioException("A sessão de caixa está fechada.");
        var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                     ?? throw new RegraNegocioException("Método inexistente ou inactivo.");

        var valor = decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero);
        if (dados.Tipo == TipoMovimentoCaixa.Saida && metodo.ContaNaGaveta)
        {
            var disponivel = (await CalcularResumoAsync(sessao)).FirstOrDefault(r => r.MetodoPagamentoId == metodo.Id)?.Esperado ?? 0m;
            if (valor > disponivel)
                throw new RegraNegocioException($"Saída superior ao valor esperado na gaveta em {metodo.Nome} ({disponivel:N2}).");
        }

        db.MovimentosCaixa.Add(new MovimentoCaixa
        {
            SessaoCaixaId = sessaoId,
            Tipo = dados.Tipo,
            MetodoPagamentoId = metodo.Id,
            Valor = valor,
            Motivo = dados.Motivo.Trim(),
            UtilizadorId = actual.Id!.Value,
            DataUtc = relogio.GetUtcNow().UtcDateTime,
        });
        // Toca na sessão para que movimentos concorrentes e o fecho se serializem pela rowversion.
        sessao.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        auditoria.Registar(dados.Tipo == TipoMovimentoCaixa.Entrada ? "caixa.entrada" : "caixa.saida", "SessaoCaixa", sessaoId,
            new { Metodo = metodo.Codigo, Valor = valor, Motivo = dados.Motivo.Trim() });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(sessaoId);
    }

    public async Task<SessaoCaixaDto> FecharAsync(int sessaoId, FecharCaixa dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.");
        GarantirAcesso(sessao);
        if (sessao.Estado != EstadoSessaoCaixa.Aberta)
            throw new RegraNegocioException("A sessão de caixa já está fechada.");
        db.VerificarVersao(sessao, dados.RowVersion);

        var resumo = await CalcularResumoAsync(sessao);
        var contagens = dados.Contagens.GroupBy(c => c.MetodoPagamentoId).ToDictionary(g => g.Key, g => g.Last().ValorContado);
        var desconhecidos = contagens.Keys.Except(resumo.Select(r => r.MetodoPagamentoId)).ToList();
        if (desconhecidos.Count > 0)
            throw new RegraNegocioException("A contagem inclui métodos de pagamento inválidos.");
        var emFalta = resumo.Where(r => (r.ContaNaGaveta || r.Esperado != 0) && !contagens.ContainsKey(r.MetodoPagamentoId)).Select(r => r.Metodo).ToList();
        if (emFalta.Count > 0)
            throw new RegraNegocioException($"Indique o valor contado em: {string.Join(", ", emFalta)}.");

        foreach (var r in resumo)
        {
            var contado = decimal.Round(contagens.GetValueOrDefault(r.MetodoPagamentoId), 2, MidpointRounding.AwayFromZero);
            db.FechoCaixaLinhas.Add(new FechoCaixaLinha
            {
                SessaoCaixaId = sessaoId,
                MetodoPagamentoId = r.MetodoPagamentoId,
                ValorEsperado = r.Esperado,
                ValorContado = contado,
                Diferenca = contado - r.Esperado,
            });
        }

        sessao.Estado = EstadoSessaoCaixa.Fechada;
        sessao.FechadaEmUtc = relogio.GetUtcNow().UtcDateTime;
        sessao.UtilizadorFechoId = actual.Id;
        sessao.ObservacoesFecho = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim();

        var esperado = resumo.Sum(r => r.Esperado);
        var contadoTotal = resumo.Sum(r => decimal.Round(contagens.GetValueOrDefault(r.MetodoPagamentoId), 2, MidpointRounding.AwayFromZero));
        auditoria.Registar("caixa.fechar", "SessaoCaixa", sessaoId, new { Esperado = esperado, Contado = contadoTotal, Diferenca = contadoTotal - esperado });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(sessaoId);
    }

    public async Task<Pagina<SessaoCaixaResumo>> ListarSessoesAsync(FiltroSessoes f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<SessaoCaixaResumo>.Normalizar(f.Pagina, f.Tamanho);
        var consulta = db.SessoesCaixa.AsNoTracking();
        if (!actual.TemPermissao(Permissoes.CaixaVerTodas))
            consulta = consulta.Where(s => s.UtilizadorAberturaId == actual.Id);
        if (f.DesdeUtc is DateTime desde) consulta = consulta.Where(s => s.AbertaEmUtc >= desde);
        if (f.AteUtc is DateTime ate) consulta = consulta.Where(s => s.AbertaEmUtc < ate);
        if (f.TerminalId is int terminal) consulta = consulta.Where(s => s.TerminalId == terminal);
        if (f.Estado is { } estado) consulta = consulta.Where(s => s.Estado == estado);

        var total = await consulta.CountAsync(ct);
        var linhas = await consulta.OrderByDescending(s => s.AbertaEmUtc).ThenByDescending(s => s.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(s => new
            {
                s.Id, Terminal = s.Terminal.Nome, s.Estado, s.UtilizadorAberturaId, s.AbertaEmUtc, s.FechadaEmUtc, s.FundoInicial,
                Diferenca = s.Estado == EstadoSessaoCaixa.Fechada ? s.LinhasFecho.Sum(l => (decimal?)l.Diferenca) ?? 0m : (decimal?)null,
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Select(l => l.UtilizadorAberturaId), ct);
        return new Pagina<SessaoCaixaResumo>(
            [.. linhas.Select(l => new SessaoCaixaResumo(l.Id, l.Terminal, l.Estado, nomes.GetValueOrDefault(l.UtilizadorAberturaId, "?"),
                l.AbertaEmUtc, l.FechadaEmUtc, l.FundoInicial, l.Diferenca))],
            total, pagina, tamanho);
    }

    // ---------- Auxiliares ----------

    /// <summary>Valor esperado actualmente num método, nesta sessão (usado para validar saídas e reembolsos).</summary>
    internal async Task<decimal> EsperadoNoMetodoAsync(SessaoCaixa sessao, int metodoPagamentoId) =>
        (await CalcularResumoAsync(sessao)).FirstOrDefault(r => r.MetodoPagamentoId == metodoPagamentoId)?.Esperado ?? 0m;

    /// <summary>Garante que a sessão está aberta e pertence ao operador (ou que ele pode operar todas).</summary>
    internal void GarantirSessaoOperavel(SessaoCaixa sessao)
    {
        if (sessao.Estado != EstadoSessaoCaixa.Aberta)
            throw new RegraNegocioException("A sessão de caixa está fechada. Abra o caixa neste terminal.");
        GarantirAcesso(sessao);
    }

    private void GarantirAcesso(SessaoCaixa sessao)
    {
        if (sessao.UtilizadorAberturaId != actual.Id && !actual.TemPermissao(Permissoes.CaixaVerTodas))
            throw new AcessoNegadoException("Esta sessão de caixa pertence a outro utilizador.");
    }

    /// <summary>Totais por método, só com as operações desta sessão.</summary>
    private async Task<List<ResumoMetodo>> CalcularResumoAsync(SessaoCaixa sessao)
    {
        var metodos = await db.MetodosPagamento.AsNoTracking().OrderBy(m => m.Ordem).ToListAsync();
        var recebimentos = await db.Pagamentos.AsNoTracking()
            .Where(p => p.SessaoCaixaId == sessao.Id && p.Estado == EstadoPagamento.Valido)
            .GroupBy(p => p.MetodoPagamentoId)
            .Select(g => new { MetodoId = g.Key, Total = g.Sum(p => p.Valor) })
            .ToDictionaryAsync(x => x.MetodoId, x => x.Total);
        var movimentos = await db.MovimentosCaixa.AsNoTracking()
            .Where(m => m.SessaoCaixaId == sessao.Id)
            .GroupBy(m => new { m.MetodoPagamentoId, Entrada = m.Tipo == TipoMovimentoCaixa.Entrada })
            .Select(g => new { g.Key.MetodoPagamentoId, g.Key.Entrada, Total = g.Sum(m => m.Valor) })
            .ToListAsync();
        var linhasFecho = sessao.LinhasFecho.Count > 0
            ? sessao.LinhasFecho.ToDictionary(l => l.MetodoPagamentoId)
            : await db.FechoCaixaLinhas.AsNoTracking().Where(l => l.SessaoCaixaId == sessao.Id).ToDictionaryAsync(l => l.MetodoPagamentoId);

        // O fundo inicial é dinheiro físico: atribui-se ao primeiro método que conta na gaveta.
        var metodoFundo = metodos.FirstOrDefault(m => m.ContaNaGaveta)?.Id;

        return [.. metodos
            .Where(m => m.Activo || recebimentos.ContainsKey(m.Id) || movimentos.Any(x => x.MetodoPagamentoId == m.Id) || linhasFecho.ContainsKey(m.Id))
            .Select(m =>
            {
                var fundo = m.Id == metodoFundo ? sessao.FundoInicial : 0m;
                var receb = recebimentos.GetValueOrDefault(m.Id);
                var entradas = movimentos.Where(x => x.MetodoPagamentoId == m.Id && x.Entrada).Sum(x => x.Total);
                var saidas = movimentos.Where(x => x.MetodoPagamentoId == m.Id && !x.Entrada).Sum(x => x.Total);
                var linha = linhasFecho.GetValueOrDefault(m.Id);
                // Numa sessão fechada mostra-se o esperado gravado no fecho (não muda se algo for corrigido depois).
                var esperado = linha?.ValorEsperado ?? fundo + receb + entradas - saidas;
                return new ResumoMetodo(m.Id, m.Nome, m.ContaNaGaveta, fundo, receb, entradas, saidas, esperado, linha?.ValorContado, linha?.Diferenca);
            })];
    }
}
