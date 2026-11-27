import type {
  Artigo, CartaoEvento, Categoria, Cliente, CompraResumo, Despesa, DiaOperacao, DocumentoResumo, Fornecedor, Funcionario, InventarioResumo, Mesa,
  MovimentoStock, PedidoResumo, Produto, ProdutoFicha, Reserva, SessaoCaixaResumo, Unidade, UtilizadorResumo,
} from '../types/api'
import { dataCurta } from './datas'
import type { ColunaExportacao } from './exportarLista'
import { formatarDataHora } from './formatar'

/** Colunas das listas exportadas (Excel, CSV, PDF): texto primeiro, valores depois (no PDF alinham à direita). */
const dh = (utc: string | null | undefined) => (utc ? formatarDataHora(utc) : null)
const dia = (iso: string | null | undefined) => (iso ? dataCurta(iso) : null)
const simNao = (v: boolean | null | undefined) => (v ? 'Sim' : 'Não')
const activo = (v: boolean) => (v ? 'Activo' : 'Inactivo')
const c = <T,>(titulo: string, valor: (l: T) => string | number | null | undefined): ColunaExportacao<T> => ({ titulo, valor })

export const COLUNAS_PRODUTOS: ColunaExportacao<Produto>[] = [
  c('Código', (p) => p.codigo), c('Nome', (p) => p.nome), c('Categoria', (p) => p.categoriaNome), c('Tipo', (p) => p.tipo),
  c('Código de barras', (p) => p.codigoBarras), c('Unidade', (p) => p.unidade), c('Imposto', (p) => p.taxaImpostoNome),
  c('Estado', (p) => (!p.activo ? 'Inactivo' : p.disponivel ? 'Activo' : 'Esgotado')), c('Controla stock', (p) => simNao(p.controlaStock)),
  c('Preço (MT)', (p) => p.precoVenda), c('Custo (MT)', (p) => p.custo), c('IVA (%)', (p) => p.taxaImpostoPercentagem),
]

export const COLUNAS_CATEGORIAS: ColunaExportacao<Categoria>[] = [
  c('Nome', (x) => x.nome), c('Categoria principal', (x) => x.categoriaPaiNome), c('Estação', (x) => x.estacaoPreparacaoNome),
  c('Impressora', (x) => x.impressoraNome), c('Estado', (x) => activo(x.activo)), c('Ordem', (x) => x.ordem), c('Produtos activos', (x) => x.numeroProdutosActivos),
]

export const COLUNAS_CLIENTES: ColunaExportacao<Cliente>[] = [
  c('Nome', (x) => x.nome), c('Tipo', (x) => x.tipo), c('Telefone', (x) => x.telefone), c('E-mail', (x) => x.email), c('NUIT', (x) => x.nuit),
  c('Endereço', (x) => x.endereco), c('Cartão fidelidade', (x) => x.cartaoFidelidade), c('Estado', (x) => activo(x.activo)), c('Última visita', (x) => dh(x.ultimaVisitaUtc)),
  c('Pedidos pagos', (x) => x.numeroPedidos), c('Total gasto (MT)', (x) => x.totalGasto), c('Pontos', (x) => x.saldoPontos),
]

export const COLUNAS_CARTOES: ColunaExportacao<CartaoEvento>[] = [
  c('Número', (x) => x.numero), c('Cliente', (x) => x.clienteNome), c('Titular', (x) => x.titular), c('Estado', (x) => (x.activo ? 'Activo' : 'Bloqueado')),
  c('Registado', (x) => dh(x.criadoEmUtc)), c('Último movimento', (x) => dh(x.ultimoMovimentoUtc)), c('Saldo (MT)', (x) => x.saldo),
]

export const COLUNAS_FORNECEDORES: ColunaExportacao<Fornecedor>[] = [
  c('Nome', (x) => x.nome), c('NUIT', (x) => x.nuit), c('Telefone', (x) => x.telefone), c('E-mail', (x) => x.email), c('Endereço', (x) => x.endereco),
  c('Estado', (x) => activo(x.activo)), c('Compras', (x) => x.numeroCompras), c('Total comprado (MT)', (x) => x.totalCompras),
]

