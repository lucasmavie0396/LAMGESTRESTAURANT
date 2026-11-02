using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Pessoal;

/// <summary>Funcionário (garçom, cozinheiro, estafeta...). Pode ou não ter conta de utilizador.</summary>
public class Funcionario : EntidadeAuditavel, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public FuncaoFuncionario Funcao { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    /// <summary>Dado pessoal: visível apenas com a permissão funcionarios.dados-pessoais.</summary>
    public string? DocumentoIdentificacao { get; set; }
    public DateOnly? DataAdmissao { get; set; }
    public int? UtilizadorId { get; set; }
    public bool Activo { get; set; } = true;
}

public class CategoriaDespesa : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public class Despesa : EntidadeAuditavel, IConcorrente
{
    public int CategoriaDespesaId { get; set; }
    public CategoriaDespesa CategoriaDespesa { get; set; } = null!;
    public DateTime DataUtc { get; set; }
    public decimal Valor { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    /// <summary>Número de factura/recibo do fornecedor da despesa.</summary>
    public string? Referencia { get; set; }
    /// <summary>Preenchido quando a despesa foi paga com dinheiro da caixa.</summary>
    public int? SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }
    public EstadoDespesa Estado { get; set; } = EstadoDespesa.Confirmada;
    public DateTime? AnuladaEmUtc { get; set; }
    public int? AnuladaPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
