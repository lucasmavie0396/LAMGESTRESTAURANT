import { useState } from 'react'
import { Loader2, Monitor, Printer } from 'lucide-react'
import { api, query } from '../api/cliente'
import { useAvisos } from '../contexts/AvisosContext'
import { mensagemErro, useDados } from '../hooks/useDados'
import type { Categoria, ConfiguracoesPublicas, DocumentoImpressao, ImpressoraRelatorio, TipoDocumentoRelatorio } from '../types/api'
import { DOCUMENTOS_DE_CLIENTE, imprimirNoNavegador, NOMES_DOCUMENTO, PAPEIS, type LarguraPapel } from '../utils/impressaoDocumento'
import { Alerta, Modal } from './ui'
import { ORDENS, type OrdemFolhaContagem } from '../utils/folhaContagem'

const nomePapel = (mm: number) => (mm >= 210 ? 'A4' : `talão de ${mm} mm`)

/** A última ordem escolhida neste posto (conveniência local). */
const CHAVE_ORDEM = 'lamgest.folha-contagem.ordem'
function ordemGuardada(): OrdemFolhaContagem {
  try {
    const v = localStorage.getItem(CHAVE_ORDEM)
    return ORDENS.some((o) => o.valor === v) ? (v as OrdemFolhaContagem) : 'Alfabetica'
  } catch {
    return 'Alfabetica'
  }
}

/**
 * Impressão de relatórios em 80 mm, 58 mm ou A4, escolhendo sempre o papel: pela janela de impressão deste computador
 * ou directamente numa impressora do servidor (por omissão, o papel configurado nessa impressora).
 */