export const COLUNAS_COMPRAS: ColunaExportacao<CompraResumo>[] = [
  c('Data', (x) => dia(x.dataDocumento)), c('Fornecedor', (x) => x.fornecedor), c('Documento', (x) => x.numeroDocumento), c('Estado', (x) => x.estado),
  c('Vencimento', (x) => dia(x.dataVencimento)), c('Itens', (x) => x.numeroItens), c('Total (MT)', (x) => x.total), c('Pago (MT)', (x) => x.pago ?? 0),
  c('Em dívida (MT)', (x) => (x.estado === 'Confirmada' ? x.total - (x.pago ?? 0) : 0)),
]

export const COLUNAS_ARTIGOS: ColunaExportacao<Artigo>[] = [
  c('Código', (x) => x.codigo), c('Nome', (x) => x.nome), c('Tipo', (x) => x.tipo), c('Categoria', (x) => x.categoria), c('Unidade', (x) => x.unidade),
  c('Localização', (x) => x.localizacao), c('Estado', (x) => activo(x.activo)), c('Abaixo do mínimo', (x) => simNao(x.abaixoMinimo)),
  c('Por armazém', (x) => x.porArmazem.map((a) => `${a.armazem}: ${a.quantidade}`).join('; ') || null),
  c('Saldo', (x) => x.saldo), c('Stock mínimo', (x) => x.stockMinimo), c('Custo médio (MT)', (x) => x.custoMedio), c('Último custo (MT)', (x) => x.ultimoCusto),
  c('Valor do stock (MT)', (x) => x.valorStock),
]

export const COLUNAS_MOVIMENTOS_STOCK: ColunaExportacao<MovimentoStock>[] = [
  c('Data/hora', (x) => dh(x.dataUtc)), c('Artigo', (x) => x.artigo), c('Armazém', (x) => x.armazem), c('Tipo', (x) => x.tipo), c('Unidade', (x) => x.unidade),
  c('Origem', (x) => (x.origemTipo ? `${x.origemTipo}${x.origemId ? ` ${x.origemId}` : ''}` : null)), c('Motivo', (x) => x.motivo), c('Utilizador', (x) => x.utilizador),
  c('Quantidade', (x) => x.quantidade), c('Custo unitário (MT)', (x) => x.custoUnitario),
]

export const COLUNAS_INVENTARIOS: ColunaExportacao<InventarioResumo>[] = [
  c('N.º', (x) => String(x.id)), c('Armazém', (x) => x.armazem), c('Estado', (x) => x.estado), c('Criado', (x) => dh(x.criadoEmUtc)), c('Confirmado', (x) => dh(x.confirmadoEmUtc)),
  c('Linhas', (x) => x.linhas), c('Com diferença', (x) => x.comDiferenca),
]

export const COLUNAS_UNIDADES: ColunaExportacao<Unidade>[] = [
  c('Código', (x) => x.codigo), c('Nome', (x) => x.nome), c('Dimensão', (x) => x.dimensao), c('Estado', (x) => activo(x.activo)), c('Factor para a base', (x) => x.factorParaBase),
]

export const COLUNAS_FICHAS: ColunaExportacao<ProdutoFicha>[] = [
  c('Código', (x) => x.codigo), c('Produto', (x) => x.produto), c('Categoria', (x) => x.categoria), c('Estado', (x) => activo(x.activo)),
  c('Versão activa', (x) => x.versaoActiva), c('Ingredientes', (x) => x.numeroIngredientes), c('Preço (MT)', (x) => x.precoVenda), c('Custo estimado (MT)', (x) => x.custoEstimado),
]

export const COLUNAS_DESPESAS: ColunaExportacao<Despesa>[] = [
  c('Data/hora', (x) => dh(x.dataUtc)), c('Categoria', (x) => x.categoria), c('Descrição', (x) => x.descricao), c('Pago com', (x) => x.metodoPagamento),
  c('Referência', (x) => x.referencia), c('Estado', (x) => x.estado), c('Registada por', (x) => x.utilizador), c('Motivo da anulação', (x) => x.motivoAnulacao),
  c('Valor (MT)', (x) => x.valor),
]

