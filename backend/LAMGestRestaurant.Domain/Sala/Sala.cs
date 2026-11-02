using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Sala;

public class Zona : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Activo { get; set; } = true;
}

public class Mesa : EntidadeAuditavel, IDesactivavel, IConcorrente
{
    public string Nome { get; set; } = string.Empty;
    public int ZonaId { get; set; }
    public Zona Zona { get; set; } = null!;
    public int Capacidade { get; set; }
    public EstadoMesa Estado { get; set; } = EstadoMesa.Livre;
    /// <summary>Momento em que a mesa passou a Ocupada (para mostrar a duração do atendimento).</summary>
    public DateTime? OcupadaDesdeUtc { get; set; }
    public bool Activo { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public class Reserva : EntidadeAuditavel, IConcorrente
{
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    /// <summary>Nome e telefone ficam na reserva para clientes sem ficha.</summary>
    public string NomeCliente { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public DateTime InicioUtc { get; set; }
    public int DuracaoMinutos { get; set; } = 120;
    public int NumeroPessoas { get; set; }
    public int? MesaId { get; set; }
    public Mesa? Mesa { get; set; }
    public EstadoReserva Estado { get; set; } = EstadoReserva.Pendente;
    public string? Observacoes { get; set; }
    public string? MotivoCancelamento { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
