using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sala;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Sala;

public record ZonaDto(int Id, string Nome, int Ordem, bool Activo, int NumeroMesas);

public record GuardarZona(
    [Required(ErrorMessage = "Indique o nome da zona.")]
    [StringLength(200, ErrorMessage = "O nome não pode exceder 200 caracteres.")]
    string Nome,
    [Range(0, 9999, ErrorMessage = "Ordem inválida.")] int Ordem);

public record MesaDto(
    int Id,
    string Nome,
    int ZonaId,
    string ZonaNome,
    int Capacidade,
    EstadoMesa Estado,
    DateTime? OcupadaDesdeUtc,
    bool Activo,
    int PedidosAbertos,
    /// <summary>0 numa mesa de outro responsável para quem não tem pos.mesas-outros (não vê o conteúdo).</summary>
    decimal TotalAberto,
    byte[] RowVersion,
    /// <summary>Utilizador responsável pelo pedido aberto (quem abriu a mesa, ou a quem foi passada).</summary>
    int? ResponsavelId = null,
    string? Responsavel = null);

public record GuardarMesa(
    [Required(ErrorMessage = "Indique o nome/número da mesa.")]
    [StringLength(30, ErrorMessage = "O nome não pode exceder 30 caracteres.")]
    string Nome,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a zona.")] int ZonaId,
    [Range(1, 100, ErrorMessage = "A capacidade deve estar entre 1 e 100 lugares.")] int Capacidade,
    byte[]? RowVersion);

public record AlterarEstadoMesa(EstadoMesa Estado, byte[]? RowVersion);

/// <summary>
/// Zonas e mesas. Os estados Ocupada/Reservada são geridos pelos pedidos e reservas;
/// manualmente só se alterna entre Livre e Indisponível.
/// </summary>
public class ServicoSala(IAppDbContext db, IAuditoria auditoria, IUtilizadorActual actual, IConsultaUtilizadores utilizadores)
{
    public async Task<IReadOnlyList<ZonaDto>> ListarZonasAsync(bool incluirInactivas, CancellationToken ct) =>
        await db.Zonas.AsNoTracking()
            .Where(z => incluirInactivas || z.Activo)
            .OrderBy(z => z.Ordem).ThenBy(z => z.Nome)
            .Select(z => new ZonaDto(z.Id, z.Nome, z.Ordem, z.Activo, db.Mesas.Count(m => m.ZonaId == z.Id && m.Activo)))
            .ToListAsync(ct);

