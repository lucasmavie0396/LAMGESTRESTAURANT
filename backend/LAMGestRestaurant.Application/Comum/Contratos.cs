namespace LAMGestRestaurant.Application.Comum;

/// <summary>Utilizador autenticado no pedido HTTP actual.</summary>
public interface IUtilizadorActual
{
    int? Id { get; }
    string? Nome { get; }
    string? EnderecoIp { get; }
    bool TemPermissao(string permissao);
}

/// <summary>Nomes de utilizadores para apresentação (as contas vivem no Identity, na Infrastructure).</summary>
public interface IConsultaUtilizadores
{
    Task<IReadOnlyDictionary<int, string>> NomesAsync(IEnumerable<int> ids, CancellationToken ct = default);
    /// <summary>Utilizadores activos (id e nome), para listas de escolha.</summary>
    Task<IReadOnlyList<Opcao>> TodosAsync(CancellationToken ct = default);
}

/// <summary>Opção simples para listas de escolha.</summary>
public record Opcao(int Id, string Nome);

/// <summary>
/// Registo de auditoria. A entrada é adicionada ao contexto e gravada no mesmo SaveChanges
/// (e na mesma transacção) da operação auditada.
/// </summary>
public interface IAuditoria
{
    void Registar(string accao, string? entidade = null, object? entidadeId = null, object? detalhes = null);
}
