using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Catalogo;

/// <summary>Estação de preparação (ex.: Cozinha quente, Bar, Pastelaria) usada para filtrar o KDS.</summary>
public class EstacaoPreparacao : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public class Categoria : EntidadeAuditavel, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public int? CategoriaPaiId { get; set; }
    public Categoria? CategoriaPai { get; set; }
    public List<Categoria> Subcategorias { get; set; } = [];
    public int? EstacaoPreparacaoId { get; set; }
    public EstacaoPreparacao? EstacaoPreparacao { get; set; }
    /// <summary>Impressora dos talões dos produtos desta categoria (vazio = a da categoria principal ou as regras automáticas).</summary>
    public int? ImpressoraId { get; set; }
    public Sistema.Impressora? Impressora { get; set; }
    /// <summary>Cor hexadecimal do botão no POS (ex.: #2563EB).</summary>
    public string? Cor { get; set; }
    public int Ordem { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Imagem de produto guardada na base (entra nas cópias de segurança; os postos não precisam de internet).</summary>
public class ImagemProduto
{
    public Guid Id { get; set; }
    public string TipoConteudo { get; set; } = "image/jpeg";
    public byte[] Dados { get; set; } = [];
    public int Largura { get; set; }
    public int Altura { get; set; }
    /// <summary>Endereço de onde foi importada (ou «ficheiro»).</summary>
    public string? Origem { get; set; }
    public string? Autor { get; set; }
    public string? Licenca { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public int? CriadoPorId { get; set; }
}

public class TaxaImposto : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    /// <summary>Percentagem (ex.: 16.0000 para 16%).</summary>
    public decimal Percentagem { get; set; }
    public bool Activo { get; set; } = true;
}

public class Produto : EntidadeAuditavel, IDesactivavel, IConcorrente
{
    public string Codigo { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public int CategoriaId { get; set; }
    public Categoria Categoria { get; set; } = null!;
    public TipoProduto Tipo { get; set; }
    /// <summary>Preço de venda ao público, com impostos incluídos.</summary>
    public decimal PrecoVenda { get; set; }
    /// <summary>Custo de referência manual; o custo por receita é calculado a partir dos ingredientes.</summary>
    public decimal Custo { get; set; }
    public int? TaxaImpostoId { get; set; }
    public TaxaImposto? TaxaImposto { get; set; }
    public string? ImagemUrl { get; set; }
    /// <summary>Impressora dos talões deste produto; vazio = a da categoria.</summary>
    public int? ImpressoraId { get; set; }
    public Sistema.Impressora? Impressora { get; set; }
    /// <summary>Se falso, os itens não aparecem no KDS (ex.: bebidas servidas no balcão).</summary>
    public bool EnviaCozinha { get; set; } = true;
    public bool Disponivel { get; set; } = true;
    public bool Activo { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}
