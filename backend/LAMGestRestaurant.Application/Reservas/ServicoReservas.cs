using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sala;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Reservas;

public record ReservaDto(
    int Id,
    int? ClienteId,
    string NomeCliente,
    string? Telefone,
    DateTime InicioUtc,
    int DuracaoMinutos,
    int NumeroPessoas,
    int? MesaId,
    string? MesaNome,
    EstadoReserva Estado,
    string? Observacoes,
    string? MotivoCancelamento,
    byte[] RowVersion);

public record GuardarReserva(
    int? ClienteId,
    [Required(ErrorMessage = "Indique o nome do cliente.")][StringLength(200)] string NomeCliente,
    [RegularExpression(@"^\+?[0-9 ]{7,20}$", ErrorMessage = "Telefone inválido.")] string? Telefone,
    DateTime InicioUtc,
    [Range(15, 720, ErrorMessage = "A duração deve estar entre 15 minutos e 12 horas.")] int DuracaoMinutos,
    [Range(1, 200, ErrorMessage = "Número de pessoas inválido.")] int NumeroPessoas,
    int? MesaId,
    [StringLength(1000)] string? Observacoes,
    byte[]? RowVersion);

public record AlterarEstadoReserva(EstadoReserva Estado, [StringLength(1000)] string? Motivo, byte[]? RowVersion);

/// <summary>
/// Reservas. Uma mesa não pode ter duas reservas activas (Pendente, Confirmada, Chegada) com horários sobrepostos.
/// As reservas não alteram automaticamente o estado da mesa no mapa: ao chegar o cliente, a mesa é aberta no POS.
/// </summary>
public class ServicoReservas(IAppDbContext db, IAuditoria auditoria, ConfiguracoesSistema config, TimeProvider relogio)
{
    private static readonly EstadoReserva[] Activas = [EstadoReserva.Pendente, EstadoReserva.Confirmada, EstadoReserva.Chegada];

    private static readonly Dictionary<EstadoReserva, EstadoReserva[]> Transicoes = new()
    {
        [EstadoReserva.Pendente] = [EstadoReserva.Confirmada, EstadoReserva.Chegada, EstadoReserva.Cancelada],
        [EstadoReserva.Confirmada] = [EstadoReserva.Chegada, EstadoReserva.Cancelada],
        [EstadoReserva.Chegada] = [EstadoReserva.Concluida],
    };

