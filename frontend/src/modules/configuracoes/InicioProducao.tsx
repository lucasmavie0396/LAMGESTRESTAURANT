import { useState } from 'react'
import { CheckCircle2, Loader2, Rocket, Trash2 } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Modal } from '../../components/ui'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstadoProducao, ResultadoProducao } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const FRASE = 'APAGAR DADOS DE TESTE'

/**
 * Configurações → Sistema: o sistema começa em modo de testes. O administrador inicia a produção uma única vez,
 * apagando os dados de teste (com cópia de segurança antes) ou mantendo-os.
 */
export function InicioProducao() {
  const estado = useDados(() => api.get<EstadoProducao>('/api/sistema/producao'), [])
  const [dialogo, setDialogo] = useState<'apagar' | 'manter' | null>(null)
  const [resultado, setResultado] = useState<ResultadoProducao | null>(null)

  if (estado.erro) return <Alerta tipo="erro">{estado.erro}</Alerta>
  if (!estado.dados) return <Carregando />
  const e = estado.dados

  if (resultado || e.emProducao)
    return (
      <section className="cartao space-y-3 p-4">
        <h2 className="flex items-center gap-2 text-lg font-semibold"><CheckCircle2 className="h-5 w-5 text-green-600" aria-hidden /> Sistema em produção</h2>
        <p className="text-sm text-slate-600">Desde {formatarDataHora((resultado?.desdeUtc ?? e.desdeUtc)!)}. Os documentos já não se podem apagar: corrigem-se com notas de crédito e anulações.</p>
        {resultado?.apagou && (
          <Alerta tipo="sucesso" titulo="Dados de teste apagados">
            {resultado.apagados.pedidos} pedidos, {resultado.apagados.documentos} documentos, {resultado.apagados.clientes} clientes e {resultado.apagados.cartoesEvento} cartões.
            {resultado.copiaSeguranca && <> Cópia de segurança feita antes: <code className="break-all">{resultado.copiaSeguranca}</code>.</>}
            {' '}Faça agora o inventário inicial do stock.
          </Alerta>
        )}
      </section>
    )

  const d = e.dados
  const linhas: [string, number][] = d ? [
    ['Pedidos (vendas)', d.pedidos], ['Documentos (VD, facturas, recibos, notas de crédito, cotações)', d.documentos], ['Clientes', d.clientes],
    ['Cartões Evento', d.cartoesEvento], ['Sessões de caixa', d.sessoesCaixa], ['Movimentos de stock', d.movimentosStock], ['Inventários', d.inventarios],
    ['Compras', d.compras], ['Despesas', d.despesas], ['Reservas', d.reservas],
  ] : []

  return (
    <section className="space-y-4">
      <Alerta tipo="aviso" titulo="O sistema está em modo de testes">
        Use-o à vontade para aprender. Quando acabar os testes, inicie a produção: os dados de teste podem ser apagados uma única vez, antes da primeira venda real.
      </Alerta>
      <div className="cartao p-4">
        <h2 className="mb-2 font-semibold">Dados registados até agora</h2>
        {d?.primeiroDocumentoUtc && <p className="mb-2 text-sm text-slate-600">Primeiro documento: {formatarDataHora(d.primeiroDocumentoUtc)}</p>}
        <table className="w-full text-sm">
          <tbody className="divide-y divide-slate-100">
            {linhas.map(([rotulo, n]) => <tr key={rotulo}><td className="py-1.5">{rotulo}</td><td className="py-1.5 text-right font-medium tabular-nums">{n}</td></tr>)}
          </tbody>
        </table>
        <p className="mt-3 text-xs text-slate-500">
          Ficam sempre: configurações, produtos e categorias, preços, unidades, armazéns, fichas técnicas, utilizadores e perfis, funcionários, mesas,
          impressoras, postos, fornecedores e a licença. Os saldos de stock ficam a zero.
        </p>
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        <button type="button" className="cartao flex items-start gap-3 p-4 text-left ring-2 ring-red-200 hover:bg-red-50" onClick={() => setDialogo('apagar')}>
          <Trash2 className="mt-0.5 h-6 w-6 shrink-0 text-red-600" aria-hidden />
          <span><span className="block font-semibold">Apagar dados de teste e iniciar produção</span>
            <span className="text-sm text-slate-600">Apaga vendas, documentos, caixa, clientes, cartões, stock e compras. A numeração recomeça no 1. Faz uma cópia de segurança antes.</span></span>
        </button>
        <button type="button" className="cartao flex items-start gap-3 p-4 text-left hover:bg-slate-50" onClick={() => setDialogo('manter')}>
          <Rocket className="mt-0.5 h-6 w-6 shrink-0 text-primaria-600" aria-hidden />
          <span><span className="block font-semibold">Iniciar produção mantendo os dados</span>
            <span className="text-sm text-slate-600">Para quem já começou a vender a sério: nada é apagado e o modo de testes termina.</span></span>
        </button>
      </div>
      {dialogo && <ConfirmarProducao apagar={dialogo === 'apagar'} aoFechar={() => setDialogo(null)} aoConcluir={(r) => { setDialogo(null); setResultado(r); void estado.recarregar() }} />}
    </section>
  )
}

function ConfirmarProducao({ apagar, aoFechar, aoConcluir }: { apagar: boolean; aoFechar: () => void; aoConcluir: (r: ResultadoProducao) => void }) {
  const { podeGravar } = useLigacao()
  const [palavraPasse, setPalavraPasse] = useState('')
  const [frase, setFrase] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [aGravar, setAGravar] = useState(false)
  const valido = palavraPasse.length > 0 && (!apagar || frase.trim() === FRASE)

  const confirmar = async () => {
    setAGravar(true)
    setErro(null)
    try {
      aoConcluir(await api.post<ResultadoProducao>('/api/sistema/producao', { palavraPasse, apagarDadosTeste: apagar, confirmacao: apagar ? frase.trim() : null }))
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAGravar(false)
    }
  }

  return (
    <Modal titulo={apagar ? 'Apagar dados de teste e iniciar produção' : 'Iniciar produção mantendo os dados'} aoFechar={aoFechar}
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className={apagar ? 'btn-perigo' : 'btn-primario'} disabled={!valido || aGravar || !podeGravar} onClick={() => void confirmar()}>
          {aGravar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} {apagar ? 'Apagar e iniciar produção' : 'Iniciar produção'}
        </button>
      </>}>
      <div className="space-y-3">
        <Alerta tipo={apagar ? 'erro' : 'aviso'}>
          {apagar
            ? 'Todos os dados de teste são apagados e não podem ser recuperados no sistema (só pela cópia de segurança). Feche as vendas nos outros postos antes de continuar. Esta operação só se pode fazer uma vez.'
            : 'O modo de testes termina e os dados ficam. Depois disto, os dados de teste já não se podem apagar.'}
        </Alerta>
        {apagar && (
          <div>
            <label htmlFor="prodFrase" className="rotulo">Escreva <strong>{FRASE}</strong></label>
            <input id="prodFrase" className="campo" autoComplete="off" value={frase} onChange={(e) => setFrase(e.target.value)} />
          </div>
        )}
        <div>
          <label htmlFor="prodSenha" className="rotulo">A sua palavra-passe</label>
          <input id="prodSenha" type="password" className="campo" autoComplete="current-password" value={palavraPasse} onChange={(e) => setPalavraPasse(e.target.value)} />
        </div>
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
      </div>
    </Modal>
  )
}
