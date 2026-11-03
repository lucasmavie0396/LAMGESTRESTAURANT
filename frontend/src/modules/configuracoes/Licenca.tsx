import { useRef, useState } from 'react'
import { BadgeCheck, FileUp, Loader2, ShieldAlert } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Carregando, Etiqueta } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro, useDados } from '../../hooks/useDados'
import type { EstadoLicenca, LicencaDto } from '../../types/api'
import { formatarDataHora } from '../../utils/formatar'

const ROTULO: Record<EstadoLicenca, { texto: string; cor: 'verde' | 'amarelo' | 'vermelho' | 'cinza' }> = {
  Valida: { texto: 'Válida', cor: 'verde' },
  AExpirar: { texto: 'A expirar', cor: 'amarelo' },
  Tolerancia: { texto: 'Expirada — em tolerância', cor: 'vermelho' },
  Expirada: { texto: 'Expirada — bloqueada', cor: 'vermelho' },
  Inexistente: { texto: 'Sem licença', cor: 'vermelho' },
  Invalida: { texto: 'Inválida', cor: 'vermelho' },
}

const data = (iso: string) => new Intl.DateTimeFormat('pt-MZ', { timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))

/**
 * Licença do sistema: estado, activação de uma licença nova ou renovada (texto ou ficheiro .lic emitido pela LAM company)
 * e histórico. Activar funciona mesmo com o sistema bloqueado pela licença.
 */
export function Licenca() {
  const { estado: ligacao, licenca, actualizarLicenca } = useLigacao()
  const avisos = useAvisos()
  const historico = useDados(() => api.get<LicencaDto[]>('/api/licenca/historico'), [])
  const [texto, setTexto] = useState('')
  const [aActivar, setAActivar] = useState(false)
  const ficheiro = useRef<HTMLInputElement>(null)

  const activar = async () => {
    setAActivar(true)
    try {
      await api.post('/api/licenca/activar', { texto })
      avisos.sucesso('Licença activada.')
      setTexto('')
      actualizarLicenca()
      await historico.recarregar()
    } catch (e) {
      avisos.erro(mensagemErro(e, 'Não foi possível activar a licença.'))
    } finally {
      setAActivar(false)
    }
  }

  const lerFicheiro = async (f: File | undefined) => {
    if (!f) return
    if (f.size > 10_000) return avisos.erro('O ficheiro não parece uma licença LAMGest.')
    setTexto((await f.text()).trim())
  }

  return (
    <div className="space-y-4">
      <section className="cartao p-4">
        <h2 className="mb-3 font-semibold">Licença actual</h2>
        {!licenca ? <Carregando /> : (
          <div className="flex flex-wrap items-start gap-4">
            {licenca.bloqueada || licenca.estado === 'Tolerancia'
              ? <ShieldAlert className="h-10 w-10 text-red-600" aria-hidden />
              : <BadgeCheck className={`h-10 w-10 ${licenca.estado === 'Valida' ? 'text-green-600' : 'text-amber-500'}`} aria-hidden />}
            <div className="min-w-0 flex-1 space-y-1">
              <p className="flex flex-wrap items-center gap-2 text-lg font-semibold">
                {licenca.nomeComercial ?? 'Sem licença'} <Etiqueta cor={ROTULO[licenca.estado].cor}>{ROTULO[licenca.estado].texto}</Etiqueta>
              </p>
              {licenca.nuit && <p className="text-sm text-slate-600">NUIT {licenca.nuit}{licenca.validaAte && ` · válida até ${data(licenca.validaAte)}`}
                {licenca.diasRestantes !== null && licenca.diasRestantes >= 0 && ` (${licenca.diasRestantes} dias)`}</p>}
              <p className="text-sm">{licenca.mensagem}</p>
            </div>
          </div>
        )}
      </section>

      <section className="cartao p-4">
        <h2 className="mb-1 font-semibold">Activar licença nova ou renovada</h2>
        <p className="mb-3 text-sm text-slate-600">
          Cole o texto da licença enviado pela LAM company (começa por «LAMGEST1.») ou carregue o ficheiro .lic. O nome comercial e o NUIT
          do estabelecimento passam a ser os da licença.
        </p>
        <textarea className="campo min-h-28 py-2 font-mono text-xs" value={texto} onChange={(e) => setTexto(e.target.value)}
          placeholder="LAMGEST1.…" aria-label="Texto da licença" spellCheck={false} />
        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="btn-primario" disabled={aActivar || ligacao !== 'ligado' || !texto.trim()} onClick={() => void activar()}>
            {aActivar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Activar licença
          </button>
          <button type="button" className="btn-secundario" onClick={() => ficheiro.current?.click()}>
            <FileUp className="h-4 w-4" aria-hidden /> Carregar ficheiro .lic
          </button>
          <input ref={ficheiro} type="file" accept=".lic,.txt" className="hidden" onChange={(e) => { void lerFicheiro(e.target.files?.[0]); e.target.value = '' }} />
        </div>
      </section>

      <section className="cartao p-4">
        <h2 className="mb-3 font-semibold">Histórico</h2>
        {historico.erro && <Alerta tipo="erro">{historico.erro}</Alerta>}
        {historico.dados?.length === 0 && <p className="text-sm text-slate-500">Ainda não foi activada nenhuma licença.</p>}
        {historico.dados && historico.dados.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-slate-600">
                <tr className="border-b border-slate-200">
                  <th scope="col" className="py-2">Nome comercial</th>
                  <th scope="col" className="py-2">NUIT</th>
                  <th scope="col" className="py-2">Emitida</th>
                  <th scope="col" className="py-2">Válida até</th>
                  <th scope="col" className="py-2">Activada</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {historico.dados.map((l, i) => (
                  <tr key={l.licencaId} className={i === 0 ? 'font-medium' : 'text-slate-600'}>
                    <td className="py-1.5">{l.nomeComercial}{i === 0 && <span className="ml-2"><Etiqueta cor="azul">Em vigor</Etiqueta></span>}</td>
                    <td className="py-1.5">{l.nuit}</td>
                    <td className="py-1.5">{data(l.emitidaEm)}</td>
                    <td className="py-1.5">{data(l.validaAte)}</td>
                    <td className="py-1.5">{formatarDataHora(l.activadaEmUtc)}{l.activadaPor && ` · ${l.activadaPor}`}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
