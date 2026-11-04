using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Despesas;
using LAMGestRestaurant.Application.Pessoal;
using LAMGestRestaurant.Application.Reservas;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api/clientes")]
public class ClientesController(ServicoClientes clientes) : ControllerBase
{
    /// <summary>Pesquisa também disponível no POS (para associar o cliente ao pedido).</summary>
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.ClientesVer, Permissoes.PosUsar, Permissoes.ReservasGerir)]
    public Task<Pagina<ClienteDto>> Listar(string? pesquisa, bool? activo, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        clientes.ListarAsync(pesquisa, activo, pagina, tamanho, ct);

    [HttpGet("{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.ClientesVer, Permissoes.PosUsar)]
    public Task<ClienteDto> Obter(int id) => clientes.ObterAsync(id);

    [HttpPost]
    [ExigePermissao(Permissoes.ClientesGerir)]
    public Task<ClienteDto> Criar(GuardarCliente dados) => clientes.CriarAsync(dados);

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ClientesGerir)]
    public Task<ClienteDto> Editar(int id, GuardarCliente dados) => clientes.EditarAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ClientesGerir)]
    public async Task<IActionResult> Activo(int id, AlterarActivo dados)
    {
        await clientes.DefinirActivoAsync(id, dados.Activo);
        return NoContent();
    }

    [HttpGet("{id:int}/pedidos")]
    [ExigePermissao(Permissoes.ClientesVer)]
    public Task<IReadOnlyList<PedidoCliente>> Pedidos(int id, CancellationToken ct) => clientes.PedidosAsync(id, ct);

    [HttpGet("{id:int}/pontos")]
    [ExigePermissao(Permissoes.ClientesVer)]
    public Task<IReadOnlyList<MovimentoPontosDto>> Pontos(int id, CancellationToken ct) => clientes.PontosAsync(id, ct);

    [HttpPost("{id:int}/pontos")]
    [ExigePermissao(Permissoes.ClientesGerir)]
    public Task<ClienteDto> AjustarPontos(int id, AjustarPontos dados) => clientes.AjustarPontosAsync(id, dados);
}

[ApiController]
[Route("api/reservas")]
[ExigePermissao(Permissoes.ReservasGerir)]
public class ReservasController(ServicoReservas reservas) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReservaDto>> Listar(DateOnly desde, DateOnly ate, EstadoReserva? estado, string? pesquisa, CancellationToken ct) =>
        reservas.ListarAsync(desde, ate, estado, pesquisa, ct);

    [HttpPost]
    public Task<ReservaDto> Criar(GuardarReserva dados) => reservas.CriarAsync(dados);

    [HttpPut("{id:int}")]
    public Task<ReservaDto> Editar(int id, GuardarReserva dados) => reservas.EditarAsync(id, dados);

    [HttpPost("{id:int}/estado")]
    public Task<ReservaDto> Estado(int id, AlterarEstadoReserva dados) => reservas.AlterarEstadoAsync(id, dados);
}

[ApiController]
[Route("api/despesas")]
public class DespesasController(ServicoDespesas despesas) : ControllerBase
{
    [HttpGet("categorias")]
    [ExigeAlgumaPermissao(Permissoes.DespesasRegistar, Permissoes.RelatoriosFinanceiros)]
    public Task<IReadOnlyList<CategoriaDespesaDto>> Categorias(CancellationToken ct) => despesas.CategoriasAsync(ct);

    [HttpPost("categorias")]
    [ExigePermissao(Permissoes.DespesasAnular)]
    public Task<CategoriaDespesaDto> CriarCategoria(GuardarCategoriaDespesa dados) => despesas.GuardarCategoriaAsync(null, dados);

    [HttpPut("categorias/{id:int}")]
    [ExigePermissao(Permissoes.DespesasAnular)]
    public Task<CategoriaDespesaDto> EditarCategoria(int id, GuardarCategoriaDespesa dados) => despesas.GuardarCategoriaAsync(id, dados);

    [HttpPost("categorias/{id:int}/activo")]
    [ExigePermissao(Permissoes.DespesasAnular)]
    public async Task<IActionResult> CategoriaActiva(int id, AlterarActivo dados)
    {
        await despesas.DefinirCategoriaActivaAsync(id, dados.Activo);
        return NoContent();
    }

    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.DespesasRegistar, Permissoes.RelatoriosFinanceiros)]
    public Task<Pagina<DespesaDto>> Listar(DateOnly desde, DateOnly ate, int? categoriaId, EstadoDespesa? estado, int pagina = 1, int tamanho = 50, CancellationToken ct = default) =>
        despesas.ListarAsync(desde, ate, categoriaId, estado, pagina, tamanho, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.DespesasRegistar)]
    public Task<DespesaDto> Registar(NovaDespesa dados) => despesas.RegistarAsync(dados);

    [HttpPost("{id:int}/anular")]
    [ExigePermissao(Permissoes.DespesasAnular)]
    public Task<DespesaDto> Anular(int id, AnularComMotivo dados) => despesas.AnularAsync(id, dados.Motivo, dados.RowVersion);

    [HttpGet("resumo")]
    [ExigeAlgumaPermissao(Permissoes.DespesasRegistar, Permissoes.RelatoriosFinanceiros)]
    public Task<ResumoDespesas> Resumo(DateOnly desde, DateOnly ate, CancellationToken ct) => despesas.ResumoAsync(desde, ate, ct);
}

