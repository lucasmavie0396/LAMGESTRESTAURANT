import { useState } from 'react'
import { Loader2, Monitor, Printer } from 'lucide-react'
import { api, query } from '../api/cliente'
import { useAvisos } from '../contexts/AvisosContext'
import { mensagemErro, useDados } from '../hooks/useDados'
import type { ConfiguracoesPublicas, DocumentoImpressao, ImpressoraRelatorio, TipoDocumentoRelatorio } from '../types/api'
import { DOCUMENTOS_DE_CLIENTE, imprimirNoNavegador, NOMES_DOCUMENTO, PAPEIS, type LarguraPapel } from '../utils/impressaoDocumento'
import { Alerta, Modal } from './ui'

const nomePapel = (mm: number) => (mm >= 210 ? 'A4' : `talão de ${mm} mm`)

/**
 * Impressão de relatórios em 80 mm, 58 mm ou A4, escolhendo sempre o papel: pela janela de impressão deste computador
 * ou directamente numa impressora do servidor (por omissão, o papel configurado nessa impressora).
 */
export function DialogoImprimir({ tipos, tipoInicial, desde, ate, sessaoId, clienteId, clienteNome, aoFechar }: {
  tipos: TipoDocumentoRelatorio[]
  tipoInicial?: TipoDocumentoRelatorio
  desde?: string
  ate?: string
  sessaoId?: number
  /** Histórico e extrato do cliente; nas facturas a crédito, filtra pelo cliente. */
  clienteId?: number
  clienteNome?: string
  aoFechar: () => void
}) {
  const avisos = useAvisos()
  const impressoras = useDados(() => api.get<ImpressoraRelatorio[]>('/api/relatorios/impressoras'), [])
  const publicas = useDados(() => api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas'), [])
  const [tipo, setTipo] = useState<TipoDocumentoRelatorio>(tipoInicial ?? tipos[0])
  const [papel, setPapel] = useState<LarguraPapel>(80)
  const [destino, setDestino] = useState<'navegador' | number>('navegador')
  const [aImprimir, setAImprimir] = useState(false)

  const impressoraEscolhida = typeof destino === 'number' ? impressoras.dados?.find((i) => i.id === destino) : undefined
  const porCaixa = (tipo === 'CartoesEvento' || tipo === 'DescargasCartoes') && sessaoId !== undefined
  const comPeriodo = tipo !== 'Stock' && tipo !== 'FechoCaixa' && !porCaixa
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
        const documento = await api.get<DocumentoImpressao>('/api/relatorios/documento' + query({ tipo, desde, ate, sessaoId, clienteId }))
        imprimirNoNavegador(documento, papel, publicas.dados, tipo === 'FechoCaixa' ? 'fecho' : 'relatorios')
      } else {
        const r = await api.post<{ impressora: string; pdf: boolean }>('/api/relatorios/documento/imprimir', {
          tipo, desde: desde ?? null, ate: ate ?? null, sessaoId: sessaoId ?? null, impressoraId: destino, clienteId: clienteId ?? null, larguraPapelMm: papel,
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
        {comPeriodo && desde && <p className="text-sm text-slate-600">Período: {desde === ate || !ate ? desde : `${desde} a ${ate}`}</p>}
        {tipo === 'Stock' && <p className="text-sm text-slate-600">Existências actuais (não depende do período).</p>}
        {porCaixa && <p className="text-sm text-slate-600">Só os movimentos do caixa n.º {sessaoId}.</p>}

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
