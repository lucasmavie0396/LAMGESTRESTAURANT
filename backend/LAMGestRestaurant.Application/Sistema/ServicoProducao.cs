using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Sistema;

/// <summary>Dados gerados pela utilização (os que a limpeza dos testes apaga).</summary>
public sealed record ContagemDados(int Pedidos, int Documentos, int Clientes, int CartoesEvento, int SessoesCaixa, int MovimentosStock, int Inventarios,
    int Compras, int Despesas, int Reservas, DateTime? PrimeiroDocumentoUtc);

/// <summary><see cref="Dados"/> só para quem gere as configurações.</summary>
public sealed record EstadoProducao(bool EmProducao, DateTime? DesdeUtc, ContagemDados? Dados);

public sealed record ResultadoProducao(DateTime DesdeUtc, bool Apagou, string? CopiaSeguranca, ContagemDados Apagados);

/// <summary>
/// Depois da instalação o sistema está em modo de testes. O administrador inicia a produção uma única vez: apagando os dados de teste
/// (vendas, documentos, caixa, clientes, cartões, stock…) ou mantendo-os. Configurações, produtos, utilizadores, impressoras e postos ficam.
/// Antes de apagar faz uma cópia de segurança da base de dados; tudo numa transacção. Depois de iniciada a produção não há volta:
/// os documentos fiscais nunca mais se apagam (regra 8).
/// </summary>
public class ServicoProducao(IAppDbContext db, IAuditoria auditoria, TimeProvider relogio)
{
    public const string FraseConfirmacao = "APAGAR DADOS DE TESTE";

