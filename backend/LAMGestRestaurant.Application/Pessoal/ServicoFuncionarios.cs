using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Pessoal;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Pessoal;

/// <summary>DocumentoIdentificacao só é devolvido a quem tem funcionarios.dados-pessoais (null caso contrário).</summary>
public record FuncionarioDto(int Id, string Nome, FuncaoFuncionario Funcao, string? Telefone, string? Email, string? DocumentoIdentificacao,
    DateOnly? DataAdmissao, int? UtilizadorId, string? Utilizador, bool Activo);

public record GuardarFuncionario(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(150)] string Nome,
    FuncaoFuncionario Funcao,
    [RegularExpression(@"^\+?[0-9 ]{7,20}$", ErrorMessage = "Telefone inválido.")] string? Telefone,
    [EmailAddress(ErrorMessage = "E-mail inválido.")][StringLength(200)] string? Email,
    [StringLength(50)] string? DocumentoIdentificacao,
    DateOnly? DataAdmissao,
    int? UtilizadorId);

/// <summary>
/// Funcionários (dados de trabalho). Não inclui processamento salarial. Os dados pessoais (documento de
/// identificação) só são vistos e alterados com a permissão funcionarios.dados-pessoais.
/// </summary>
public class ServicoFuncionarios(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, IConsultaUtilizadores utilizadores)
{
    private bool VeDadosPessoais => actual.TemPermissao(Permissoes.FuncionariosDadosPessoais);

    public async Task<IReadOnlyList<FuncionarioDto>> ListarAsync(bool incluirInactivos, CancellationToken ct)
    {
        var lista = await db.Funcionarios.AsNoTracking().Where(f => incluirInactivos || f.Activo).OrderBy(f => f.Nome).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(lista.Where(f => f.UtilizadorId is not null).Select(f => f.UtilizadorId!.Value), ct);
        return [.. lista.Select(f => ParaDto(f, nomes))];
    }

    /// <summary>Lista mínima para escolher garçons/estafetas (sem dados pessoais).</summary>
    public async Task<IReadOnlyList<Opcao>> PorFuncaoAsync(FuncaoFuncionario funcao, CancellationToken ct) =>
        await db.Funcionarios.AsNoTracking().Where(f => f.Activo && f.Funcao == funcao).OrderBy(f => f.Nome).Select(f => new Opcao(f.Id, f.Nome)).ToListAsync(ct);

    public Task<IReadOnlyList<Opcao>> UtilizadoresAsync(CancellationToken ct) => utilizadores.TodosAsync(ct);

    public async Task<FuncionarioDto> GuardarAsync(int? id, GuardarFuncionario dados)
    {
        if (dados.UtilizadorId is int u)
        {
            if (!(await utilizadores.NomesAsync([u])).ContainsKey(u))
                throw new RegraNegocioException("Utilizador inexistente.");
            if (await db.Funcionarios.AnyAsync(f => f.Id != id && f.UtilizadorId == u))
                throw new ConflitoException("Esse utilizador já está associado a outro funcionário.");
        }
        await using var transacao = await db.Database.BeginTransactionAsync();
        var f = id is int existente
            ? await db.Funcionarios.FindAsync(existente) ?? throw new NaoEncontradoException("Funcionário não encontrado.")
            : db.Funcionarios.Add(new Funcionario { Activo = true }).Entity;
        f.Nome = dados.Nome.Trim();
        f.Funcao = dados.Funcao;
        f.Telefone = Limpar(dados.Telefone);
        f.Email = Limpar(dados.Email);
        f.DataAdmissao = dados.DataAdmissao;
        f.UtilizadorId = dados.UtilizadorId;
        // Sem permissão de dados pessoais, o documento existente mantém-se (o cliente nem o recebe).
        if (VeDadosPessoais) f.DocumentoIdentificacao = Limpar(dados.DocumentoIdentificacao);
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "funcionario.criar" : "funcionario.editar", "Funcionario", f.Id, new { f.Nome, Funcao = f.Funcao.ToString(), f.UtilizadorId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        var nomes = await utilizadores.NomesAsync(f.UtilizadorId is int x ? [x] : Array.Empty<int>());
        return ParaDto(f, nomes);
    }

    public async Task DefinirActivoAsync(int id, bool activo)
    {
        var f = await db.Funcionarios.FindAsync(id) ?? throw new NaoEncontradoException("Funcionário não encontrado.");
        f.Activo = activo;
        auditoria.Registar(activo ? "funcionario.activar" : "funcionario.desactivar", "Funcionario", id, null);
        await db.SaveChangesAsync();
    }

    private FuncionarioDto ParaDto(Funcionario f, IReadOnlyDictionary<int, string> nomes) => new(
        f.Id, f.Nome, f.Funcao, f.Telefone, f.Email, VeDadosPessoais ? f.DocumentoIdentificacao : null, f.DataAdmissao,
        f.UtilizadorId, f.UtilizadorId is int u ? nomes.GetValueOrDefault(u) : null, f.Activo);

    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
