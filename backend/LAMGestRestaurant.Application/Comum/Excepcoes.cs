namespace LAMGestRestaurant.Application.Comum;

/// <summary>Erro que diz respeito a um campo do formulário (o ecrã põe o cursor nesse campo).</summary>
public interface IErroDeCampo
{
    /// <summary>Nome da propriedade do pedido (ex.: «UnidadeMedidaId»), ou null.</summary>
    string? Campo { get; }
}

/// <summary>Violação de uma regra de negócio. Mensagem apresentável ao utilizador (HTTP 422).</summary>
public class RegraNegocioException(string mensagem, string? campo = null) : Exception(mensagem), IErroDeCampo
{
    public string? Campo { get; } = campo;
}

/// <summary>Recurso inexistente (HTTP 404).</summary>
public class NaoEncontradoException(string mensagem) : Exception(mensagem);

/// <summary>Conflito de estado, por exemplo registo alterado por outra sessão (HTTP 409).</summary>
public class ConflitoException(string mensagem, string? campo = null) : Exception(mensagem), IErroDeCampo
{
    public string? Campo { get; } = campo;
}

/// <summary>O utilizador não pode aceder a este registo concreto (HTTP 403).</summary>
public class AcessoNegadoException(string mensagem) : Exception(mensagem);