    private async Task<DateTime?> DesdeAsync(CancellationToken ct)
    {
        var valor = await db.Configuracoes.AsNoTracking().Where(c => c.Chave == ChavesConfiguracao.ProducaoDesde).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    public async Task<EstadoProducao> EstadoAsync(bool comDados, CancellationToken ct = default)
    {
        var desde = await DesdeAsync(ct);
        return new EstadoProducao(desde is not null, desde, comDados && desde is null ? await ContarAsync(ct) : null);
    }

    private async Task<ContagemDados> ContarAsync(CancellationToken ct) => new(
        await db.Pedidos.CountAsync(ct), await db.Documentos.CountAsync(ct), await db.Clientes.CountAsync(ct), await db.CartoesEvento.CountAsync(ct),
        await db.SessoesCaixa.CountAsync(ct), await db.MovimentosStock.CountAsync(ct), await db.Inventarios.CountAsync(ct), await db.Compras.CountAsync(ct),
        await db.Despesas.CountAsync(ct), await db.Reservas.CountAsync(ct),
        await db.Documentos.MinAsync(d => (DateTime?)d.EmitidoEmUtc, ct));

    /// <summary>Inicia a produção (uma única vez). Com <paramref name="apagar"/>, apaga primeiro os dados de teste.</summary>
    public async Task<ResultadoProducao> IniciarAsync(bool apagar, string? confirmacao, CancellationToken ct = default)
    {
        if (await DesdeAsync(ct) is not null)
            throw new ConflitoException("O sistema já está em produção: os dados já não se podem apagar.");
        if (apagar && !string.Equals(confirmacao?.Trim(), FraseConfirmacao, StringComparison.Ordinal))
            throw new RegraNegocioException($"Para apagar os dados de teste escreva exactamente «{FraseConfirmacao}».");

        var contagem = await ContarAsync(ct);
        string? copia = null;
        if (apagar)
            copia = await CopiaSegurancaAsync(ct); // fora da transacção (o SQL Server não faz cópias dentro de uma)

        var agora = relogio.GetUtcNow().UtcDateTime;
        await using (var transacao = await db.Database.BeginTransactionAsync(ct))
        {
            if (apagar)
                await ApagarDadosAsync(ct);
            var registo = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == ChavesConfiguracao.ProducaoDesde, ct);
            if (registo is null)
                db.Configuracoes.Add(registo = new Configuracao { Chave = ChavesConfiguracao.ProducaoDesde, Descricao = "Data em que o sistema entrou em produção" });
            registo.Valor = agora.ToString("O", CultureInfo.InvariantCulture);
            registo.AlteradoEmUtc = agora;
            auditoria.Registar("sistema.iniciar-producao", "Sistema", null, new { ApagouDadosDeTeste = apagar, CopiaSeguranca = copia, Dados = contagem });
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        return new ResultadoProducao(agora, apagar, copia, apagar ? contagem : new ContagemDados(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null));
    }

    /// <summary>Cópia completa da base de dados na pasta de cópias do SQL Server (COPY_ONLY: não mexe na sequência das cópias agendadas).</summary>
    private async Task<string> CopiaSegurancaAsync(CancellationToken ct)
    {
        var baseDados = db.Database.GetDbConnection().Database;
        var pasta = await db.Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS Value").SingleAsync(ct);
        var ficheiro = Path.Combine(pasta, $"{baseDados}-antes-da-producao-{relogio.GetUtcNow().UtcDateTime:yyyyMMdd-HHmmss}.bak");
        var anterior = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        try
        {
            await db.Database.ExecuteSqlRawAsync($"BACKUP DATABASE [{baseDados.Replace("]", "]]")}] TO DISK = {{0}} WITH COPY_ONLY, INIT", [ficheiro], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RegraNegocioException($"Não foi possível fazer a cópia de segurança antes de apagar ({ex.Message}). Nada foi apagado.");
        }
        finally
        {
            db.Database.SetCommandTimeout(anterior);
        }
        return ficheiro;
    }

    /// <summary>Apaga os dados gerados pela utilização, dos dependentes para os principais.</summary>
    private async Task ApagarDadosAsync(CancellationToken ct)
    {
        await db.TrabalhosImpressaoPosto.ExecuteDeleteAsync(ct);
        // Documentos (VD, facturas, recibos, notas de crédito, cotações) e o que deles depende.
        await db.LiquidacoesFactura.ExecuteDeleteAsync(ct);
        await db.DocumentoLinhas.ExecuteDeleteAsync(ct);
        await db.Documentos.Where(d => d.DocumentoOrigemId != null).ExecuteUpdateAsync(s => s.SetProperty(d => d.DocumentoOrigemId, (int?)null), ct);
        // Cartões Evento.
        await db.MovimentosCartao.ExecuteDeleteAsync(ct);
        await db.DescargasCartoes.ExecuteDeleteAsync(ct);
        await db.Documentos.ExecuteDeleteAsync(ct);
        await db.CartoesEvento.ExecuteDeleteAsync(ct);
        // Vendas.
        await db.MovimentosPontos.ExecuteDeleteAsync(ct);
        await db.Pagamentos.ExecuteDeleteAsync(ct);
        await db.Recibos.ExecuteDeleteAsync(ct);
        await db.HistoricoPedidos.ExecuteDeleteAsync(ct);
        await db.TransferenciasMesa.ExecuteDeleteAsync(ct);
        await db.PedidoItens.ExecuteDeleteAsync(ct);
        await db.Pedidos.ExecuteDeleteAsync(ct);
        // Caixa e despesas.
        await db.MovimentosCaixa.ExecuteDeleteAsync(ct);
        await db.Despesas.ExecuteDeleteAsync(ct);
        await db.FechoCaixaLinhas.ExecuteDeleteAsync(ct);
        await db.SessoesCaixa.ExecuteDeleteAsync(ct);
        await db.DiasOperacao.ExecuteDeleteAsync(ct);
        // Stock e compras: os saldos ficam a zero (faz-se depois um inventário inicial).
        await db.MovimentosStock.ExecuteDeleteAsync(ct);
        await db.InventarioLinhas.ExecuteDeleteAsync(ct);
        await db.Inventarios.ExecuteDeleteAsync(ct);
        await db.CompraItens.ExecuteDeleteAsync(ct);
        await db.Compras.ExecuteDeleteAsync(ct);
        await db.StockSaldos.ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantidade, 0m), ct);
        // Reservas e clientes.
        await db.Reservas.ExecuteDeleteAsync(ct);
        await db.Clientes.ExecuteDeleteAsync(ct);
        // Numeração: a primeira venda real é a n.º 1 de cada série.
        await db.SeriesDocumento.ExecuteDeleteAsync(ct);
        // Sala: todas as mesas livres.
        await db.Mesas.ExecuteUpdateAsync(s => s.SetProperty(m => m.Estado, EstadoMesa.Livre).SetProperty(m => m.OcupadaDesdeUtc, (DateTime?)null), ct);
    }
}
