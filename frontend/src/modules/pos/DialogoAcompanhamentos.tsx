import { useState } from 'react'
import { Check } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useDados } from '../../hooks/useDados'
import type { GrupoPos } from '../../types/api'
import { alternarOpcao, gruposEmFalta } from './acompanhamentos'

/** Escolha dos acompanhamentos ao lançar um prato (ex.: Arroz, Batata, Salada fria). */
export function DialogoAcompanhamentos({ produtoId, produto, aoConfirmar, aoFechar }: {
  produtoId: number
  produto: string
  aoConfirmar: (opcoes: number[]) => void
  aoFechar: () => void
}) {
  const { dados: grupos, erro } = useDados(() => api.get<GrupoPos[]>(`/api/acompanhamentos/produto/${produtoId}`), [produtoId])
  const [escolhidas, setEscolhidas] = useState<number[]>([])
  const emFalta = grupos ? gruposEmFalta(grupos, escolhidas) : []
  const pronto = !!grupos && emFalta.length === 0

  return (
    <Modal titulo={produto} aoFechar={aoFechar} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="btn-primario min-h-12 px-6" disabled={!pronto} onClick={() => aoConfirmar(escolhidas)}>Lançar</button>
      </>}>
      {erro && <Alerta tipo="erro">{erro}</Alerta>}
      {!grupos && !erro && <Carregando />}
      {grupos && (
        <div className="space-y-4">
          {grupos.map((g) => {
            const n = g.opcoes.filter((o) => escolhidas.includes(o.id)).length
            return (
              <fieldset key={g.id}>
                <legend className="mb-2 flex w-full items-baseline justify-between gap-2">
                  <span className="text-lg font-semibold">{g.nome}</span>
                  <span className={`text-sm ${n < g.minimo ? 'font-medium text-amber-700' : 'text-slate-500'}`}>
                    {g.maximo === 1 ? (g.minimo === 1 ? 'escolha 1' : 'opcional') : `${n} de ${g.minimo > 0 ? `${g.minimo} a ` : 'até '}${g.maximo}`}
                  </span>
                </legend>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                  {g.opcoes.map((o) => {
                    const activa = escolhidas.includes(o.id)
                    return (
                      <button key={o.id} type="button" aria-pressed={activa} onClick={() => setEscolhidas((e) => alternarOpcao(e, g, o.id))}
                        className={`flex min-h-16 items-center justify-center gap-2 rounded-xl border-2 px-3 text-center text-base font-medium ${
                          activa ? 'border-primaria-600 bg-primaria-50 text-primaria-800' : 'border-slate-200 bg-white text-slate-800 hover:border-slate-300'
                        }`}>
                        {activa && <Check className="h-5 w-5 shrink-0" aria-hidden />}
                        {o.nome}
                      </button>
                    )
                  })}
                </div>
              </fieldset>
            )
          })}
          <p className="text-xs text-slate-500">Os acompanhamentos não mudam o preço e saem no talão da cozinha com o prato.</p>
        </div>
      )}
    </Modal>
  )
}
