using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;

namespace LAMGestRestaurant.Domain.Catalogo;

/// <summary>
/// Grupo de acompanhamentos (ex.: «Acompanhamento»: Arroz, Batata, Salada fria) associado a pratos. No POS, ao lançar o prato,
/// escolhe-se entre <see cref="Minimo"/> e <see cref="Maximo"/> opções. Não mudam o preço do prato, saem no talão da cozinha
/// e gastam stock pelos artigos de cada opção. Nunca se vendem sozinhos.
/// </summary>
public class GrupoAcompanhamentos : EntidadeAuditavel, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public int Minimo { get; set; }
    public int Maximo { get; set; } = 1;
    public bool Activo { get; set; } = true;
    public List<OpcaoAcompanhamento> Opcoes { get; set; } = [];
    public List<ProdutoGrupoAcompanhamentos> Produtos { get; set; } = [];
}

/// <summary>Opção de um grupo (ex.: «Batata frita»). Usada num pedido, nunca se apaga: desactiva-se.</summary>
public class OpcaoAcompanhamento : Entidade
{
    public int GrupoId { get; set; }
    public GrupoAcompanhamentos Grupo { get; set; } = null!;
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Artigos gastos por 1 prato (como uma ficha técnica). Sem artigos, a opção só sai no talão.</summary>
    public List<OpcaoAcompanhamentoItem> Itens { get; set; } = [];
}

public class OpcaoAcompanhamentoItem : Entidade
{
    public int OpcaoId { get; set; }
    public OpcaoAcompanhamento Opcao { get; set; } = null!;
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    /// <summary>Quantidade por 1 prato vendido, na unidade indicada.</summary>
    public decimal Quantidade { get; set; }
    public int UnidadeMedidaId { get; set; }
    public UnidadeMedida UnidadeMedida { get; set; } = null!;
    /// <summary>Armazém de onde sai na venda. Vazio = automático (o do prato).</summary>
    public int? ArmazemId { get; set; }
    public Armazem? Armazem { get; set; }
}

/// <summary>Pratos que pedem o grupo de acompanhamentos.</summary>
public class ProdutoGrupoAcompanhamentos
{
    public int ProdutoId { get; set; }
    public Produto Produto { get; set; } = null!;
    public int GrupoId { get; set; }
    public GrupoAcompanhamentos Grupo { get; set; } = null!;
}
