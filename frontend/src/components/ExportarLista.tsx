import { useEffect, useRef, useState } from 'react'
import { ChevronDown, Download, FileSpreadsheet, FileText, Loader2, Printer } from 'lucide-react'
import { api } from '../api/cliente'
import { useAvisos } from '../contexts/AvisosContext'
import { useSessao } from '../contexts/SessaoContext'
import { mensagemErro } from '../hooks/useDados'
import type { ConfiguracoesPublicas } from '../types/api'
import { descarregarCsv } from '../utils/csv'
import { documentoLista, MAXIMO_LINHAS_EXPORTACAO, type ListaExportada } from '../utils/exportarLista'
import { imprimirNoNavegador } from '../utils/impressaoDocumento'
import { descarregarXlsx } from '../utils/xlsx'

type Formato = 'xlsx' | 'csv' | 'pdf'

/**
 * Botão «Exportar» das listas: Excel, CSV ou PDF (A4, pela janela de impressão: «Guardar como PDF»), sempre com os filtros
 * do ecrã e todas as páginas (até 20 000 linhas). Cada exportação fica na auditoria.
 */
export function ExportarLista({ obter, desactivado }: { obter: () => Promise<ListaExportada>; desactivado?: boolean }) {
  const avisos = useAvisos()
  const { utilizador } = useSessao()
  const [aberto, setAberto] = useState(false)
  const [aTrabalhar, setATrabalhar] = useState<Formato | null>(null)
  const caixa = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!aberto) return
    const fora = (e: MouseEvent) => { if (!caixa.current?.contains(e.target as Node)) setAberto(false) }
    const tecla = (e: KeyboardEvent) => { if (e.key === 'Escape') setAberto(false) }
    document.addEventListener('mousedown', fora)
    document.addEventListener('keydown', tecla)
    return () => { document.removeEventListener('mousedown', fora); document.removeEventListener('keydown', tecla) }
  }, [aberto])

  const exportar = async (formato: Formato) => {
    setAberto(false)
    setATrabalhar(formato)
    try {
      const lista = await obter()
      await api.post('/api/exportacoes', { lista: lista.titulo, formato, linhas: lista.linhas.length, filtros: lista.filtros.join(' · ') || null })
      if (formato === 'csv') descarregarCsv(lista.nome, lista.cabecalho, lista.linhas)
      else if (formato === 'xlsx') descarregarXlsx(lista.nome, lista.cabecalho, lista.linhas, lista.titulo)
      else {
        const publicas = await api.get<ConfiguracoesPublicas>('/api/configuracoes/publicas').catch(() => null)
        imprimirNoNavegador(documentoLista(lista, publicas?.nomeEstabelecimento || 'LAMGest Restaurant', utilizador?.nomeCompleto ?? null), 210, publicas, 'relatorios')
      }
      if (lista.cortada) avisos.erro(`Saíram só as primeiras ${MAXIMO_LINHAS_EXPORTACAO.toLocaleString('pt-MZ')} linhas. Use filtros para exportar o resto.`)
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível exportar a lista.'))
    } finally {
      setATrabalhar(null)
    }
  }

  const opcoes: [Formato, string, typeof FileText][] = [['xlsx', 'Excel (.xlsx)', FileSpreadsheet], ['csv', 'CSV', FileText], ['pdf', 'PDF / imprimir (A4)', Printer]]
  return (
    <div ref={caixa} className="relative print:hidden">
      <button type="button" className="btn-secundario" aria-haspopup="menu" aria-expanded={aberto} disabled={desactivado || aTrabalhar !== null}
        onClick={() => setAberto((a) => !a)}>
        {aTrabalhar ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Download className="h-4 w-4" aria-hidden />} Exportar
        <ChevronDown className="h-4 w-4" aria-hidden />
      </button>
      {aberto && (
        <div role="menu" aria-label="Formato da exportação" className="absolute right-0 z-30 mt-1 w-56 rounded-lg bg-superficie p-1 shadow-lg ring-1 ring-slate-200">
          {opcoes.map(([f, rotulo, Icone]) => (
            <button key={f} type="button" role="menuitem" className="flex min-h-10 w-full items-center gap-2 rounded-md px-3 text-left text-sm hover:bg-slate-100"
              onClick={() => void exportar(f)}>
              <Icone className={`h-4 w-4 ${f === 'xlsx' ? 'text-green-700' : 'text-slate-600'}`} aria-hidden /> {rotulo}
            </button>
          ))}
          <p className="px-3 pb-1 pt-2 text-xs text-slate-500">Com os filtros do ecrã, todas as páginas.</p>
        </div>
      )}
    </div>
  )
}
