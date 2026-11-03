import { useRef, useState } from 'react'
import { ExternalLink, Globe, ImageOff, Link2, Loader2, Search, Upload, X } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Modal, Vazio } from '../../components/ui'
import { mensagemErro } from '../../hooks/useDados'
import type { ImagemEncontrada, ImagemGuardada } from '../../types/api'

/**
 * Imagem do produto: pesquisa na internet (imagens com licença livre, Openverse), importação a partir do endereço
 * de uma imagem (ex.: copiado do Google Imagens) ou ficheiro do computador. O servidor reduz a imagem e guarda-a na base.
 */
export function SeletorImagem({ valor, aoMudar, sugestao, desactivado }: {
  valor: string | null
  aoMudar: (url: string | null) => void
  sugestao: string
  desactivado?: boolean
}) {
  const [pesquisar, setPesquisar] = useState(false)
  const [endereco, setEndereco] = useState('')
  const [aTrabalhar, setATrabalhar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [credito, setCredito] = useState<string | null>(null)
  const ficheiro = useRef<HTMLInputElement>(null)

  const executar = async (accao: () => Promise<ImagemGuardada>) => {
    setATrabalhar(true)
    setErro(null)
    try {
      const r = await accao()
      aoMudar(r.url)
      setCredito(r.autor || r.licenca ? [r.autor, r.licenca].filter(Boolean).join(' · ') : null)
      setEndereco('')
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setATrabalhar(false)
    }
  }

  const carregar = (f: File) => {
    const dados = new FormData()
    dados.append('ficheiro', f)
    void executar(() => api.post<ImagemGuardada>('/api/imagens/carregar', dados))
  }

  return (
    <div className="flex flex-col gap-3 sm:flex-row">
      <div className="flex h-32 w-32 shrink-0 items-center justify-center overflow-hidden rounded-lg border border-slate-200 bg-slate-50">
        {aTrabalhar ? <Loader2 className="h-6 w-6 animate-spin text-slate-400" aria-label="A guardar a imagem" />
          : valor ? <img src={valor} alt="Imagem do produto" className="h-full w-full object-cover" />
          : <ImageOff className="h-8 w-8 text-slate-300" aria-label="Sem imagem" />}
      </div>
      <div className="min-w-0 flex-1 space-y-2">
        <div className="flex flex-wrap gap-2">
          <button type="button" className="btn-secundario min-h-9 text-sm" disabled={desactivado || aTrabalhar} onClick={() => setPesquisar(true)}>
            <Globe className="h-4 w-4" aria-hidden /> Procurar na internet
          </button>
          <button type="button" className="btn-secundario min-h-9 text-sm" disabled={desactivado || aTrabalhar} onClick={() => ficheiro.current?.click()}>
            <Upload className="h-4 w-4" aria-hidden /> Do computador
          </button>
          {valor && (
            <button type="button" className="btn-secundario min-h-9 text-sm text-red-700" disabled={desactivado || aTrabalhar} onClick={() => { aoMudar(null); setCredito(null) }}>
              <X className="h-4 w-4" aria-hidden /> Retirar
            </button>
          )}
          <input ref={ficheiro} type="file" accept="image/png,image/jpeg,image/gif,image/bmp" className="hidden" aria-label="Ficheiro de imagem"
            onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) carregar(f) }} />
        </div>
        <div className="flex gap-2">
          <div className="relative flex-1">
            <Link2 className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
            <input className="campo min-h-9 pl-9 text-sm" placeholder="Ou cole o endereço de uma imagem (https://…)" value={endereco}
              onChange={(e) => setEndereco(e.target.value)} disabled={desactivado || aTrabalhar} aria-label="Endereço da imagem"
              onKeyDown={(e) => { if (e.key === 'Enter') e.preventDefault() }} />
          </div>
          <button type="button" className="btn-secundario min-h-9 text-sm" disabled={desactivado || aTrabalhar || !/^https?:\/\/\S+$/i.test(endereco.trim())}
            onClick={() => void executar(() => api.post<ImagemGuardada>('/api/imagens/importar', { url: endereco.trim(), autor: null, licenca: null }))}>
            Importar
          </button>
        </div>
        <p className="text-xs text-slate-500">
          No <a className="text-primaria-700 underline" href={`https://www.google.com/search?tbm=isch&q=${encodeURIComponent(sugestao)}`} target="_blank" rel="noopener noreferrer">
            Google Imagens <ExternalLink className="inline h-3 w-3" aria-hidden /></a>: clique com o botão direito na imagem → «Copiar endereço da imagem» e cole acima.
          Respeite os direitos de autor das imagens.
        </p>
        {credito && <p className="text-xs text-slate-500">Imagem: {credito}</p>}
        {erro && <Alerta tipo="erro">{erro}</Alerta>}
      </div>
      {pesquisar && (
        <PesquisaImagens termoInicial={sugestao} aoFechar={() => setPesquisar(false)}
          aoEscolher={(i) => {
            setPesquisar(false)
            void executar(() => api.post<ImagemGuardada>('/api/imagens/importar', { url: i.url, autor: i.autor, licenca: i.licenca }))
          }} />
      )}
    </div>
  )
}

