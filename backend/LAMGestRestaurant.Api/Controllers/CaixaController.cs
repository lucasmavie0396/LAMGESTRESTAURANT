using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api/terminais")]
public class TerminaisController(ServicoCaixa servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.CaixaOperar, Permissoes.CaixaVerTodas, Permissoes.ConfiguracoesGerir)]
    public Task<IReadOnlyList<TerminalDto>> Listar(bool incluirInactivos = false, CancellationToken ct = default) =>
        servico.ListarTerminaisAsync(incluirInactivos, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<ActionResult<TerminalDto>> Criar(GuardarTerminal dados)
    {
        var criado = await servico.CriarTerminalAsync(dados);
        return Created($"api/terminais/{criado.Id}", criado);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<TerminalDto> Editar(int id, GuardarTerminal dados) => servico.EditarTerminalAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> DefinirActivo(int id, AlterarActivo dados)
    {
        await servico.DefinirTerminalActivoAsync(id, dados.Activo);
        return NoContent();
    }

    /// <summary>Sinal do navegador deste posto (a cada minuto, enquanto o sistema está aberto): serve para o ver «online».</summary>
    [HttpPost("{id:int}/presenca")]
    [Authorize]
    public async Task<IActionResult> Presenca(int id, [FromServices] ServicoPostos postos, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        await postos.PresencaAsync(id, ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4().ToString() : ip?.ToString(), ct);
        return NoContent();
    }

    /// <summary>Retira a chave do agente de impressão (ex.: computador do posto substituído ou roubado).</summary>
    [HttpDelete("{id:int}/agente")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> RevogarAgente(int id, [FromServices] ServicoAgentes agentes, CancellationToken ct)
    {
        await agentes.RevogarAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Consulta pública de um posto pelo número: usada pelo instalador dos postos e pelo atalho (?posto=N).</summary>
[ApiController]
[Route("api/postos")]
public class PostosController(ServicoCaixa servico) : ControllerBase
{
    [HttpGet("{numero:int}")]
    [AllowAnonymous]
    public Task<PostoPublico> Obter(int numero, CancellationToken ct) => servico.ObterPostoAsync(numero, ct);

    /// <summary>
    /// Regista o agente de impressão do posto (instalador, com a sessão de um administrador) e devolve a chave do agente.
    /// Registar de novo substitui a chave (a anterior deixa de valer).
    /// </summary>
    [HttpPost("{numero:int}/agente")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<AgenteRegistado> RegistarAgente(int numero, [FromServices] ServicoAgentes agentes, CancellationToken ct) =>
        agentes.RegistarAsync(numero, ct);
}

[ApiController]
[Route("api/caixa")]
public class CaixaController(ServicoCaixa servico) : ControllerBase
{
    [HttpGet("metodos-pagamento")]
    public Task<IReadOnlyList<MetodoPagamentoDto>> Metodos(CancellationToken ct) => servico.ListarMetodosAsync(ct);

    [HttpPost("sessoes")]
    [ExigePermissao(Permissoes.CaixaOperar)]
    public async Task<ActionResult<SessaoCaixaDto>> Abrir(AbrirCaixa dados)
    {
        var sessao = await servico.AbrirAsync(dados);
        return CreatedAtAction(nameof(Obter), new { id = sessao.Id }, sessao);
    }

    /// <summary>Sessão aberta no terminal (204 se não houver).</summary>
    [HttpGet("terminais/{terminalId:int}/sessao-aberta")]
    [ExigeAlgumaPermissao(Permissoes.CaixaOperar, Permissoes.CaixaVerTodas, Permissoes.PagamentosRegistar)]
    public async Task<ActionResult<SessaoCaixaDto>> SessaoAberta(int terminalId)
    {
        var sessao = await servico.ObterAbertaDoTerminalAsync(terminalId);
        return sessao is null ? NoContent() : Ok(sessao);
    }

    [HttpGet("sessoes")]
    [ExigeAlgumaPermissao(Permissoes.CaixaOperar, Permissoes.CaixaVerTodas)]
    public Task<Pagina<SessaoCaixaResumo>> Listar(
        DateTime? desdeUtc, DateTime? ateUtc, int? terminalId, EstadoSessaoCaixa? estado, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        servico.ListarSessoesAsync(new FiltroSessoes(desdeUtc, ateUtc, terminalId, estado, pagina, tamanho), ct);

    [HttpGet("sessoes/{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.CaixaOperar, Permissoes.CaixaVerTodas)]
    public Task<SessaoCaixaDto> Obter(int id) => servico.ObterAsync(id);

    [HttpPost("sessoes/{id:int}/movimentos")]
    [ExigePermissao(Permissoes.CaixaMovimentos)]
    public Task<SessaoCaixaDto> Movimento(int id, RegistarMovimentoCaixa dados) => servico.RegistarMovimentoAsync(id, dados);

    [HttpPost("sessoes/{id:int}/fechar")]
    [ExigePermissao(Permissoes.CaixaOperar)]
    public Task<SessaoCaixaDto> Fechar(int id, FecharCaixa dados) => servico.FecharAsync(id, dados);
}
