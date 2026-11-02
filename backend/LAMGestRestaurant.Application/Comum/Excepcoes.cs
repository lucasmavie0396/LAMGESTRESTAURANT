namespace LAMGestRestaurant.Application.Comum;

/// <summary>Violação de uma regra de negócio. Mensagem apresentável ao utilizador (HTTP 422).</summary>
public class RegraNegocioException(string mensagem) : Exception(mensagem);

/// <summary>Recurso inexistente (HTTP 404).</summary>
public class NaoEncontradoException(string mensagem) : Exception(mensagem);

/// <summary>Conflito de estado, por exemplo registo alterado por outra sessão (HTTP 409).</summary>
public class ConflitoException(string mensagem) : Exception(mensagem);

/// <summary>O utilizador não pode aceder a este registo concreto (HTTP 403).</summary>
public class AcessoNegadoException(string mensagem) : Exception(mensagem);
