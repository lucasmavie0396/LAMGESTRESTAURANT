// Postos, estado do servidor, auditoria, lojas, visor e balança. (reexportados por api.ts)
import type { Produto } from './catalogo'
import type { LinhaValor } from './relatorios'

// ---------- Postos ----------

export interface PostoEstado {
  id: number; numero: number | null; nome: string; descricao: string | null; activo: boolean
  online: boolean; vistoEmUtc: string | null; vistoPor: string | null; vistoEndereco: string | null
  caixasAbertos: string[]
  agenteRegistado: boolean; agenteLigado: boolean; agenteVistoEmUtc: string | null; agenteVersao: string | null; impressorasWindows: string[]
  impressorasConfiguradas: string[]
  ligacaoTablets: boolean; tabletsCodigoGeradoEmUtc: string | null
  visor?: VisorPosto
  /** Ecrã do posto: modo táctil e tamanho do sistema em %. */
  ecra?: { tactil: boolean; escala: number }
  balanca?: BalancaPosto
  /** Posto do próprio servidor (abre por localhost), criado pelo instalador. */
  doServidor?: boolean
}
export interface CodigoTablets { terminalId: number; codigo: string; geradoEmUtc: string }
/** `enderecosVpn`: endereços do servidor numa VPN privada (Tailscale), para os tablets fora do restaurante. */
export interface RedeServidor { enderecos: string[]; porta: number; enderecosVpn?: string[] }
/** APK da app de venda servida pelo servidor; enderecos = endereços completos de descarga (HTTP). */
export interface AppVendaServidor { disponivel: boolean; tamanho: number | null; actualizadaEmUtc: string | null; enderecos: string[] }

// ---------- Modo de testes / produção ----------

export interface ContagemDados {
  pedidos: number; documentos: number; clientes: number; cartoesEvento: number; sessoesCaixa: number; movimentosStock: number
  inventarios: number; compras: number; despesas: number; reservas: number; primeiroDocumentoUtc: string | null
}
export interface EstadoProducao { emProducao: boolean; desdeUtc: string | null; dados: ContagemDados | null }
export interface ResultadoProducao { desdeUtc: string; apagou: boolean; copiaSeguranca: string | null; apagados: ContagemDados }

// ---------- Estado do servidor (cópias de segurança e espaço) ----------

export interface EstadoCopias {
  ultimaExecucaoUtc: string | null; ultimaComSucesso: boolean | null; mensagem: string | null
  ultimaCopiaUtc: string | null; ficheiro: string | null; tamanhoMb: number | null
}
export interface EstadoEspaco { baseMb: number; limiteMb: number | null; disco: string | null; discoLivreMb: number | null; discoTotalMb: number | null }
export interface AvisoSistema { nivel: 'aviso' | 'critico'; titulo: string; texto: string }
export interface EstadoCertificado { validoAteUtc: string; nomes: string[]; enderecosEmFalta: string[] }
export interface EstadoSistema { baseDados: string; copias: EstadoCopias; espaco: EstadoEspaco; avisos: AvisoSistema[]; certificado?: EstadoCertificado | null }

// ---------- Auditoria ----------

export interface RegistoAuditoria {
  id: number; dataUtc: string; utilizadorId: number | null; utilizador: string | null; accao: string; entidade: string | null; entidadeId: string | null
  detalhes: string | null; enderecoIp: string | null
}
export interface OpcoesAuditoria { modulos: string[]; accoes: string[]; entidades: string[]; utilizadores: { id: number; nome: string }[] }

// ---------- Lojas ligadas pela VPN (seletor de lojas) ----------

export interface IdentidadeLoja { identificador: string; nome: string; nuit: string | null; cor: string | null }
export interface LojaLigada { id: number; identificador: string; nome: string; nuit: string | null; endereco: string; cor: string | null }
export interface ListaLojas { actual: IdentidadeLoja; outras: LojaLigada[] }

// ---------- Visor do cliente ----------