export function DialogoImprimir({ tipos, tipoInicial, desde, ate, sessaoId, clienteId, clienteNome, diaId, inventarioId, armazemId, aoFechar }: {
  tipos: TipoDocumentoRelatorio[]
  tipoInicial?: TipoDocumentoRelatorio
  desde?: string
  ate?: string
  sessaoId?: number
  /** Histórico e extrato do cliente; nas facturas a crédito, filtra pelo cliente. */
  clienteId?: number
  clienteNome?: string
  /** Fecho do dia: o dia de operação. */
  diaId?: number
  /** Folha de contagem: o inventário. */
  inventarioId?: number
  /** Relatórios de stock: só deste armazém. */
  armazemId?: number
  aoFechar: () => void
}) {
  const avisos = useAvisos()
  const impressoras = useDados(() => api.get<ImpressoraRelatorio[]>('/api/relatorios/impressoras'), [])
  const publicas = useDados(() => api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas'), [])
  const [tipo, setTipo] = useState<TipoDocumentoRelatorio>(tipoInicial ?? tipos[0])
  const [papel, setPapel] = useState<LarguraPapel>(80)
  const [destino, setDestino] = useState<'navegador' | number>('navegador')
  const [aImprimir, setAImprimir] = useState(false)
  // Folha de contagem: cega por omissão (sem o saldo do sistema); ordem, só algumas categorias, página por grupo, sem os de saldo zero.
  const [comSaldo, setComSaldo] = useState(false)
  const [ordem, setOrdem] = useState<OrdemFolhaContagem>(() => ordemGuardada())
  const [soCategorias, setSoCategorias] = useState<number[]>([])
  const [paginaPorGrupo, setPaginaPorGrupo] = useState(false)
  const [esconderZero, setEsconderZero] = useState(false)
  const folha = tipo === 'FolhaContagem'
  const categorias = useDados(() => (folha ? api.get<Categoria[]>('/api/categorias') : Promise.resolve([] as Categoria[])), [folha])
  const comGrupos = ordem === 'Categoria' || ordem === 'Tipo' || ordem === 'Localizacao'
  const opcoesFolha = folha
    ? { comSaldo, ordem, paginaPorGrupo: comGrupos && papel === 210 && paginaPorGrupo, esconderZero }
    : {}
  const escolherOrdem = (o: OrdemFolhaContagem) => {
    setOrdem(o)
    try { localStorage.setItem(CHAVE_ORDEM, o) } catch { /* sem armazenamento: só esta vez */ }
  }

  const impressoraEscolhida = typeof destino === 'number' ? impressoras.dados?.find((i) => i.id === destino) : undefined
  const porCaixa = (tipo === 'CartoesEvento' || tipo === 'DescargasCartoes') && sessaoId !== undefined
  const comPeriodo = tipo !== 'Stock' && tipo !== 'FechoCaixa' && tipo !== 'FechoDia' && tipo !== 'FolhaContagem' && !porCaixa
  const deCliente = DOCUMENTOS_DE_CLIENTE.includes(tipo)

  /** Ao escolher uma impressora do servidor, o papel passa a ser o dela (pode-se mudar a seguir). */
  const escolherDestino = (d: 'navegador' | number) => {
    setDestino(d)
    const i = typeof d === 'number' ? impressoras.dados?.find((x) => x.id === d) : undefined
    if (i && PAPEIS.some((p) => p.valor === i.larguraPapelMm)) setPapel(i.larguraPapelMm as LarguraPapel)
  }

  const imprimir = async () => {
    setAImprimir(true)
    try {
      if (destino === 'navegador') {
        const cats = folha ? soCategorias.map((c) => `&categorias=${c}`).join('') : ''
        const documento = await api.get<DocumentoImpressao>('/api/relatorios/documento' + query({ tipo, desde, ate, sessaoId, clienteId, diaId, inventarioId, armazemId, ...opcoesFolha }) + cats)
        imprimirNoNavegador(documento, papel, publicas.dados, tipo === 'FechoCaixa' || tipo === 'FechoDia' ? 'fecho' : 'relatorios')
      } else {
        const r = await api.post<{ impressora: string; pdf: boolean }>('/api/relatorios/documento/imprimir', {
          tipo, desde: desde ?? null, ate: ate ?? null, sessaoId: sessaoId ?? null, impressoraId: destino, clienteId: clienteId ?? null, diaId: diaId ?? null, inventarioId: inventarioId ?? null, armazemId: armazemId ?? null, ...opcoesFolha, categorias: folha && soCategorias.length ? soCategorias : null, larguraPapelMm: papel,
        })
        avisos.sucesso(r.pdf ? `${NOMES_DOCUMENTO[tipo]}: o PDF abre dentro de momentos.` : `${NOMES_DOCUMENTO[tipo]} enviado para a impressora ${r.impressora} (${nomePapel(papel)}).`)
      }
      aoFechar()
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível imprimir.'))
    } finally {
      setAImprimir(false)
    }
  }

  return (
    <Modal titulo="Imprimir" aoFechar={aoFechar} largura="max-w-xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario" disabled={aImprimir || (deCliente && !clienteId)} onClick={() => void imprimir()}>
          {aImprimir ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Printer className="h-4 w-4" aria-hidden />} Imprimir
        </button>
      </>}>
      <div className="space-y-4">
        {tipos.length > 1 && (
          <div>
            <label htmlFor="docTipo" className="rotulo">Documento</label>
            <select id="docTipo" className="campo" value={tipo} onChange={(e) => setTipo(e.target.value as TipoDocumentoRelatorio)}>
              {tipos.map((t) => <option key={t} value={t}>{NOMES_DOCUMENTO[t]}</option>)}
            </select>
          </div>
        )}
        {tipos.length === 1 && <p className="font-medium">{NOMES_DOCUMENTO[tipo]}</p>}
        {clienteNome && (deCliente || tipo === 'FacturasCredito') && <p className="text-sm text-slate-600">Cliente: <span className="font-medium">{clienteNome}</span></p>}
        {deCliente && !clienteId && <Alerta tipo="aviso">Escolha primeiro o cliente.</Alerta>}
        {comPeriodo && desde && <p className="text-sm text-slate-600">Período: {desde === ate || !ate ? desde : <>{desde}{' a '}{ate}</>}</p>}
        {tipo === 'Stock' && <p className="text-sm text-slate-600">Existências actuais (não depende do período).</p>}
        {porCaixa && <p className="text-sm text-slate-600">Só os movimentos do caixa n.º {sessaoId}.</p>}
        {folha && (
          <div className="space-y-3 rounded-lg border border-slate-200 p-3">
            <div>
              <label htmlFor="folhaOrdem" className="rotulo">Ordem dos artigos</label>
              <select id="folhaOrdem" className="campo" value={ordem} onChange={(e) => escolherOrdem(e.target.value as OrdemFolhaContagem)}>
                {ORDENS.map((o) => <option key={o.valor} value={o.valor}>{o.rotulo}</option>)}
              </select>
            </div>
            <details>
              <summary className="cursor-pointer text-sm font-medium">
                Só algumas categorias {soCategorias.length ? `(${soCategorias.length} escolhidas)` : '(todas)'}
              </summary>
              <div className="mt-2 max-h-48 space-y-1 overflow-y-auto">
                {(categorias.dados ?? []).filter((c) => c.categoriaPaiId === null).map((pai) => (
                  <div key={pai.id}>
                    <label className="flex min-h-9 items-center gap-2 text-sm">
                      <input type="checkbox" className="h-4 w-4" checked={soCategorias.includes(pai.id)}
                        onChange={(e) => setSoCategorias((s) => (e.target.checked ? [...s, pai.id] : s.filter((x) => x !== pai.id)))} />
                      {pai.nome}
                    </label>
                    {(categorias.dados ?? []).filter((c) => c.categoriaPaiId === pai.id).map((filha) => (
                      <label key={filha.id} className="ml-6 flex min-h-9 items-center gap-2 text-sm text-slate-600">
                        <input type="checkbox" className="h-4 w-4" disabled={soCategorias.includes(pai.id)}
                          checked={soCategorias.includes(pai.id) || soCategorias.includes(filha.id)}
                          onChange={(e) => setSoCategorias((s) => (e.target.checked ? [...s, filha.id] : s.filter((x) => x !== filha.id)))} />
                        {filha.nome}
                      </label>
                    ))}
                  </div>
                ))}
                {categorias.dados?.length === 0 && <p className="text-sm text-slate-500">Sem categorias.</p>}
              </div>
              <p className="mt-1 text-xs text-slate-500">Uma categoria inclui as subcategorias. Os artigos sem categoria só saem com «todas».</p>
            </details>
            <label className="flex min-h-11 items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={comSaldo} onChange={(e) => setComSaldo(e.target.checked)} />
              Mostrar o saldo do sistema (por omissão a contagem é cega)
            </label>
            <label className="flex min-h-11 items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={esconderZero} onChange={(e) => setEsconderZero(e.target.checked)} />
              Esconder os artigos com saldo zero
            </label>
            {comGrupos && (
              <label className={`flex min-h-11 items-center gap-2 text-sm ${papel === 210 ? '' : 'text-slate-400'}`}>
                <input type="checkbox" className="h-4 w-4" disabled={papel !== 210} checked={papel === 210 && paginaPorGrupo} onChange={(e) => setPaginaPorGrupo(e.target.checked)} />
                Uma página por grupo, para dividir a contagem por pessoas (só em A4)
              </label>
            )}
          </div>
        )}

        <fieldset>
          <legend className="rotulo">Onde imprimir</legend>
          <div className="space-y-2">
            <label className={`flex cursor-pointer items-center gap-3 rounded-lg border-2 px-3 py-2 text-sm ${destino === 'navegador' ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
              <input type="radio" name="destino" className="h-4 w-4" checked={destino === 'navegador'} onChange={() => escolherDestino('navegador')} />
              <Monitor className="h-4 w-4 text-slate-500" aria-hidden />
              <span><span className="font-medium">Este computador</span><span className="block text-xs text-slate-500">Abre a janela de impressão do Windows (também permite guardar em PDF).</span></span>
            </label>
            {impressoras.dados?.map((i) => (
              <label key={i.id} className={`flex cursor-pointer items-center gap-3 rounded-lg border-2 px-3 py-2 text-sm ${destino === i.id ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                <input type="radio" name="destino" className="h-4 w-4" checked={destino === i.id} onChange={() => escolherDestino(i.id)} />
                <Printer className="h-4 w-4 text-slate-500" aria-hidden />
                <span><span className="font-medium">{i.nome}</span><span className="block text-xs text-slate-500">
                  {i.modo === 'Pdf' ? 'PDF' : 'Impressora do servidor'} · configurada para {i.larguraPapelMm >= 210 ? 'A4' : `${i.larguraPapelMm} mm`}</span></span>
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset>
          <legend className="rotulo">Papel</legend>
          <div className="grid grid-cols-3 gap-2">
            {PAPEIS.map((p) => (
              <label key={p.valor} className={`flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-lg border-2 text-sm font-medium ${papel === p.valor ? 'border-primaria-600 bg-primaria-50' : 'border-slate-200'}`}>
                <input type="radio" name="papel" className="sr-only" checked={papel === p.valor} onChange={() => setPapel(p.valor)} />
                {p.rotulo}
              </label>
            ))}
          </div>
          {destino === 'navegador' && publicas.dados?.logotipo && <p className="mt-2 text-xs text-slate-500">O logótipo do estabelecimento sai nos documentos A4 e nos recibos de 80 mm.</p>}
        </fieldset>

        {impressoraEscolhida && (
          <Alerta tipo={impressoraEscolhida.modo !== 'Pdf' && papel !== impressoraEscolhida.larguraPapelMm ? 'aviso' : 'info'}>
            {impressoraEscolhida.modo === 'Pdf'
              ? `Abre em PDF (${nomePapel(papel)}) neste ecrã, para imprimir ou guardar.`
              : papel !== impressoraEscolhida.larguraPapelMm
                ? `Sai em ${nomePapel(papel)}, mas a impressora está configurada para ${nomePapel(impressoraEscolhida.larguraPapelMm)}: confirme que tem esse papel.`
                : `Sai em ${nomePapel(papel)}, sem janela de impressão.`}
          </Alerta>
        )}
      </div>
    </Modal>
  )
}