[ApiController]
[Route("api/funcionarios")]
public class FuncionariosController(ServicoFuncionarios funcionarios) : ControllerBase
{
    [HttpGet]
    [ExigePermissao(Permissoes.FuncionariosVer)]
    public Task<IReadOnlyList<FuncionarioDto>> Listar(bool incluirInactivos = false, CancellationToken ct = default) =>
        funcionarios.ListarAsync(incluirInactivos, ct);

    /// <summary>Nomes por função (ex.: Garcom, Estafeta) para escolher no POS/Delivery, sem dados pessoais.</summary>
    [HttpGet("por-funcao/{funcao}")]
    [ExigeAlgumaPermissao(Permissoes.PosUsar, Permissoes.DeliveryGerir, Permissoes.FuncionariosVer)]
    public Task<IReadOnlyList<Opcao>> PorFuncao(FuncaoFuncionario funcao, CancellationToken ct) => funcionarios.PorFuncaoAsync(funcao, ct);

    [HttpGet("utilizadores")]
    [ExigePermissao(Permissoes.FuncionariosGerir)]
    public Task<IReadOnlyList<Opcao>> Utilizadores(CancellationToken ct) => funcionarios.UtilizadoresAsync(ct);

    [HttpPost]
    [ExigePermissao(Permissoes.FuncionariosGerir)]
    public Task<FuncionarioDto> Criar(GuardarFuncionario dados) => funcionarios.GuardarAsync(null, dados);

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.FuncionariosGerir)]
    public Task<FuncionarioDto> Editar(int id, GuardarFuncionario dados) => funcionarios.GuardarAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.FuncionariosGerir)]
    public async Task<IActionResult> Activo(int id, AlterarActivo dados)
    {
        await funcionarios.DefinirActivoAsync(id, dados.Activo);
        return NoContent();
    }
}

public record GuardarConfiguracoes(IReadOnlyDictionary<string, string> Valores);

[ApiController]
[Route("api/configuracoes")]
public class ConfiguracoesController(ServicoConfiguracoes configuracoes) : ControllerBase
{
    /// <summary>Qualquer utilizador autenticado (nome do estabelecimento, moeda, fuso).</summary>
    [HttpGet("publicas")]
    public Task<ConfiguracoesPublicas> Publicas(CancellationToken ct) => configuracoes.PublicasAsync(ct);

    [HttpGet]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<IReadOnlyList<ConfiguracaoDto>> Listar(CancellationToken ct) => configuracoes.ListarAsync(ct);

    [HttpPut]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<IReadOnlyList<ConfiguracaoDto>> Guardar(GuardarConfiguracoes dados) => configuracoes.GuardarAsync(dados.Valores);

    [HttpPost("taxas")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<TaxaImpostoDto> CriarTaxa(GuardarTaxa dados)
    {
        var t = await configuracoes.GuardarTaxaAsync(null, dados);
        return new TaxaImpostoDto(t.Id, t.Nome, t.Percentagem, t.Activo);
    }

    [HttpPut("taxas/{id:int}")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<TaxaImpostoDto> EditarTaxa(int id, GuardarTaxa dados)
    {
        var t = await configuracoes.GuardarTaxaAsync(id, dados);
        return new TaxaImpostoDto(t.Id, t.Nome, t.Percentagem, t.Activo);
    }

    [HttpPost("taxas/{id:int}/activo")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> TaxaActiva(int id, AlterarActivo dados)
    {
        await configuracoes.DefinirTaxaActivaAsync(id, dados.Activo);
        return NoContent();
    }

    [HttpPost("metodos-pagamento")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<MetodoPagamentoDto> CriarMetodo(GuardarMetodoPagamento dados) => configuracoes.GuardarMetodoAsync(null, dados);

    [HttpPut("metodos-pagamento/{id:int}")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<MetodoPagamentoDto> EditarMetodo(int id, GuardarMetodoPagamento dados) => configuracoes.GuardarMetodoAsync(id, dados);

    [HttpPost("metodos-pagamento/{id:int}/activo")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> MetodoActivo(int id, AlterarActivo dados)
    {
        await configuracoes.DefinirMetodoActivoAsync(id, dados.Activo);
        return NoContent();
    }
}

[ApiController]
[Route("api/configuracoes/impressoras")]
[ExigePermissao(Permissoes.ConfiguracoesGerir)]
public class ImpressorasController(ServicoImpressoras impressoras) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ImpressoraDto>> Listar(CancellationToken ct) => impressoras.ListarAsync(ct);

    /// <summary>Impressoras instaladas no Windows do servidor (as únicas que o servidor consegue usar).</summary>
    [HttpGet("instaladas")]
    public IReadOnlyList<string> Instaladas() => impressoras.Instaladas();

    /// <summary>Servidor e postos com o agente de impressão, cada um com as impressoras instaladas no seu Windows.</summary>
    [HttpGet("locais")]
    public Task<IReadOnlyList<LocalImpressoras>> Locais(CancellationToken ct) => impressoras.LocaisAsync(ct);

    [HttpPost]
    public Task<ImpressoraDto> Criar(GuardarImpressora dados) => impressoras.GuardarAsync(null, dados);

    [HttpPut("{id:int}")]
    public Task<ImpressoraDto> Editar(int id, GuardarImpressora dados) => impressoras.GuardarAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    public async Task<IActionResult> DefinirActiva(int id, AlterarActivo dados)
    {
        await impressoras.DefinirActivaAsync(id, dados.Activo);
        return NoContent();
    }

    /// <summary>Envia uma página de teste (em segundo plano; o resultado aparece no estado da impressora).</summary>
    [HttpPost("{id:int}/teste")]
    public async Task<IActionResult> Testar(int id)
    {
        await impressoras.TestarAsync(id);
        return Accepted();
    }
}
