import { useState } from 'react'
import { ExternalLink } from 'lucide-react'
import { useAvisos } from '../contexts/AvisosContext'
import { useEvento, type EventosTempoReal } from '../contexts/TempoRealContext'
import { Modal } from './ui'

type Documento = EventosTempoReal['documentoPdf']

/**
 * Impressoras em modo PDF: o servidor avisa quando o documento (recibo, conta, talão, relatório) está pronto
 * e ele abre aqui, no visualizador de PDF do navegador, para imprimir ou guardar. Vários seguidos ficam em fila.
 */
export function VisualizadorPdf() {
  const [fila, setFila] = useState<Documento[]>([])
  const avisos = useAvisos()
  useEvento('documentoPdf', (d) => setFila((f) => [...f, d]))
  // Qualquer impressão pedida neste posto que falhe (directa ou PDF) é avisada aqui, com o motivo.
  useEvento('impressaoFalhou', (d) => avisos.erro(`Não foi possível imprimir «${d.titulo}»${d.impressora ? ` em ${d.impressora}` : ''}: ${d.erro}`))

  const actual = fila[0]
  if (!actual) return null
  const fechar = () => setFila((f) => f.slice(1))

  return (
    <Modal titulo={`${actual.titulo} · ${actual.impressora}`} aoFechar={fechar} largura="max-w-4xl"
      rodape={<>
        <a className="btn-secundario mr-auto" href={actual.url} target="_blank" rel="noopener">
          <ExternalLink className="h-4 w-4" aria-hidden /> Abrir noutra janela
        </a>
        <button type="button" className="btn-primario" onClick={fechar}>
          {fila.length > 1 ? `Seguinte (${fila.length - 1})` : 'Fechar'}
        </button>
      </>}>
      <iframe key={actual.url} src={actual.url} title={actual.titulo} className="h-[70vh] w-full rounded border border-slate-200" />
      <p className="mt-2 text-xs text-slate-500">Use o botão de impressão do visualizador para imprimir, ou o de transferência para guardar o PDF.</p>
    </Modal>
  )
}
