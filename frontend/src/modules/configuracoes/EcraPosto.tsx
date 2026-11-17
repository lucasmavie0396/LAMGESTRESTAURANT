import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro } from '../../hooks/useDados'
import type { PostoEstado } from '../../types/api'
import { ECRA_NORMAL, ESCALAS, definirEcraPosto } from '../../utils/ecraPosto'
import { lerTerminalLocal } from '../../utils/terminalLocal'

const NOMES_ESCALA: Record<number, string> = { 90: '90% (mais pequeno)', 100: '100% (normal)', 110: '110% (maior)', 125: '125% (muito maior)' }

/** Ecrã do posto: modo táctil e tamanho do sistema (ex.: monitores tácteis de 15" a 1024x768). */
export function ConfigurarEcra({ posto, aoFechar }: { posto: PostoEstado; aoFechar: (gravado?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const actual = posto.ecra ?? ECRA_NORMAL
  const [tactil, setTactil] = useState(actual.tactil)
  const [escala, setEscala] = useState(actual.escala)
  const [aGuardar, setAGuardar] = useState(false)

  const guardar = async () => {
    setAGuardar(true)
    try {
      await api.put(`/api/configuracoes/postos/${posto.id}/ecra`, { tactil, escala })
      // Neste computador aplica-se já; nos outros, no minuto seguinte ou ao recarregar a página.
      if (lerTerminalLocal() === posto.id) definirEcraPosto({ tactil, escala })
      avisos.sucesso(`Ecrã do posto ${posto.nome} guardado.`)
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={`Ecrã do posto ${posto.nome}`} aoFechar={() => aoFechar()}>
      <div className="space-y-4">
        <label className="flex cursor-pointer items-start gap-3 rounded-lg border-2 border-slate-200 p-3 has-[:checked]:border-primaria-600 has-[:checked]:bg-primaria-50">
          <input type="checkbox" className="mt-1 h-5 w-5" checked={tactil} onChange={(e) => setTactil(e.target.checked)} />
          <span>
            <span className="block font-semibold">Modo ecrã táctil</span>
            <span className="text-sm text-slate-600">Botões, campos e produtos do POS maiores e com letra maior, para tocar com o dedo.</span>
          </span>
        </label>
        <div>
          <label htmlFor="ecraEscala" className="rotulo">Tamanho do sistema</label>
          <select id="ecraEscala" className="campo" value={escala} onChange={(e) => setEscala(Number(e.target.value))}>
            {ESCALAS.map((e) => <option key={e} value={e}>{NOMES_ESCALA[e]}</option>)}
          </select>
          <p className="mt-1 text-xs text-slate-500">
            Aumenta ou diminui tudo na mesma proporção, sem mexer no Windows. Em monitores de 15" a 1024x768, experimente o modo táctil com 100%;
            se a letra continuar pequena, 110%.
          </p>
        </div>
        <p className="text-sm text-slate-600">O posto aplica a alteração no minuto seguinte (ou logo, ao recarregar a página).</p>
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secundario" onClick={() => aoFechar()}>Cancelar</button>
          <button type="button" className="btn-primario" disabled={!podeGravar || aGuardar} onClick={() => void guardar()}>
            {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
          </button>
        </div>
      </div>
    </Modal>
  )
}
