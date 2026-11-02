namespace LAMGestRestaurant.Application.Comum;

/// <summary>Utilizador autenticado no pedido HTTP actual.</summary>
public interface IUtilizadorActual
{
    int? Id { get; }
    string? Nome { get; }
    string? EnderecoIp { get; }
    bool TemPermissao(string permissao);
}

/// <summary>
/// Registo de auditoria. A entrada é adicionada ao contexto e gravada no mesmo SaveChanges
/// (e na mesma transacção) da operação auditada.
/// </summary>
public interface IAuditoria
{
    void Registar(string accao, string? entidade = null, object? entidadeId = null, object? detalhes = null);
}
