import { useState } from 'react'
import { Loader2, Scale } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useBalanca } from '../../hooks/useBalanca'
import { mensagemErro } from '../../hooks/useDados'
import type { BalancaPosto, FormatoSerie, ModoBalanca, PostoEstado } from '../../types/api'

const MODOS: { valor: ModoBalanca; nome: string; texto: string }[] = [
  { valor: 'Nenhuma', nome: 'Sem balança', texto: 'O peso escreve-se à mão no POS.' },
  { valor: 'Continua', nome: 'Contínua', texto: 'A balança envia o peso sem parar (muitas balanças de balcão).' },
  { valor: 'PedidoEnq', nome: 'A pedido (ENQ)', texto: 'O agente pede o peso: Toledo/Prix e compatíveis.' },
  { valor: 'PedidoW', nome: 'A pedido («W»)', texto: 'O agente pede o peso: CAS, Dibal e outras.' },
]
const VELOCIDADES = [1200, 2400, 4800, 9600, 19200, 38400]
const FORMATOS: { valor: FormatoSerie; nome: string }[] = [
  { valor: 'Oito_N_1', nome: '8 bits, sem paridade (8N1)' },
  { valor: 'Sete_E_1', nome: '7 bits, paridade par (7E1)' },
]

/** Linha da balança no cartão do posto. */
export function ResumoBalanca({ posto: p }: { posto: PostoEstado }) {
  const b = p.balanca
  if (!b || b.modo === 'Nenhuma') return <span className="text-slate-500">sem balança</span>
  return <span>{MODOS.find((m) => m.valor === b.modo)?.nome.toLowerCase()} em {b.porta} ({b.velocidade} bps, {b.formato === 'Sete_E_1' ? '7E1' : '8N1'})</span>
}

