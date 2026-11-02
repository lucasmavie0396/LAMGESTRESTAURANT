using FluentValidation;
using LAMGestRestaurant.Application.Comum;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Erros;

/// <summary>
/// Converte excepções em respostas ProblemDetails com mensagens em Português.
/// Nunca devolve stack traces; os detalhes técnicos ficam apenas no log.
/// </summary>
public class TratadorExcepcoes(ILogger<TratadorExcepcoes> log, IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcao, CancellationToken ct)
    {
        var (estado, titulo, detalhe) = excepcao switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "Dados inválidos", string.Join(" ", v.Errors.Select(e => e.ErrorMessage))),
            RegraNegocioException r => (StatusCodes.Status422UnprocessableEntity, "Operação não permitida", r.Message),
            NaoEncontradoException n => (StatusCodes.Status404NotFound, "Não encontrado", n.Message),
            ConflitoException c => (StatusCodes.Status409Conflict, "Conflito", c.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflito",
                "O registo foi alterado por outro utilizador. Actualize os dados e tente novamente."),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (StatusCodes.Status409Conflict, "Conflito",
                "Já existe um registo com estes dados (valor duplicado)."),
            DbUpdateException { InnerException: SqlException { Number: 547 } } => (StatusCodes.Status409Conflict, "Conflito",
                "A operação viola uma regra de integridade dos dados (registo relacionado ou valor inválido)."),
            SqlException or TimeoutException => (StatusCodes.Status503ServiceUnavailable, "Base de dados indisponível",
                "Não foi possível comunicar com a base de dados. A operação não foi gravada."),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "Pedido cancelado", null),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno", "Ocorreu um erro inesperado. A operação não foi concluída."),
        };

        if (estado >= 500)
            log.LogError(excepcao, "Erro não tratado em {Metodo} {Caminho}", http.Request.Method, http.Request.Path);
        else
            log.LogInformation("Pedido recusado ({Estado}) em {Metodo} {Caminho}: {Mensagem}", estado, http.Request.Method, http.Request.Path, detalhe);

        http.Response.StatusCode = estado;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = excepcao,
            ProblemDetails = new ProblemDetails { Status = estado, Title = titulo, Detail = detalhe },
        });
    }
}
