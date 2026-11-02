using System.Text.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Dados;

namespace LAMGestRestaurant.Infrastructure.Auditoria;

public class ServicoAuditoria(AppDbContext db, IUtilizadorActual utilizador, TimeProvider relogio) : IAuditoria
{
    private static readonly JsonSerializerOptions OpcoesJson = new() { WriteIndented = false };

    public void Registar(string accao, string? entidade = null, object? entidadeId = null, object? detalhes = null)
    {
        db.RegistosAuditoria.Add(new RegistoAuditoria
        {
            DataUtc = relogio.GetUtcNow().UtcDateTime,
            UtilizadorId = utilizador.Id,
            NomeUtilizador = utilizador.Nome,
            EnderecoIp = utilizador.EnderecoIp,
            Accao = accao,
            Entidade = entidade,
            EntidadeId = entidadeId?.ToString(),
            Detalhes = detalhes is null ? null : JsonSerializer.Serialize(detalhes, OpcoesJson),
        });
    }
}