export type TipoVisor = 'Nenhum' | 'Poste' | 'Monitor'
export type ProtocoloVisor = 'EscPos' | 'Cd5220' | 'Simples'
/** Visor do posto; `endereco` é o caminho da página do visor (segundo monitor ou tablet virado para o cliente). */
export interface VisorPosto {
  tipo: TipoVisor; porta: string | null; protocolo: ProtocoloVisor; velocidade: number; colunas: number; ecra: string | null; endereco: string | null
  portasDetectadas: { nome: string; descricao: string | null }[]
  ecrasDetectados: { nome: string; largura: number; altura: number; principal: boolean }[]
}
export type FaseVisor = 'Repouso' | 'Venda' | 'Pagamento' | 'Pago' | 'Teste' | 'Peso'
export interface ConteudoVisor {
  fase: FaseVisor; mensagem: string; linhas: { descricao: string; quantidade: number; total: number }[]
  total: number; recebido: number | null; troco: number | null; pedido: string | null
  /** Produto a ser pesado no POS (fase Peso). */
  pesagem?: { produto: string; peso: number; unidade: string; precoUnitario: number; valor: number } | null
}
export interface EcraVisor { versao: number; estabelecimento: string; moeda: string; temLogotipo: boolean; conteudo: ConteudoVisor }

// ---------- Balança ----------

/** Como o agente lê a balança do posto: contínua, pedido ENQ (Toledo/Prix) ou pedido «W» (CAS, Dibal e outras). */
export type ModoBalanca = 'Nenhuma' | 'Continua' | 'PedidoEnq' | 'PedidoW'
export type FormatoSerie = 'Oito_N_1' | 'Sete_E_1'
export interface BalancaPosto { modo: ModoBalanca; porta: string | null; velocidade: number; formato: FormatoSerie }
/** Peso da balança do posto; `configurada` falso = o posto não tem balança (escreve-se o peso). */
export interface LeituraBalanca { versao: number; configurada: boolean; peso: number | null; estavel: boolean; erro: string | null }
/** Produto e quantidade de uma etiqueta da balança; `valor` = preço da etiqueta (etiquetas com preço). */
export interface ProdutoEtiqueta { produto: Produto; quantidade: number; valor: number | null }

export interface OpcaoAcompanhamentoLinha { artigoId: number; artigo: string; quantidade: number; unidadeMedidaId: number; unidade: string; armazemId?: number | null; armazem?: string | null }
export interface GrupoAcompanhamentos {
  id: number
  nome: string
  minimo: number
  maximo: number
  activo: boolean
  opcoes: { id: number; nome: string; linhas: OpcaoAcompanhamentoLinha[] }[]
  produtos: { id: number; nome: string }[]
}
/** Grupo de acompanhamentos de um prato, como o POS o mostra. */
export interface GrupoPos { id: number; nome: string; minimo: number; maximo: number; opcoes: { id: number; nome: string }[] }

export interface RelatorioAcompanhamentos {
  desde: string
  ate: string
  total: number
  porOpcao: { grupo: string; opcao: string; vezes: number; pratos: number }[]
  porPrato: { prato: string; opcao: string; vezes: number }[]
  artigos: { artigoId: number; artigo: string; unidade: string; quantidade: number; custo: number }[]
  metodologia: string
}
export interface ExistenciaArtigo {
  artigoId: number; codigo: string; artigo: string; categoria: string | null; unidade: string; armazemId: number; armazem: string
  localizacao: string | null; quantidade: number; minimo: number; abaixoMinimo: boolean; custoMedio: number; valor: number
}
export interface RelatorioExistencias { data: string; armazem: string | null; linhas: ExistenciaArtigo[]; porArmazem: LinhaValor[]; valorTotal: number; abaixoMinimo: number }
export interface TransferenciaArmazem {
  dataUtc: string; artigoId: number; codigo: string; artigo: string; unidade: string; quantidade: number; origem: string; destino: string
  utilizador: string | null; motivo: string | null; valor: number
}
export interface RelatorioTransferencias { desde: string; ate: string; armazem: string | null; linhas: TransferenciaArmazem[]; porPercurso: LinhaValor[] }