    /// <summary>Reservas de um dia local (fuso configurado), opcionalmente filtradas.</summary>
    public async Task<IReadOnlyList<ReservaDto>> ListarAsync(DateOnly desde, DateOnly ate, EstadoReserva? estado, string? pesquisa, CancellationToken ct)
    {
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var q = db.Reservas.AsNoTracking().Where(r => r.InicioUtc >= inicio && r.InicioUtc < fim);
        if (estado is { } e) q = q.Where(r => r.Estado == e);
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            var p = pesquisa.Trim();
            var tel = ServicoClientes.NormalizarTelefone(p);
            q = q.Where(r => r.NomeCliente.Contains(p) || (tel != null && r.Telefone!.Contains(tel)));
        }
        return await Projectar(q.OrderBy(r => r.InicioUtc)).ToListAsync(ct);
    }

    public async Task<ReservaDto> ObterAsync(int id) =>
        await Projectar(db.Reservas.AsNoTracking().Where(r => r.Id == id)).FirstOrDefaultAsync() ?? throw new NaoEncontradoException("Reserva não encontrada.");

    public async Task<ReservaDto> CriarAsync(GuardarReserva dados)
    {
        if (dados.InicioUtc < relogio.GetUtcNow().UtcDateTime.AddMinutes(-15))
            throw new RegraNegocioException("A data e hora da reserva já passaram.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var reserva = new Reserva();
        await AplicarAsync(reserva, dados);
        db.Reservas.Add(reserva);
        await db.SaveChangesAsync();
        auditoria.Registar("reserva.criar", "Reserva", reserva.Id, new { reserva.InicioUtc, reserva.NumeroPessoas, reserva.MesaId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(reserva.Id);
    }

    public async Task<ReservaDto> EditarAsync(int id, GuardarReserva dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var reserva = await db.Reservas.FindAsync(id) ?? throw new NaoEncontradoException("Reserva não encontrada.");
        db.VerificarVersao(reserva, dados.RowVersion);
        if (!Activas.Contains(reserva.Estado))
            throw new RegraNegocioException("Reservas concluídas ou canceladas não podem ser alteradas.");
        await AplicarAsync(reserva, dados);
        auditoria.Registar("reserva.editar", "Reserva", id, new { reserva.InicioUtc, reserva.NumeroPessoas, reserva.MesaId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task<ReservaDto> AlterarEstadoAsync(int id, AlterarEstadoReserva dados)
    {
        var reserva = await db.Reservas.FindAsync(id) ?? throw new NaoEncontradoException("Reserva não encontrada.");
        db.VerificarVersao(reserva, dados.RowVersion);
        if (!Transicoes.TryGetValue(reserva.Estado, out var permitidos) || !permitidos.Contains(dados.Estado))
            throw new RegraNegocioException($"Não é possível passar de {reserva.Estado} para {dados.Estado}.");
        if (dados.Estado == EstadoReserva.Cancelada)
        {
            if (string.IsNullOrWhiteSpace(dados.Motivo) || dados.Motivo.Trim().Length < 3)
                throw new RegraNegocioException("Indique o motivo do cancelamento.");
            reserva.MotivoCancelamento = dados.Motivo.Trim();
        }
        var anterior = reserva.Estado;
        reserva.Estado = dados.Estado;
        auditoria.Registar("reserva.estado", "Reserva", id, new { De = anterior.ToString(), Para = dados.Estado.ToString(), Motivo = reserva.MotivoCancelamento });
        await db.SaveChangesAsync();
        return await ObterAsync(id);
    }

    private async Task AplicarAsync(Reserva reserva, GuardarReserva dados)
    {
        if (dados.ClienteId is int c && !await db.Clientes.AnyAsync(x => x.Id == c && x.Activo))
            throw new RegraNegocioException("Cliente inexistente ou inactivo.");
        var inicio = DateTime.SpecifyKind(dados.InicioUtc, DateTimeKind.Utc);
        var fim = inicio.AddMinutes(dados.DuracaoMinutos);
        if (dados.MesaId is int mesaId)
        {
            var mesa = await db.Mesas.FirstOrDefaultAsync(m => m.Id == mesaId && m.Activo) ?? throw new RegraNegocioException("Mesa inexistente ou inactiva.");
            if (dados.NumeroPessoas > mesa.Capacidade)
                throw new RegraNegocioException($"A mesa {mesa.Nome} só tem {mesa.Capacidade} lugares.");
            var conflito = await db.Reservas.Where(r => r.Id != reserva.Id && r.MesaId == mesaId && Activas.Contains(r.Estado)
                    && r.InicioUtc < fim && r.InicioUtc.AddMinutes(r.DuracaoMinutos) > inicio)
                .Select(r => new { r.NomeCliente, r.InicioUtc }).FirstOrDefaultAsync();
            if (conflito is not null)
                throw new ConflitoException($"A mesa {mesa.Nome} já está reservada nesse horário ({conflito.NomeCliente}).");
        }
        reserva.ClienteId = dados.ClienteId;
        reserva.NomeCliente = dados.NomeCliente.Trim();
        reserva.Telefone = ServicoClientes.NormalizarTelefone(dados.Telefone);
        reserva.InicioUtc = inicio;
        reserva.DuracaoMinutos = dados.DuracaoMinutos;
        reserva.NumeroPessoas = dados.NumeroPessoas;
        reserva.MesaId = dados.MesaId;
        reserva.Observacoes = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim();
    }

    private static IQueryable<ReservaDto> Projectar(IQueryable<Reserva> q) => q.Select(r => new ReservaDto(
        r.Id, r.ClienteId, r.NomeCliente, r.Telefone, r.InicioUtc, r.DuracaoMinutos, r.NumeroPessoas, r.MesaId,
        r.Mesa != null ? r.Mesa.Nome : null, r.Estado, r.Observacoes, r.MotivoCancelamento, r.RowVersion));
}
