namespace LAMGestRestaurant.Domain.Comum;

/// <summary>Base de todas as entidades persistidas.</summary>
public abstract class Entidade
{
    public int Id { get; set; }
}

/// <summary>Entidade com campos de criação/alteração preenchidos automaticamente no SaveChanges.</summary>
public abstract class EntidadeAuditavel : Entidade
{
    public DateTime CriadoEmUtc { get; set; }
    public int? CriadoPorId { get; set; }
    public DateTime? AlteradoEmUtc { get; set; }
    public int? AlteradoPorId { get; set; }
}

/// <summary>Entidade com controlo de concorrência optimista (rowversion no SQL Server).</summary>
public interface IConcorrente
{
    byte[] RowVersion { get; set; }
}

/// <summary>Entidade que não se elimina depois de usada; apenas se desactiva.</summary>
public interface IDesactivavel
{
    bool Activo { get; set; }
}