/** Balança ligada ao posto (porta COM): o agente lê-a enquanto o POS pede o peso. «Testar» mostra o peso ao vivo. */
export function ConfigurarBalanca({ posto, aoFechar }: { posto: PostoEstado; aoFechar: (gravado?: boolean) => void }) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const actual: BalancaPosto = posto.balanca ?? { modo: 'Nenhuma', porta: null, velocidade: 9600, formato: 'Oito_N_1' }
  // As portas COM vêm do agente do posto; a do visor de poste não serve para a balança.
  const portas = (posto.visor?.portasDetectadas ?? []).filter((p) => !(posto.visor?.tipo === 'Poste' && p.nome === posto.visor.porta))
  const [modo, setModo] = useState<ModoBalanca>(actual.modo)
  const [porta, setPorta] = useState(actual.porta ?? (portas.length === 1 ? portas[0].nome : ''))
  const [velocidade, setVelocidade] = useState(actual.velocidade)
  const [formato, setFormato] = useState<FormatoSerie>(actual.formato)
  const [gravou, setGravou] = useState(false)
  const [aGuardar, setAGuardar] = useState(false)
  const [aTestar, setATestar] = useState(false)
  const leitura = useBalanca(aTestar, posto.id)
  const portaValida = /^COM\d{1,3}$/i.test(porta.trim())
  const alterado = modo !== actual.modo || (modo !== 'Nenhuma' && (porta.trim().toUpperCase() !== actual.porta || velocidade !== actual.velocidade || formato !== actual.formato))
  const podeGuardar = podeGravar && !aGuardar && (modo === 'Nenhuma' || portaValida)

  const guardar = async (testar: boolean) => {
    setAGuardar(true)
    try {
      if (alterado) {
        await api.put(`/api/configuracoes/postos/${posto.id}/balanca`, { modo, porta: porta.trim() || null, velocidade, formato })
        setGravou(true)
      }
      if (testar) {
        setATestar(false)
        window.setTimeout(() => setATestar(true), 0) // recomeça a leitura com a configuração nova
        return
      }
      avisos.sucesso(modo === 'Nenhuma' ? 'Balança desligada.' : 'Balança guardada. O agente do posto lê-a quando o POS pedir o peso.')
      aoFechar(true)
    } catch (e) {
      avisos.erro(mensagemErro(e))
    } finally {
      setAGuardar(false)
    }
  }

  return (
    <Modal titulo={`Balança — ${posto.nome}`} aoFechar={() => aoFechar(gravou)} largura="max-w-2xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={() => aoFechar(gravou)}>Fechar</button>
        {modo !== 'Nenhuma' && (
          <button type="button" className="btn-secundario" disabled={!podeGuardar} onClick={() => void guardar(true)}>
            <Scale className="h-4 w-4" aria-hidden /> {alterado ? 'Guardar e testar' : 'Testar balança'}
          </button>
        )}
        <button type="button" className="btn-primario" disabled={!podeGuardar || !alterado} onClick={() => void guardar(false)}>
          {aGuardar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Guardar
        </button>
      </>}>
      <div className="space-y-4">
        {!posto.agenteRegistado ? (
          <Alerta tipo="aviso">Este posto não tem o agente do LAMGest instalado: a balança ligada ao posto precisa do agente (LAMGest-Setup, modo Posto).</Alerta>
        ) : !posto.agenteLigado && (
          <Alerta tipo="aviso">O agente deste posto está desligado: as portas mostradas são as da última vez que ligou.</Alerta>
        )}

        <fieldset>
          <legend className="rotulo">Leitura da balança</legend>
          <div className="grid gap-2 sm:grid-cols-2">
            {MODOS.map((m) => (
              <label key={m.valor} className={`cursor-pointer rounded-lg border p-3 text-sm ${modo === m.valor ? 'border-primaria-500 bg-primaria-50 ring-1 ring-primaria-500' : 'border-slate-200'}`}>
                <input type="radio" name="modoBalanca" className="mr-2" checked={modo === m.valor} onChange={() => { setModo(m.valor); setATestar(false) }} />
                <span className="font-medium">{m.nome}</span>
                <span className="mt-1 block text-slate-600">{m.texto}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {modo !== 'Nenhuma' && (
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label htmlFor="balancaPorta" className="rotulo">Porta COM</label>
              {portas.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-2" aria-label="Portas detectadas">
                  {portas.map((p) => (
                    <button key={p.nome} type="button" onClick={() => setPorta(p.nome)}
                      className={`rounded-lg border px-3 py-2 text-left text-sm ${porta.toUpperCase() === p.nome ? 'border-primaria-500 bg-primaria-50' : 'border-slate-200'}`}>
                      <span className="font-mono font-semibold">{p.nome}</span>
                      {p.descricao && <span className="block text-xs text-slate-600">{p.descricao}</span>}
                    </button>
                  ))}
                </div>
              )}
              <input id="balancaPorta" className="campo font-mono" maxLength={6} placeholder="COM4" value={porta} onChange={(e) => setPorta(e.target.value.toUpperCase())} />
              {porta && !portaValida && <p className="mt-1 text-xs text-red-700">Porta no formato COM4.</p>}
            </div>
            <div>
              <label htmlFor="balancaVelocidade" className="rotulo">Velocidade (bps)</label>
              <select id="balancaVelocidade" className="campo" value={velocidade} onChange={(e) => setVelocidade(Number(e.target.value))}>
                {VELOCIDADES.map((v) => <option key={v} value={v}>{v}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="balancaFormato" className="rotulo">Formato da porta</label>
              <select id="balancaFormato" className="campo" value={formato} onChange={(e) => setFormato(e.target.value as FormatoSerie)}>
                {FORMATOS.map((f) => <option key={f.valor} value={f.valor}>{f.nome}</option>)}
              </select>
            </div>
            <p className="text-xs text-slate-500 sm:col-span-2">
              Veja a velocidade e o formato no manual ou no menu da balança (o mais comum é 9600 bps, 8N1). A balança tem de estar em quilogramas.
            </p>
          </div>
        )}

        {aTestar && modo !== 'Nenhuma' && (
          <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 text-center" aria-label="Teste da balança">
            {!leitura ? <p className="text-sm text-slate-500">A pedir o peso ao agente…</p> : (
              <>
                <p className="text-4xl font-bold tabular-nums" data-testid="peso-teste">
                  {leitura.peso !== null ? `${leitura.peso.toLocaleString('pt-PT', { minimumFractionDigits: 3 })} kg` : '—'}
                </p>
                <p className={`mt-1 text-sm font-medium ${leitura.erro ? 'text-red-700' : leitura.estavel ? 'text-green-700' : 'text-amber-700'}`}>
                  {leitura.erro ?? (leitura.estavel ? 'Peso estável' : leitura.peso === null ? 'À espera da balança…' : 'A estabilizar…')}
                </p>
                <p className="mt-2 text-xs text-slate-500">Ponha um peso conhecido na balança e confirme o valor.</p>
              </>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
