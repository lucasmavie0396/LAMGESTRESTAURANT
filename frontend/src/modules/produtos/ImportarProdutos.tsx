import { useRef, useState } from 'react'
import { AlertTriangle, CheckCircle2, Download, FileSpreadsheet, Loader2, Upload } from 'lucide-react'
import { api } from '../../api/cliente'
import { Alerta, Etiqueta, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { mensagemErro } from '../../hooks/useDados'
import type { AnaliseImportacaoProdutos, ResultadoImportacaoProdutos } from '../../types/api'
import { formatarMoeda } from '../../utils/formatar'

const MODELO = '/modelos/Modelo-Importacao-Produtos.xlsx'

/** Importação de produtos: carregar o modelo Excel, rever os erros linha a linha e só então gravar (tudo ou nada). */
export function ImportarProdutos({ aoFechar, aoImportar }: { aoFechar: () => void; aoImportar: () => void }) {
  const { podeGravar } = useLigacao()
  const avisos = useAvisos()
  const entrada = useRef<HTMLInputElement>(null)
  const [ficheiro, setFicheiro] = useState<File | null>(null)
  const [analise, setAnalise] = useState<AnaliseImportacaoProdutos | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [aProcessar, setAProcessar] = useState<'analisar' | 'importar' | null>(null)
  const [soErros, setSoErros] = useState(true)

  const corpo = (f: File) => {
    const dados = new FormData()
    dados.append('ficheiro', f)
    return dados
  }

  const analisar = async (f: File) => {
    setFicheiro(f)
    setAnalise(null)
    setErro(null)
    setSoErros(true)
    setAProcessar('analisar')
    try {
      setAnalise(await api.post<AnaliseImportacaoProdutos>('/api/produtos/importacao/analisar', corpo(f)))
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAProcessar(null)
    }
  }

  const importar = async () => {
    if (!ficheiro) return
    setAProcessar('importar')
    setErro(null)
    try {
      const r = await api.post<ResultadoImportacaoProdutos>('/api/produtos/importacao', corpo(ficheiro))
      avisos.sucesso(`${r.importados} produto(s) importado(s).`)
      aoImportar()
    } catch (e) {
      setErro(mensagemErro(e))
    } finally {
      setAProcessar(null)
    }
  }

  const validas = analise ? analise.total - analise.comErros : 0
  const podeImportar = !!analise && analise.total > 0 && analise.comErros === 0 && podeGravar && !aProcessar
  const linhas = (analise?.linhas ?? []).filter((l) => !soErros || !analise?.comErros || l.erros.length > 0)
  const visiveis = linhas.slice(0, 300)

  return (
    <Modal titulo="Importar produtos do Excel" aoFechar={aoFechar} largura="max-w-4xl"
      rodape={<>
        <button type="button" className="btn-secundario" onClick={aoFechar}>Fechar</button>
        <button type="button" className="btn-primario" disabled={!podeImportar} onClick={() => void importar()}>
          {aProcessar === 'importar' && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
          Importar {validas > 0 && analise?.comErros === 0 ? `${validas} produto(s)` : ''}
        </button>
      </>}>
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          Preencha o modelo e carregue-o aqui. O sistema verifica todas as linhas antes de gravar: enquanto houver erros, nada é importado.
          As categorias que ainda não existam são criadas.
        </p>
        <div className="flex flex-wrap gap-2">
          <a href={MODELO} download className="btn-secundario"><Download className="h-4 w-4" aria-hidden /> Descarregar modelo</a>
          <button type="button" className="btn-primario" disabled={!podeGravar || !!aProcessar} onClick={() => entrada.current?.click()}>
            {aProcessar === 'analisar' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Upload className="h-4 w-4" aria-hidden />}
            {ficheiro ? 'Carregar outro ficheiro' : 'Carregar ficheiro'}
          </button>
          <input ref={entrada} type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" className="hidden" aria-label="Ficheiro Excel dos produtos"
            onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) void analisar(f) }} />
        </div>

        {ficheiro && <p className="flex items-center gap-2 text-sm text-slate-700"><FileSpreadsheet className="h-4 w-4 text-green-700" aria-hidden /> {ficheiro.name}</p>}
        {erro && <Alerta tipo="erro">{erro}</Alerta>}

        {analise && analise.total === 0 && <Alerta tipo="aviso">O ficheiro não tem produtos: preencha a folha «Produtos» a partir da linha 2.</Alerta>}
        {analise && analise.total > 0 && (
          <>
            {analise.comErros > 0
              ? <Alerta tipo="erro" titulo={`${analise.comErros} de ${analise.total} linha(s) com erros`}>Corrija o ficheiro e carregue-o de novo. Nada foi gravado.</Alerta>
              : <Alerta tipo="sucesso" titulo={`${analise.total} produto(s) prontos a importar`}>Reveja a lista e carregue em «Importar».</Alerta>}
            {analise.categoriasNovas.length > 0 && (
              <p className="text-sm text-slate-700"><strong>Categorias novas:</strong> {analise.categoriasNovas.join(', ')}</p>
            )}
            {analise.comErros > 0 && analise.comErros < analise.total && (
              <label className="flex min-h-9 items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={soErros} onChange={(e) => setSoErros(e.target.checked)} /> Mostrar só as linhas com erros
              </label>
            )}
            <div className="max-h-96 overflow-auto rounded-lg border border-slate-200">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 bg-slate-50 text-slate-600">
                  <tr>
                    <th scope="col" className="px-3 py-2">Linha</th>
                    <th scope="col" className="px-3 py-2">Código</th>
                    <th scope="col" className="px-3 py-2">Nome</th>
                    <th scope="col" className="px-3 py-2">Categoria</th>
                    <th scope="col" className="px-3 py-2 text-right">Preço</th>
                    <th scope="col" className="px-3 py-2">Estado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {visiveis.map((l) => (
                    <tr key={l.linha} className="align-top">
                      <td className="px-3 py-2 tabular-nums">{l.linha}</td>
                      <td className="px-3 py-2">{l.codigo}</td>
                      <td className="px-3 py-2">{l.nome}</td>
                      <td className="px-3 py-2">{l.categoria}{l.subcategoria && ` › ${l.subcategoria}`}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">{l.precoVenda === null ? '—' : formatarMoeda(l.precoVenda)}</td>
                      <td className="px-3 py-2">
                        {l.erros.length === 0
                          ? <span className="inline-flex items-center gap-1 text-green-700"><CheckCircle2 className="h-4 w-4" aria-hidden /> OK</span>
                          : <ul className="space-y-0.5 text-red-700">{l.erros.map((e) => <li key={e} className="flex gap-1"><AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden /> {e}</li>)}</ul>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {linhas.length > visiveis.length && <p className="text-xs text-slate-500"><Etiqueta cor="cinza">{linhas.length - visiveis.length}</Etiqueta> linha(s) não mostrada(s). Corrija estas e carregue de novo.</p>}
          </>
        )}
      </div>
    </Modal>
  )
}