function PesquisaImagens({ termoInicial, aoEscolher, aoFechar }: { termoInicial: string; aoEscolher: (i: ImagemEncontrada) => void; aoFechar: () => void }) {
  const [termo, setTermo] = useState(termoInicial)
  const [resultados, setResultados] = useState<ImagemEncontrada[] | null>(null)
  const [aProcurar, setAProcurar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const procurar = async () => {
    if (termo.trim().length < 2) return
    setAProcurar(true)
    setErro(null)
    try {
      setResultados(await api.get<ImagemEncontrada[]>(`/api/imagens/pesquisa?q=${encodeURIComponent(termo.trim())}`))
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAProcurar(false)
    }
  }

  return (
    <Modal titulo="Procurar imagem na internet" aoFechar={aoFechar} largura="max-w-4xl">
      {/* Sem <form>: este diálogo fica dentro do formulário do produto e formulários encaixados não são válidos. */}
      <div className="mb-3 flex gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input className="campo pl-9" value={termo} onChange={(e) => setTermo(e.target.value)} placeholder="Ex.: cerveja, frango grelhado, vinho tinto" aria-label="Termo de pesquisa"
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void procurar() } }} />
        </div>
        <button type="button" className="btn-primario" disabled={aProcurar || termo.trim().length < 2} onClick={() => void procurar()}>
          {aProcurar && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />} Procurar
        </button>
      </div>
      <p className="mb-3 text-xs text-slate-500">Imagens com licença livre (Openverse). Pesquisas em inglês costumam dar mais resultados (ex.: «beer bottle»).</p>
      {erro && <Alerta tipo="erro">{erro}</Alerta>}
      {resultados?.length === 0 && <Vazio texto="Sem resultados. Experimente outras palavras." />}
      {!resultados && !erro && !aProcurar && <p className="text-sm text-slate-500">Carregue em «Procurar».</p>}
      {resultados && resultados.length > 0 && (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {resultados.map((r) => (
            <li key={r.url}>
              <button type="button" className="group block w-full overflow-hidden rounded-lg border border-slate-200 text-left hover:border-primaria-500 focus:border-primaria-600"
                onClick={() => aoEscolher(r)} title={r.titulo}>
                <img src={`/api/imagens/miniatura?url=${encodeURIComponent(r.miniatura)}`} alt={r.titulo} loading="lazy" className="aspect-square w-full bg-slate-100 object-cover" />
                <span className="block truncate px-2 pt-1 text-xs font-medium">{r.titulo}</span>
                <span className="block truncate px-2 pb-1 text-[11px] text-slate-500">{[r.autor, r.licenca].filter(Boolean).join(' · ')}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  )
}
