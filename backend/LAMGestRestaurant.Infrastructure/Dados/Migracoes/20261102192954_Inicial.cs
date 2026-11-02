using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Armazens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Principal = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Armazens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoriasDespesa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasDespesa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EstacoesPreparacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstacoesPreparacao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetodosPagamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PermiteTroco = table.Column<bool>(type: "bit", nullable: false),
                    ExigeReferencia = table.Column<bool>(type: "bit", nullable: false),
                    ContaNaGaveta = table.Column<bool>(type: "bit", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetodosPagamento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Perfis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Sistema = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perfis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistosAuditoria",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    NomeUtilizador = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Accao = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Entidade = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EntidadeId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Detalhes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnderecoIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistosAuditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeriesDocumento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Prefixo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Ano = table.Column<int>(type: "int", nullable: false),
                    UltimoNumero = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesDocumento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxasImposto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Percentagem = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxasImposto", x => x.Id);
                    table.CheckConstraint("CK_TaxasImposto_Percentagem", "[Percentagem] >= 0 AND [Percentagem] <= 100");
                });

            migrationBuilder.CreateTable(
                name: "Terminais",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminais", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnidadesMedida",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Dimensao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FactorParaBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnidadesMedida", x => x.Id);
                    table.CheckConstraint("CK_UnidadesMedida_Factor", "[FactorParaBase] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Utilizadores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NomeCompleto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    DeveAlterarPalavraPasse = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoLoginUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilizadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zonas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zonas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Impressoras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Ligacao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Endereco = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LarguraPapelMm = table.Column<int>(type: "int", nullable: false),
                    EstacaoPreparacaoId = table.Column<int>(type: "int", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Impressoras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Impressoras_EstacoesPreparacao_EstacaoPreparacaoId",
                        column: x => x.EstacaoPreparacaoId,
                        principalTable: "EstacoesPreparacao",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PerfilPermissoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerfilPermissoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerfilPermissoes_Perfis_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Artigos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnidadeMedidaId = table.Column<int>(type: "int", nullable: false),
                    StockMinimo = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CustoMedio = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UltimoCusto = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PermiteStockNegativo = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artigos", x => x.Id);
                    table.CheckConstraint("CK_Artigos_Valores", "[StockMinimo] >= 0 AND [CustoMedio] >= 0 AND [UltimoCusto] >= 0");
                    table.ForeignKey(
                        name: "FK_Artigos_UnidadesMedida_UnidadeMedidaId",
                        column: x => x.UnidadeMedidaId,
                        principalTable: "UnidadesMedida",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Artigos_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Artigos_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Categorias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CategoriaPaiId = table.Column<int>(type: "int", nullable: true),
                    EstacaoPreparacaoId = table.Column<int>(type: "int", nullable: true),
                    Cor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categorias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categorias_Categorias_CategoriaPaiId",
                        column: x => x.CategoriaPaiId,
                        principalTable: "Categorias",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Categorias_EstacoesPreparacao_EstacaoPreparacaoId",
                        column: x => x.EstacaoPreparacaoId,
                        principalTable: "EstacoesPreparacao",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Categorias_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Categorias_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Telefone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Endereco = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Nuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CartaoFidelidade = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SaldoPontos = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                    table.CheckConstraint("CK_Clientes_Pontos", "[SaldoPontos] >= 0");
                    table.ForeignKey(
                        name: "FK_Clientes_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Clientes_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Configuracoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Chave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Configuracoes_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fornecedores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Nuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Telefone = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Endereco = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fornecedores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fornecedores_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fornecedores_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Funcionarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Funcao = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Telefone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentoIdentificacao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DataAdmissao = table.Column<DateOnly>(type: "date", nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funcionarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Funcionarios_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Funcionarios_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Funcionarios_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Inventarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArmazemId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmadoPorId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inventarios_Armazens_ArmazemId",
                        column: x => x.ArmazemId,
                        principalTable: "Armazens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inventarios_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inventarios_Utilizadores_ConfirmadoPorId",
                        column: x => x.ConfirmadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inventarios_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessoesCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    UtilizadorAberturaId = table.Column<int>(type: "int", nullable: false),
                    AbertaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FundoInicial = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UtilizadorFechoId = table.Column<int>(type: "int", nullable: true),
                    FechadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObservacoesAbertura = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ObservacoesFecho = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessoesCaixa", x => x.Id);
                    table.CheckConstraint("CK_SessoesCaixa_Fundo", "[FundoInicial] >= 0");
                    table.ForeignKey(
                        name: "FK_SessoesCaixa_Terminais_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessoesCaixa_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessoesCaixa_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessoesCaixa_Utilizadores_UtilizadorAberturaId",
                        column: x => x.UtilizadorAberturaId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessoesCaixa_Utilizadores_UtilizadorFechoId",
                        column: x => x.UtilizadorFechoId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilizadorClaims_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UtilizadorLogins_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorPerfis",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorPerfis", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UtilizadorPerfis_Perfis_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UtilizadorPerfis_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UtilizadorTokens_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Mesas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ZonaId = table.Column<int>(type: "int", nullable: false),
                    Capacidade = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OcupadaDesdeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mesas", x => x.Id);
                    table.CheckConstraint("CK_Mesas_Capacidade", "[Capacidade] > 0");
                    table.ForeignKey(
                        name: "FK_Mesas_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mesas_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mesas_Zonas_ZonaId",
                        column: x => x.ZonaId,
                        principalTable: "Zonas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosStock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    ArmazemId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OrigemTipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OrigemId = table.Column<int>(type: "int", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosStock", x => x.Id);
                    table.CheckConstraint("CK_MovimentosStock_Quantidade", "[Quantidade] <> 0");
                    table.ForeignKey(
                        name: "FK_MovimentosStock_Armazens_ArmazemId",
                        column: x => x.ArmazemId,
                        principalTable: "Armazens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosStock_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosStock_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockSaldos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    ArmazemId = table.Column<int>(type: "int", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSaldos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockSaldos_Armazens_ArmazemId",
                        column: x => x.ArmazemId,
                        principalTable: "Armazens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockSaldos_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CodigoBarras = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CategoriaId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrecoVenda = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Custo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaImpostoId = table.Column<int>(type: "int", nullable: true),
                    ImagemUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnviaCozinha = table.Column<bool>(type: "bit", nullable: false),
                    Disponivel = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                    table.CheckConstraint("CK_Produtos_Custo", "[Custo] >= 0");
                    table.CheckConstraint("CK_Produtos_PrecoVenda", "[PrecoVenda] >= 0");
                    table.ForeignKey(
                        name: "FK_Produtos_Categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "Categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Produtos_TaxasImposto_TaxaImpostoId",
                        column: x => x.TaxaImpostoId,
                        principalTable: "TaxasImposto",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Produtos_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Produtos_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Compras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FornecedorId = table.Column<int>(type: "int", nullable: false),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataDocumento = table.Column<DateOnly>(type: "date", nullable: false),
                    DataVencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    ArmazemId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Impostos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmadaPorId = table.Column<int>(type: "int", nullable: true),
                    AnuladaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladaPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Compras", x => x.Id);
                    table.CheckConstraint("CK_Compras_Totais", "[Subtotal] >= 0 AND [Desconto] >= 0 AND [Impostos] >= 0 AND [Total] >= 0");
                    table.ForeignKey(
                        name: "FK_Compras_Armazens_ArmazemId",
                        column: x => x.ArmazemId,
                        principalTable: "Armazens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Compras_Fornecedores_FornecedorId",
                        column: x => x.FornecedorId,
                        principalTable: "Fornecedores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Compras_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Compras_Utilizadores_AnuladaPorId",
                        column: x => x.AnuladaPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Compras_Utilizadores_ConfirmadaPorId",
                        column: x => x.ConfirmadaPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Compras_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventarioLinhas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventarioId = table.Column<int>(type: "int", nullable: false),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    QuantidadeSistema = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantidadeContada = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventarioLinhas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventarioLinhas_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventarioLinhas_Inventarios_InventarioId",
                        column: x => x.InventarioId,
                        principalTable: "Inventarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Despesas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoriaDespesaId = table.Column<int>(type: "int", nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AnuladaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladaPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Despesas", x => x.Id);
                    table.CheckConstraint("CK_Despesas_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_Despesas_CategoriasDespesa_CategoriaDespesaId",
                        column: x => x.CategoriaDespesaId,
                        principalTable: "CategoriasDespesa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Despesas_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Despesas_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Despesas_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Despesas_Utilizadores_AnuladaPorId",
                        column: x => x.AnuladaPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Despesas_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FechoCaixaLinhas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: false),
                    ValorEsperado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorContado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Diferenca = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FechoCaixaLinhas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FechoCaixaLinhas_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FechoCaixaLinhas_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pedidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EstadoEntrega = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    MesaId = table.Column<int>(type: "int", nullable: true),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    GarcomId = table.Column<int>(type: "int", nullable: true),
                    EstafetaId = table.Column<int>(type: "int", nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    NumeroPessoas = table.Column<int>(type: "int", nullable: false),
                    ContactoEntrega = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EnderecoEntrega = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Impostos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaEntrega = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPago = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FechadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StockBaixado = table.Column<bool>(type: "bit", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pedidos", x => x.Id);
                    table.CheckConstraint("CK_Pedidos_Mesa", "[Tipo] <> 'Mesa' OR [MesaId] IS NOT NULL");
                    table.CheckConstraint("CK_Pedidos_Totais", "[Subtotal] >= 0 AND [Desconto] >= 0 AND [Impostos] >= 0 AND [TaxaEntrega] >= 0 AND [Total] >= 0 AND [TotalPago] >= 0");
                    table.ForeignKey(
                        name: "FK_Pedidos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pedidos_Funcionarios_EstafetaId",
                        column: x => x.EstafetaId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pedidos_Funcionarios_GarcomId",
                        column: x => x.GarcomId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pedidos_Mesas_MesaId",
                        column: x => x.MesaId,
                        principalTable: "Mesas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pedidos_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pedidos_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pedidos_Utilizadores_CanceladoPorId",
                        column: x => x.CanceladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pedidos_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    NomeCliente = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Telefone = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InicioUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DuracaoMinutos = table.Column<int>(type: "int", nullable: false),
                    NumeroPessoas = table.Column<int>(type: "int", nullable: false),
                    MesaId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservas", x => x.Id);
                    table.CheckConstraint("CK_Reservas_Duracao", "[DuracaoMinutos] > 0");
                    table.CheckConstraint("CK_Reservas_Pessoas", "[NumeroPessoas] > 0");
                    table.ForeignKey(
                        name: "FK_Reservas_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reservas_Mesas_MesaId",
                        column: x => x.MesaId,
                        principalTable: "Mesas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Reservas_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservas_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Receitas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProdutoId = table.Column<int>(type: "int", nullable: false),
                    Versao = table.Column<int>(type: "int", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    ValidaDesdeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidaAteUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receitas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receitas_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receitas_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receitas_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompraItens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompraId = table.Column<int>(type: "int", nullable: false),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnidadeMedidaId = table.Column<int>(type: "int", nullable: false),
                    CustoUnitario = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaImpostoPercentagem = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ValorImposto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompraItens", x => x.Id);
                    table.CheckConstraint("CK_CompraItens_Valores", "[Quantidade] > 0 AND [CustoUnitario] >= 0 AND [Desconto] >= 0 AND [Total] >= 0");
                    table.ForeignKey(
                        name: "FK_CompraItens_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompraItens_Compras_CompraId",
                        column: x => x.CompraId,
                        principalTable: "Compras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompraItens_UnidadesMedida_UnidadeMedidaId",
                        column: x => x.UnidadeMedidaId,
                        principalTable: "UnidadesMedida",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DespesaId = table.Column<int>(type: "int", nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosCaixa", x => x.Id);
                    table.CheckConstraint("CK_MovimentosCaixa_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_Despesas_DespesaId",
                        column: x => x.DespesaId,
                        principalTable: "Despesas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosPontos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    PedidoId = table.Column<int>(type: "int", nullable: true),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Pontos = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosPontos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosPontos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosPontos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MovimentosPontos_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoId = table.Column<int>(type: "int", nullable: false),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorRecebido = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Troco = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    AnuladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.CheckConstraint("CK_Pagamentos_Valores", "[Valor] > 0 AND [ValorRecebido] >= [Valor] AND [Troco] >= 0");
                    table.ForeignKey(
                        name: "FK_Pagamentos_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Utilizadores_AnuladoPorId",
                        column: x => x.AnuladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PedidoItens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoId = table.Column<int>(type: "int", nullable: false),
                    ProdutoId = table.Column<int>(type: "int", nullable: false),
                    NomeProduto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxaImpostoPercentagem = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorImposto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EnviaCozinha = table.Column<bool>(type: "bit", nullable: false),
                    EstadoCozinha = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EnviadoCozinhaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProntoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cancelado = table.Column<bool>(type: "bit", nullable: false),
                    CanceladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoItens", x => x.Id);
                    table.CheckConstraint("CK_PedidoItens_Quantidade", "[Quantidade] > 0");
                    table.CheckConstraint("CK_PedidoItens_Valores", "[PrecoUnitario] >= 0 AND [Desconto] >= 0 AND [Total] >= 0");
                    table.ForeignKey(
                        name: "FK_PedidoItens_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidoItens_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidoItens_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidoItens_Utilizadores_CanceladoPorId",
                        column: x => x.CanceladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidoItens_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recibos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PedidoId = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmitidoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    Anulado = table.Column<bool>(type: "bit", nullable: false),
                    AnuladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recibos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recibos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recibos_Utilizadores_AnuladoPorId",
                        column: x => x.AnuladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recibos_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferenciasMesa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoId = table.Column<int>(type: "int", nullable: false),
                    MesaOrigemId = table.Column<int>(type: "int", nullable: false),
                    MesaDestinoId = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferenciasMesa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferenciasMesa_Mesas_MesaDestinoId",
                        column: x => x.MesaDestinoId,
                        principalTable: "Mesas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferenciasMesa_Mesas_MesaOrigemId",
                        column: x => x.MesaOrigemId,
                        principalTable: "Mesas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferenciasMesa_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferenciasMesa_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceitaItens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceitaId = table.Column<int>(type: "int", nullable: false),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnidadeMedidaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceitaItens", x => x.Id);
                    table.CheckConstraint("CK_ReceitaItens_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_ReceitaItens_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceitaItens_Receitas_ReceitaId",
                        column: x => x.ReceitaId,
                        principalTable: "Receitas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceitaItens_UnidadesMedida_UnidadeMedidaId",
                        column: x => x.UnidadeMedidaId,
                        principalTable: "UnidadesMedida",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HistoricoPedidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoId = table.Column<int>(type: "int", nullable: false),
                    PedidoItemId = table.Column<int>(type: "int", nullable: true),
                    Accao = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EstadoAnterior = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    EstadoNovo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Detalhes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricoPedidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricoPedidos_PedidoItens_PedidoItemId",
                        column: x => x.PedidoItemId,
                        principalTable: "PedidoItens",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HistoricoPedidos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricoPedidos_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Armazens_Nome",
                table: "Armazens",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Armazens_Principal",
                table: "Armazens",
                column: "Principal",
                unique: true,
                filter: "[Principal] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_AlteradoPorId",
                table: "Artigos",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_Codigo",
                table: "Artigos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_CriadoPorId",
                table: "Artigos",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_Nome",
                table: "Artigos",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_UnidadeMedidaId",
                table: "Artigos",
                column: "UnidadeMedidaId");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_AlteradoPorId",
                table: "Categorias",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_CategoriaPaiId_Nome",
                table: "Categorias",
                columns: new[] { "CategoriaPaiId", "Nome" },
                unique: true,
                filter: "[CategoriaPaiId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_CriadoPorId",
                table: "Categorias",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_EstacaoPreparacaoId",
                table: "Categorias",
                column: "EstacaoPreparacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasDespesa_Nome",
                table: "CategoriasDespesa",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_AlteradoPorId",
                table: "Clientes",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_CartaoFidelidade",
                table: "Clientes",
                column: "CartaoFidelidade",
                unique: true,
                filter: "[CartaoFidelidade] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_CriadoPorId",
                table: "Clientes",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Nome",
                table: "Clientes",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Nuit",
                table: "Clientes",
                column: "Nuit");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Telefone",
                table: "Clientes",
                column: "Telefone",
                unique: true,
                filter: "[Telefone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompraItens_ArtigoId",
                table: "CompraItens",
                column: "ArtigoId");

            migrationBuilder.CreateIndex(
                name: "IX_CompraItens_CompraId",
                table: "CompraItens",
                column: "CompraId");

            migrationBuilder.CreateIndex(
                name: "IX_CompraItens_UnidadeMedidaId",
                table: "CompraItens",
                column: "UnidadeMedidaId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_AlteradoPorId",
                table: "Compras",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_AnuladaPorId",
                table: "Compras",
                column: "AnuladaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_ArmazemId",
                table: "Compras",
                column: "ArmazemId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_ConfirmadaPorId",
                table: "Compras",
                column: "ConfirmadaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_CriadoPorId",
                table: "Compras",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_DataDocumento",
                table: "Compras",
                column: "DataDocumento");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_Estado",
                table: "Compras",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Compras_FornecedorId_NumeroDocumento",
                table: "Compras",
                columns: new[] { "FornecedorId", "NumeroDocumento" },
                unique: true,
                filter: "[Estado] <> 'Anulada'");

            migrationBuilder.CreateIndex(
                name: "IX_Configuracoes_AlteradoPorId",
                table: "Configuracoes",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Configuracoes_Chave",
                table: "Configuracoes",
                column: "Chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_AlteradoPorId",
                table: "Despesas",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_AnuladaPorId",
                table: "Despesas",
                column: "AnuladaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_CategoriaDespesaId_DataUtc",
                table: "Despesas",
                columns: new[] { "CategoriaDespesaId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_CriadoPorId",
                table: "Despesas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_DataUtc",
                table: "Despesas",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_MetodoPagamentoId",
                table: "Despesas",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Despesas_SessaoCaixaId",
                table: "Despesas",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_EstacoesPreparacao_Nome",
                table: "EstacoesPreparacao",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FechoCaixaLinhas_MetodoPagamentoId",
                table: "FechoCaixaLinhas",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_FechoCaixaLinhas_SessaoCaixaId_MetodoPagamentoId",
                table: "FechoCaixaLinhas",
                columns: new[] { "SessaoCaixaId", "MetodoPagamentoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_AlteradoPorId",
                table: "Fornecedores",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_CriadoPorId",
                table: "Fornecedores",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_Nome",
                table: "Fornecedores",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_Nuit",
                table: "Fornecedores",
                column: "Nuit",
                unique: true,
                filter: "[Nuit] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_AlteradoPorId",
                table: "Funcionarios",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_CriadoPorId",
                table: "Funcionarios",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_Nome",
                table: "Funcionarios",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_UtilizadorId",
                table: "Funcionarios",
                column: "UtilizadorId",
                unique: true,
                filter: "[UtilizadorId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricoPedidos_PedidoId_DataUtc",
                table: "HistoricoPedidos",
                columns: new[] { "PedidoId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricoPedidos_PedidoItemId",
                table: "HistoricoPedidos",
                column: "PedidoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricoPedidos_UtilizadorId",
                table: "HistoricoPedidos",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Impressoras_EstacaoPreparacaoId",
                table: "Impressoras",
                column: "EstacaoPreparacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Impressoras_Nome",
                table: "Impressoras",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventarioLinhas_ArtigoId",
                table: "InventarioLinhas",
                column: "ArtigoId");

            migrationBuilder.CreateIndex(
                name: "IX_InventarioLinhas_InventarioId_ArtigoId",
                table: "InventarioLinhas",
                columns: new[] { "InventarioId", "ArtigoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inventarios_AlteradoPorId",
                table: "Inventarios",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventarios_ArmazemId_Estado",
                table: "Inventarios",
                columns: new[] { "ArmazemId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Inventarios_ConfirmadoPorId",
                table: "Inventarios",
                column: "ConfirmadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventarios_CriadoPorId",
                table: "Inventarios",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Mesas_AlteradoPorId",
                table: "Mesas",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Mesas_CriadoPorId",
                table: "Mesas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Mesas_Nome",
                table: "Mesas",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mesas_ZonaId_Estado",
                table: "Mesas",
                columns: new[] { "ZonaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_MetodosPagamento_Codigo",
                table: "MetodosPagamento",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_DataUtc",
                table: "MovimentosCaixa",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_DespesaId",
                table: "MovimentosCaixa",
                column: "DespesaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_MetodoPagamentoId",
                table: "MovimentosCaixa",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_SessaoCaixaId",
                table: "MovimentosCaixa",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_UtilizadorId",
                table: "MovimentosCaixa",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosPontos_ClienteId_DataUtc",
                table: "MovimentosPontos",
                columns: new[] { "ClienteId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosPontos_PedidoId",
                table: "MovimentosPontos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosPontos_UtilizadorId",
                table: "MovimentosPontos",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosStock_ArmazemId",
                table: "MovimentosStock",
                column: "ArmazemId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosStock_ArtigoId_DataUtc",
                table: "MovimentosStock",
                columns: new[] { "ArtigoId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosStock_DataUtc",
                table: "MovimentosStock",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosStock_UtilizadorId",
                table: "MovimentosStock",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "UX_MovimentosStock_Origem",
                table: "MovimentosStock",
                columns: new[] { "Tipo", "OrigemTipo", "OrigemId", "ArtigoId", "ArmazemId" },
                unique: true,
                filter: "[OrigemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_AlteradoPorId",
                table: "Pagamentos",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_AnuladoPorId",
                table: "Pagamentos",
                column: "AnuladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_CriadoPorId",
                table: "Pagamentos",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_DataUtc",
                table: "Pagamentos",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_IdempotencyKey",
                table: "Pagamentos",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_MetodoPagamentoId",
                table: "Pagamentos",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_PedidoId",
                table: "Pagamentos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_SessaoCaixaId_Estado",
                table: "Pagamentos",
                columns: new[] { "SessaoCaixaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_UtilizadorId",
                table: "Pagamentos",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_AlteradoPorId",
                table: "PedidoItens",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_CanceladoPorId",
                table: "PedidoItens",
                column: "CanceladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_CriadoPorId",
                table: "PedidoItens",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_EstadoCozinha_EnviadoCozinhaEmUtc",
                table: "PedidoItens",
                columns: new[] { "EstadoCozinha", "EnviadoCozinhaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_PedidoId",
                table: "PedidoItens",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_ProdutoId",
                table: "PedidoItens",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_AlteradoPorId",
                table: "Pedidos",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CanceladoPorId",
                table: "Pedidos",
                column: "CanceladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_ClienteId",
                table: "Pedidos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoEmUtc",
                table: "Pedidos",
                column: "CriadoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CriadoPorId",
                table: "Pedidos",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Estado_Tipo",
                table: "Pedidos",
                columns: new[] { "Estado", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_EstafetaId",
                table: "Pedidos",
                column: "EstafetaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_FechadoEmUtc",
                table: "Pedidos",
                column: "FechadoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_GarcomId",
                table: "Pedidos",
                column: "GarcomId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_IdempotencyKey",
                table: "Pedidos",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_MesaId_Estado",
                table: "Pedidos",
                columns: new[] { "MesaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Numero",
                table: "Pedidos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_SessaoCaixaId",
                table: "Pedidos",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_PerfilPermissoes_RoleId",
                table: "PerfilPermissoes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Perfis",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_AlteradoPorId",
                table: "Produtos",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CategoriaId_Activo",
                table: "Produtos",
                columns: new[] { "CategoriaId", "Activo" });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Codigo",
                table: "Produtos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CodigoBarras",
                table: "Produtos",
                column: "CodigoBarras",
                unique: true,
                filter: "[CodigoBarras] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CriadoPorId",
                table: "Produtos",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Nome",
                table: "Produtos",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_TaxaImpostoId",
                table: "Produtos",
                column: "TaxaImpostoId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceitaItens_ArtigoId",
                table: "ReceitaItens",
                column: "ArtigoId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceitaItens_ReceitaId_ArtigoId",
                table: "ReceitaItens",
                columns: new[] { "ReceitaId", "ArtigoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceitaItens_UnidadeMedidaId",
                table: "ReceitaItens",
                column: "UnidadeMedidaId");

            migrationBuilder.CreateIndex(
                name: "IX_Receitas_AlteradoPorId",
                table: "Receitas",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Receitas_CriadoPorId",
                table: "Receitas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Receitas_ProdutoId_Versao",
                table: "Receitas",
                columns: new[] { "ProdutoId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Receitas_ProdutoActiva",
                table: "Receitas",
                column: "ProdutoId",
                unique: true,
                filter: "[Activa] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Recibos_AnuladoPorId",
                table: "Recibos",
                column: "AnuladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Recibos_Numero",
                table: "Recibos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recibos_PedidoId",
                table: "Recibos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_Recibos_UtilizadorId",
                table: "Recibos",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistosAuditoria_Accao",
                table: "RegistosAuditoria",
                column: "Accao");

            migrationBuilder.CreateIndex(
                name: "IX_RegistosAuditoria_DataUtc",
                table: "RegistosAuditoria",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RegistosAuditoria_Entidade_EntidadeId",
                table: "RegistosAuditoria",
                columns: new[] { "Entidade", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistosAuditoria_UtilizadorId_DataUtc",
                table: "RegistosAuditoria",
                columns: new[] { "UtilizadorId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_AlteradoPorId",
                table: "Reservas",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_ClienteId",
                table: "Reservas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_CriadoPorId",
                table: "Reservas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_Estado",
                table: "Reservas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_InicioUtc",
                table: "Reservas",
                column: "InicioUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_MesaId_InicioUtc",
                table: "Reservas",
                columns: new[] { "MesaId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_Telefone",
                table: "Reservas",
                column: "Telefone");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesDocumento_Codigo_Ano",
                table: "SeriesDocumento",
                columns: new[] { "Codigo", "Ano" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_AbertaEmUtc",
                table: "SessoesCaixa",
                column: "AbertaEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_AlteradoPorId",
                table: "SessoesCaixa",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_CriadoPorId",
                table: "SessoesCaixa",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_UtilizadorAberturaId_Estado",
                table: "SessoesCaixa",
                columns: new[] { "UtilizadorAberturaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_UtilizadorFechoId",
                table: "SessoesCaixa",
                column: "UtilizadorFechoId");

            migrationBuilder.CreateIndex(
                name: "UX_SessoesCaixa_TerminalAberta",
                table: "SessoesCaixa",
                column: "TerminalId",
                unique: true,
                filter: "[Estado] = 'Aberta'");

            migrationBuilder.CreateIndex(
                name: "IX_StockSaldos_ArmazemId",
                table: "StockSaldos",
                column: "ArmazemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockSaldos_ArtigoId_ArmazemId",
                table: "StockSaldos",
                columns: new[] { "ArtigoId", "ArmazemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxasImposto_Nome",
                table: "TaxasImposto",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terminais_Nome",
                table: "Terminais",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferenciasMesa_MesaDestinoId",
                table: "TransferenciasMesa",
                column: "MesaDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferenciasMesa_MesaOrigemId",
                table: "TransferenciasMesa",
                column: "MesaOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferenciasMesa_PedidoId",
                table: "TransferenciasMesa",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferenciasMesa_UtilizadorId",
                table: "TransferenciasMesa",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_UnidadesMedida_Codigo",
                table: "UnidadesMedida",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorClaims_UserId",
                table: "UtilizadorClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Utilizadores",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Utilizadores",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorLogins_UserId",
                table: "UtilizadorLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorPerfis_RoleId",
                table: "UtilizadorPerfis",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Zonas_Nome",
                table: "Zonas",
                column: "Nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompraItens");

            migrationBuilder.DropTable(
                name: "Configuracoes");

            migrationBuilder.DropTable(
                name: "FechoCaixaLinhas");

            migrationBuilder.DropTable(
                name: "HistoricoPedidos");

            migrationBuilder.DropTable(
                name: "Impressoras");

            migrationBuilder.DropTable(
                name: "InventarioLinhas");

            migrationBuilder.DropTable(
                name: "MovimentosCaixa");

            migrationBuilder.DropTable(
                name: "MovimentosPontos");

            migrationBuilder.DropTable(
                name: "MovimentosStock");

            migrationBuilder.DropTable(
                name: "Pagamentos");

            migrationBuilder.DropTable(
                name: "PerfilPermissoes");

            migrationBuilder.DropTable(
                name: "ReceitaItens");

            migrationBuilder.DropTable(
                name: "Recibos");

            migrationBuilder.DropTable(
                name: "RegistosAuditoria");

            migrationBuilder.DropTable(
                name: "Reservas");

            migrationBuilder.DropTable(
                name: "SeriesDocumento");

            migrationBuilder.DropTable(
                name: "StockSaldos");

            migrationBuilder.DropTable(
                name: "TransferenciasMesa");

            migrationBuilder.DropTable(
                name: "UtilizadorClaims");

            migrationBuilder.DropTable(
                name: "UtilizadorLogins");

            migrationBuilder.DropTable(
                name: "UtilizadorPerfis");

            migrationBuilder.DropTable(
                name: "UtilizadorTokens");

            migrationBuilder.DropTable(
                name: "Compras");

            migrationBuilder.DropTable(
                name: "PedidoItens");

            migrationBuilder.DropTable(
                name: "Inventarios");

            migrationBuilder.DropTable(
                name: "Despesas");

            migrationBuilder.DropTable(
                name: "Receitas");

            migrationBuilder.DropTable(
                name: "Artigos");

            migrationBuilder.DropTable(
                name: "Perfis");

            migrationBuilder.DropTable(
                name: "Fornecedores");

            migrationBuilder.DropTable(
                name: "Pedidos");

            migrationBuilder.DropTable(
                name: "Armazens");

            migrationBuilder.DropTable(
                name: "CategoriasDespesa");

            migrationBuilder.DropTable(
                name: "MetodosPagamento");

            migrationBuilder.DropTable(
                name: "Produtos");

            migrationBuilder.DropTable(
                name: "UnidadesMedida");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "Funcionarios");

            migrationBuilder.DropTable(
                name: "Mesas");

            migrationBuilder.DropTable(
                name: "SessoesCaixa");

            migrationBuilder.DropTable(
                name: "Categorias");

            migrationBuilder.DropTable(
                name: "TaxasImposto");

            migrationBuilder.DropTable(
                name: "Zonas");

            migrationBuilder.DropTable(
                name: "Terminais");

            migrationBuilder.DropTable(
                name: "EstacoesPreparacao");

            migrationBuilder.DropTable(
                name: "Utilizadores");
        }
    }
}