    public async Task<ZonaDto> CriarZonaAsync(GuardarZona dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.Zonas.AnyAsync(z => z.Nome == nome))
            throw new ConflitoException("Já existe uma zona com este nome.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var zona = new Zona { Nome = nome, Ordem = dados.Ordem };
        db.Zonas.Add(zona);
        await db.SaveChangesAsync();
        auditoria.Registar("zona.criar", "Zona", zona.Id, new { zona.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new ZonaDto(zona.Id, zona.Nome, zona.Ordem, zona.Activo, 0);
    }

    public async Task<ZonaDto> EditarZonaAsync(int id, GuardarZona dados)
    {
        var zona = await db.Zonas.FindAsync(id) ?? throw new NaoEncontradoException("Zona não encontrada.");
        var nome = dados.Nome.Trim();
        if (await db.Zonas.AnyAsync(z => z.Id != id && z.Nome == nome))
            throw new ConflitoException("Já existe uma zona com este nome.");
        zona.Nome = nome;
        zona.Ordem = dados.Ordem;
        auditoria.Registar("zona.editar", "Zona", id, new { zona.Nome });
        await db.SaveChangesAsync();
        return (await ListarZonasAsync(true, default)).Single(z => z.Id == id);
    }

    public async Task DefinirZonaActivaAsync(int id, bool activa)
    {
        var zona = await db.Zonas.FindAsync(id) ?? throw new NaoEncontradoException("Zona não encontrada.");
        if (!activa && await db.Mesas.AnyAsync(m => m.ZonaId == id && m.Activo))
            throw new RegraNegocioException("A zona tem mesas activas. Desactive-as ou mude-as de zona primeiro.");
        zona.Activo = activa;
        auditoria.Registar(activa ? "zona.activar" : "zona.desactivar", "Zona", id, new { zona.Nome });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<MesaDto>> ListarMesasAsync(int? zonaId, bool incluirInactivas, CancellationToken ct) =>
        await Projectar(db.Mesas.AsNoTracking()
                .Where(m => (incluirInactivas || m.Activo) && (zonaId == null || m.ZonaId == zonaId)), ct);

    public async Task<MesaDto> ObterMesaAsync(int id) =>
        (await Projectar(db.Mesas.AsNoTracking().Where(m => m.Id == id))).FirstOrDefault()
        ?? throw new NaoEncontradoException("Mesa não encontrada.");

    public async Task<MesaDto> CriarMesaAsync(GuardarMesa dados)
    {
        var nome = dados.Nome.Trim();
        await ValidarMesa(0, nome, dados.ZonaId);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var mesa = new Mesa { Nome = nome, ZonaId = dados.ZonaId, Capacidade = dados.Capacidade };
        db.Mesas.Add(mesa);
        await db.SaveChangesAsync();
        auditoria.Registar("mesa.criar", "Mesa", mesa.Id, new { mesa.Nome, mesa.ZonaId, mesa.Capacidade });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterMesaAsync(mesa.Id);
    }

    public async Task<MesaDto> EditarMesaAsync(int id, GuardarMesa dados)
    {
        var mesa = await db.Mesas.FindAsync(id) ?? throw new NaoEncontradoException("Mesa não encontrada.");
        db.VerificarVersao(mesa, dados.RowVersion);
        var nome = dados.Nome.Trim();
        await ValidarMesa(id, nome, dados.ZonaId);
        mesa.Nome = nome;
        mesa.ZonaId = dados.ZonaId;
        mesa.Capacidade = dados.Capacidade;
        auditoria.Registar("mesa.editar", "Mesa", id, new { mesa.Nome, mesa.ZonaId, mesa.Capacidade });
        await db.SaveChangesAsync();
        return await ObterMesaAsync(id);
    }

    public async Task<MesaDto> AlterarEstadoAsync(int id, AlterarEstadoMesa dados)
    {
        var mesa = await db.Mesas.FindAsync(id) ?? throw new NaoEncontradoException("Mesa não encontrada.");
        db.VerificarVersao(mesa, dados.RowVersion);
        if (dados.Estado is not (EstadoMesa.Livre or EstadoMesa.Indisponivel))
            throw new RegraNegocioException("Os estados Ocupada e Reservada são definidos pelos pedidos e reservas.");
        if (mesa.Estado is EstadoMesa.Ocupada || await TemPedidoAberto(id))
            throw new RegraNegocioException("A mesa tem um pedido em aberto. Feche ou transfira o pedido primeiro.");
        if (mesa.Estado is EstadoMesa.Reservada && dados.Estado is EstadoMesa.Livre)
            throw new RegraNegocioException("A mesa está reservada. Altere a reserva para a libertar.");

        var anterior = mesa.Estado;
        mesa.Estado = dados.Estado;
        mesa.OcupadaDesdeUtc = null;
        auditoria.Registar("mesa.estado", "Mesa", id, new { mesa.Nome, Anterior = anterior.ToString(), Novo = dados.Estado.ToString() });
        await db.SaveChangesAsync();
        return await ObterMesaAsync(id);
    }

    public async Task<MesaDto> DefinirMesaActivaAsync(int id, bool activa, byte[]? versao)
    {
        var mesa = await db.Mesas.FindAsync(id) ?? throw new NaoEncontradoException("Mesa não encontrada.");
        db.VerificarVersao(mesa, versao);
        if (!activa && (mesa.Estado is EstadoMesa.Ocupada or EstadoMesa.Reservada || await TemPedidoAberto(id)))
            throw new RegraNegocioException("Só é possível desactivar uma mesa livre ou indisponível, sem pedidos em aberto.");
        if (activa && !await db.Zonas.AnyAsync(z => z.Id == mesa.ZonaId && z.Activo))
            throw new RegraNegocioException("A zona da mesa está inactiva.");
        mesa.Activo = activa;
        auditoria.Registar(activa ? "mesa.activar" : "mesa.desactivar", "Mesa", id, new { mesa.Nome });
        await db.SaveChangesAsync();
        return await ObterMesaAsync(id);
    }

    private Task<bool> TemPedidoAberto(int mesaId) =>
        db.Pedidos.AnyAsync(p => p.MesaId == mesaId && p.Estado == EstadoPedido.Aberto);

    private async Task ValidarMesa(int id, string nome, int zonaId)
    {
        if (await db.Mesas.AnyAsync(m => m.Id != id && m.Nome == nome))
            throw new ConflitoException($"Já existe uma mesa com o nome {nome}.");
        if (!await db.Zonas.AnyAsync(z => z.Id == zonaId && z.Activo))
            throw new RegraNegocioException("Zona inexistente ou inactiva.");
    }

    private async Task<List<MesaDto>> Projectar(IQueryable<Mesa> consulta, CancellationToken ct = default)
    {
        var mesas = await consulta
            .OrderBy(m => m.Zona.Ordem).ThenBy(m => m.Zona.Nome).ThenBy(m => m.Nome.Length).ThenBy(m => m.Nome)
            .Select(m => new
            {
                Dto = new MesaDto(
                    m.Id, m.Nome, m.ZonaId, m.Zona.Nome, m.Capacidade, m.Estado, m.OcupadaDesdeUtc, m.Activo,
                    db.Pedidos.Count(p => p.MesaId == m.Id && p.Estado == EstadoPedido.Aberto),
                    db.Pedidos.Where(p => p.MesaId == m.Id && p.Estado == EstadoPedido.Aberto).Sum(p => (decimal?)p.Total) ?? 0m,
                    m.RowVersion, null, null),
                Responsavel = db.Pedidos.Where(p => p.MesaId == m.Id && p.Estado == EstadoPedido.Aberto).Select(p => p.UtilizadorResponsavelId).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(mesas.Where(m => m.Responsavel != null).Select(m => m.Responsavel!.Value).Distinct(), ct);
        var veOutros = actual.TemPermissao(Seguranca.Permissoes.PosMesasOutros);
        return [.. mesas.Select(m => m.Responsavel is int r
            ? m.Dto with
            {
                ResponsavelId = r, Responsavel = nomes.GetValueOrDefault(r),
                TotalAberto = r == actual.Id || veOutros ? m.Dto.TotalAberto : 0m,
            }
            : m.Dto)];
    }
}