export const COLUNAS_DOCUMENTOS: ColunaExportacao<DocumentoResumo>[] = [
  c('Data/hora', (x) => dh(x.emitidoEmUtc)), c('Tipo', (x) => x.tipo), c('Número', (x) => x.numero), c('Estado', (x) => x.estado), c('Cliente', (x) => x.clienteNome),
  c('NUIT', (x) => x.clienteNuit), c('Vencimento', (x) => dia(x.vencimento)), c('Origem', (x) => x.origem), c('Total (MT)', (x) => x.total), c('Em dívida (MT)', (x) => x.emDivida),
]

export const COLUNAS_SESSOES_CAIXA: ColunaExportacao<SessaoCaixaResumo>[] = [
  c('N.º', (x) => String(x.id)), c('Posto', (x) => x.terminal), c('Estado', (x) => x.estado), c('Aberta por', (x) => x.abertaPor), c('Aberta', (x) => dh(x.abertaEmUtc)),
  c('Fechada', (x) => dh(x.fechadaEmUtc)), c('Fundo inicial (MT)', (x) => x.fundoInicial), c('Diferença (MT)', (x) => x.totalDiferenca),
]

export const COLUNAS_DIAS: ColunaExportacao<DiaOperacao>[] = [
  c('Data', (x) => dia(x.data)), c('N.º', (x) => x.numero), c('Estado', (x) => x.estado), c('Aberto por', (x) => x.abertoPor), c('Aberto', (x) => dh(x.abertoEmUtc)),
  c('Fechado por', (x) => x.fechadoPor), c('Fechado', (x) => dh(x.fechadoEmUtc)), c('Caixas', (x) => x.caixas),
]

export const COLUNAS_UTILIZADORES: ColunaExportacao<UtilizadorResumo>[] = [
  c('Utilizador', (x) => x.nomeUtilizador), c('Nome', (x) => x.nomeCompleto), c('Perfis', (x) => x.perfis.join(', ')), c('Estado', (x) => activo(x.activo)),
  c('Código (PIN)', (x) => simNao(x.temPin)), c('Ecrã táctil', (x) => simNao(x.ecraTactil)), c('Bloqueado até', (x) => dh(x.bloqueadoAteUtc)), c('Última entrada', (x) => dh(x.ultimoLoginUtc)),
]

export const COLUNAS_FUNCIONARIOS: ColunaExportacao<Funcionario>[] = [
  c('Nome', (x) => x.nome), c('Função', (x) => x.funcao), c('Telefone', (x) => x.telefone), c('E-mail', (x) => x.email),
  c('Documento de identificação', (x) => x.documentoIdentificacao), c('Admissão', (x) => dia(x.dataAdmissao)), c('Utilizador', (x) => x.utilizador), c('Estado', (x) => activo(x.activo)),
]

export const COLUNAS_RESERVAS: ColunaExportacao<Reserva>[] = [
  c('Início', (x) => dh(x.inicioUtc)), c('Cliente', (x) => x.nomeCliente), c('Telefone', (x) => x.telefone), c('Mesa', (x) => x.mesaNome), c('Estado', (x) => x.estado),
  c('Observações', (x) => x.observacoes), c('Pessoas', (x) => x.numeroPessoas), c('Duração (min)', (x) => x.duracaoMinutos),
]

export const COLUNAS_MESAS: ColunaExportacao<Mesa>[] = [
  c('Mesa', (x) => x.nome), c('Zona', (x) => x.zonaNome), c('Estado', (x) => (x.activo ? x.estado : 'Inactiva')), c('Responsável', (x) => x.responsavel),
  c('Ocupada desde', (x) => dh(x.ocupadaDesdeUtc)), c('Lugares', (x) => x.capacidade), c('Pedidos abertos', (x) => x.pedidosAbertos), c('Em aberto (MT)', (x) => x.totalAberto),
]

export const COLUNAS_PEDIDOS: ColunaExportacao<PedidoResumo>[] = [
  c('Número', (x) => x.numero), c('Tipo', (x) => x.tipo), c('Estado', (x) => x.estadoEntrega ?? x.estado), c('Mesa', (x) => x.mesaNome), c('Entregar a', (x) => x.nomeEntrega),
  c('Contacto', (x) => x.contactoEntrega), c('Endereço', (x) => x.enderecoEntrega), c('Itens', (x) => x.numeroItens), c('Total (MT)', (x) => x.total), c('Pago (MT)', (x) => x.totalPago),
]
