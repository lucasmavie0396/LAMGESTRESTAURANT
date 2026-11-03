import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Boxes, Loader2 } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, ErroCampo, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import { useDados } from '../../hooks/useDados'
import type { Categoria, ImpressoraTalao, Produto, TaxaImposto, TipoProduto } from '../../types/api'
import { SeletorImagem } from './SeletorImagem'

const valorMonetario = (rotulo: string) =>
  z.coerce
    .number({ invalid_type_error: `${rotulo}: valor inválido.` })
    .min(0, `${rotulo} tem de ser zero ou positivo.`)
    .max(999_999_999, `${rotulo} demasiado alto.`)
    .refine((v) => Number.isInteger(Math.round(v * 100)) && Math.abs(v * 100 - Math.round(v * 100)) < 1e-6, `${rotulo}: no máximo 2 casas decimais.`)

/** Número opcional do stock inicial (aceita vírgula decimal). */
const numeroTexto = (v: string) => Number(v.replace(',', '.'))

const esquema = z.object({
  codigo: z.string().trim().min(1, 'Indique o código interno.').max(30, 'Máximo 30 caracteres.').regex(/^[A-Za-z0-9._-]+$/, 'Só letras, algarismos e . _ -'),
  codigoBarras: z.string().trim().max(50, 'Máximo 50 caracteres.').regex(/^[0-9A-Za-z-]*$/, 'Código de barras inválido.'),
  nome: z.string().trim().min(1, 'Indique o nome do produto.').max(150, 'Máximo 150 caracteres.'),
  descricao: z.string().max(1000, 'Máximo 1000 caracteres.'),
  categoriaId: z.coerce.number().min(1, 'Seleccione a categoria.'),
  tipo: z.enum(['Restaurante', 'Bar', 'Outro']),
  precoVenda: valorMonetario('O preço'),
  custo: valorMonetario('O custo'),
  taxaImpostoId: z.string(),
  impressoraId: z.string(),
  enviaCozinha: z.boolean(),
  disponivel: z.boolean(),
  controlarStock: z.boolean(),
  stockQuantidade: z.string(),
  stockCusto: z.string(),
  stockMinimo: z.string(),
}).superRefine((d, ctx) => {
  if (!d.controlarStock) return
  const verificar = (campo: 'stockQuantidade' | 'stockCusto' | 'stockMinimo', rotulo: string, casas: number) => {
    const v = numeroTexto(d[campo] || '0')
    if (!Number.isFinite(v) || v < 0) ctx.addIssue({ code: 'custom', path: [campo], message: `${rotulo}: valor inválido.` })
    else if (Math.abs(v * 10 ** casas - Math.round(v * 10 ** casas)) > 1e-6) ctx.addIssue({ code: 'custom', path: [campo], message: `${rotulo}: no máximo ${casas} casas decimais.` })
  }
  verificar('stockQuantidade', 'Quantidade', 3)
  verificar('stockCusto', 'Custo', 2)
  verificar('stockMinimo', 'Mínimo', 3)
})
type Dados = z.infer<typeof esquema>

/** Impressora que a categoria (ou a sua principal) define, para mostrar na opção «Da categoria». */
function impressoraDaCategoria(id: number, categorias: Categoria[]) {
  const c = categorias.find((x) => x.id === id)
  if (!c) return null
  return c.impressoraNome ?? categorias.find((p) => p.id === c.categoriaPaiId)?.impressoraNome ?? null
}

export function FormularioProduto({
  produto,
  categorias,
  taxas,
  podeAlterarPreco,
  podeLancarStock,
  aoFechar,
  aoGuardar,
}: {
  produto?: Produto
  categorias: Categoria[]
  taxas: TaxaImposto[]
  podeAlterarPreco: boolean
  podeLancarStock: boolean
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [erro, setErro] = useState<string | null>(null)
  const [imagemUrl, setImagemUrl] = useState<string | null>(produto?.imagemUrl ?? null)
  const impressoras = useDados(() => api.get<ImpressoraTalao[]>('/api/catalogo/impressoras'), [])
  const { register, handleSubmit, formState, setError, setValue, watch } = useForm<Dados>({
    resolver: zodResolver(esquema),
    defaultValues: {
      codigo: produto?.codigo ?? '',
      codigoBarras: produto?.codigoBarras ?? '',
      nome: produto?.nome ?? '',
      descricao: produto?.descricao ?? '',
      categoriaId: produto?.categoriaId ?? 0,
      tipo: produto?.tipo ?? 'Restaurante',
      precoVenda: produto?.precoVenda ?? 0,
      custo: produto?.custo ?? 0,
      taxaImpostoId: produto?.taxaImpostoId?.toString() ?? '',
      impressoraId: produto?.impressoraId?.toString() ?? '',
      enviaCozinha: produto?.enviaCozinha ?? true,
      disponivel: produto?.disponivel ?? true,
      controlarStock: false,
      stockQuantidade: '',
      stockCusto: '',
      stockMinimo: '',
    },
  })

  // Sugestão de código para produtos novos (pode ser alterada).
  useEffect(() => {
    if (produto) return
    api
      .get<{ codigo: string }>('/api/produtos/sugerir-codigo')
      .then((r) => setValue('codigo', r.codigo))
      .catch(() => undefined)
  }, [produto, setValue])

  const precoBloqueado = Boolean(produto) && !podeAlterarPreco
  const categoriasDisponiveis = categorias.filter((c) => c.activo || c.id === produto?.categoriaId)
  // Categorias principais seguidas das suas subcategorias, para a lista ficar agrupada.
  const ordenadas = categoriasDisponiveis.filter((c) => !c.categoriaPaiId)
    .flatMap((p) => [p, ...categoriasDisponiveis.filter((f) => f.categoriaPaiId === p.id)])
    .concat(categoriasDisponiveis.filter((c) => c.categoriaPaiId && !categoriasDisponiveis.some((p) => p.id === c.categoriaPaiId)))
  const categoriaId = Number(watch('categoriaId'))
  const daCategoria = impressoraDaCategoria(categoriaId, categorias)
  const controlarStock = watch('controlarStock')
  const nome = watch('nome')

  const submeter = handleSubmit(async (d) => {
    setErro(null)
    const { controlarStock: comStock, stockQuantidade, stockCusto, stockMinimo, ...resto } = d
    const corpo = {
      ...resto,
      codigoBarras: d.codigoBarras || null,
      descricao: d.descricao || null,
      imagemUrl,
      taxaImpostoId: d.taxaImpostoId ? Number(d.taxaImpostoId) : null,
      impressoraId: d.impressoraId ? Number(d.impressoraId) : null,
      tipo: d.tipo as TipoProduto,
      rowVersion: produto?.rowVersion ?? null,
      stockInicial: !produto && comStock
        ? { quantidade: numeroTexto(stockQuantidade || '0'), custoUnitario: numeroTexto(stockCusto || '0'), stockMinimo: numeroTexto(stockMinimo || '0'), armazemId: null }
        : null,
    }
    try {
      if (produto) await api.put(`/api/produtos/${produto.id}`, corpo)
      else await api.post('/api/produtos', corpo)
      avisos.sucesso(produto ? 'Produto actualizado.' : comStock ? 'Produto criado com o stock inicial.' : 'Produto criado.')
      aoGuardar()
    } catch (e) {
      if (e instanceof ErroApi) {
        for (const [campo, mensagens] of Object.entries(e.erros)) setError(campo as keyof Dados, { message: mensagens[0] })
        setErro(e.estado === 409 && e.message.includes('alterado por outro') ? `${e.message} Feche o formulário e abra-o de novo.` : e.message)
      } else setErro('Não foi possível guardar o produto.')
    }
  })

  const campo = (nome: keyof Dados) => `campo ${formState.errors[nome] ? 'campo-erro' : ''}`

  return (
    <Modal
      titulo={produto ? `Editar ${produto.nome}` : 'Novo produto'}
      aoFechar={aoFechar}
      largura="max-w-3xl"
      rodape={
        <>
          <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
          <button type="submit" form="form-produto" className="btn-primario" disabled={formState.isSubmitting || !podeGravar}>
            {formState.isSubmitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
            Guardar
          </button>
        </>
      }
    >
      <form id="form-produto" onSubmit={submeter} noValidate className="grid gap-4 sm:grid-cols-2">
        {erro && (
          <div className="sm:col-span-2">
            <Alerta tipo="erro">{erro}</Alerta>
          </div>
        )}
        <div>
          <label htmlFor="codigo" className="rotulo">Código interno</label>
          <input id="codigo" className={campo('codigo')} {...register('codigo')} />
          <ErroCampo mensagem={formState.errors.codigo?.message} />
        </div>
        <div>
          <label htmlFor="codigoBarras" className="rotulo">Código de barras (opcional)</label>
          <input id="codigoBarras" className={campo('codigoBarras')} {...register('codigoBarras')} />
          <ErroCampo mensagem={formState.errors.codigoBarras?.message} />
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="nome" className="rotulo">Nome</label>
          <input id="nome" className={campo('nome')} {...register('nome')} />
          <ErroCampo mensagem={formState.errors.nome?.message} />
        </div>
        <div>
          <label htmlFor="categoriaId" className="rotulo">Categoria</label>
          <select id="categoriaId" className={campo('categoriaId')} {...register('categoriaId')}>
            <option value={0}>Seleccione…</option>
            {ordenadas.map((c) => (
              <option key={c.id} value={c.id}>
                {c.categoriaPaiNome ? `${c.categoriaPaiNome} › ${c.nome}` : c.nome}
              </option>
            ))}
          </select>
          <ErroCampo mensagem={formState.errors.categoriaId?.message} />
        </div>
        <div>
          <label htmlFor="tipo" className="rotulo">Tipo</label>
          <select id="tipo" className={campo('tipo')} {...register('tipo')}>
            <option value="Restaurante">Restaurante</option>
            <option value="Bar">Bar</option>
            <option value="Outro">Outro</option>
          </select>
        </div>
        <div>
          <label htmlFor="precoVenda" className="rotulo">Preço de venda (MT, impostos incluídos)</label>
          <input id="precoVenda" type="number" step="0.01" min="0" inputMode="decimal" className={`${campo('precoVenda')} read-only:bg-slate-100`} readOnly={precoBloqueado} {...register('precoVenda')} />
          <ErroCampo mensagem={formState.errors.precoVenda?.message} />
          {precoBloqueado && <p className="mt-1 text-xs text-slate-500">Não tem permissão para alterar preços.</p>}
        </div>
        <div>
          <label htmlFor="custo" className="rotulo">Custo de referência (MT)</label>
          <input id="custo" type="number" step="0.01" min="0" inputMode="decimal" className={campo('custo')} {...register('custo')} />
          <ErroCampo mensagem={formState.errors.custo?.message} />
        </div>
        <div>
          <label htmlFor="taxaImpostoId" className="rotulo">Imposto</label>
          <select id="taxaImpostoId" className={campo('taxaImpostoId')} {...register('taxaImpostoId')}>
            <option value="">Sem imposto definido</option>
            {taxas.filter((t) => t.activo || t.id === produto?.taxaImpostoId).map((t) => (
              <option key={t.id} value={t.id}>{t.nome}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="impressoraId" className="rotulo">Impressora dos pedidos</label>
          <select id="impressoraId" className={campo('impressoraId')} {...register('impressoraId')}>
            <option value="">{daCategoria ? `Da categoria (${daCategoria})` : 'Da categoria (regras automáticas)'}</option>
            {impressoras.dados?.filter((i) => i.activo || String(i.id) === watch('impressoraId')).map((i) => (
              <option key={i.id} value={i.id}>{i.nome} ({i.funcao === 'Producao' ? 'outro centro' : i.funcao})</option>
            ))}
          </select>
        </div>

        <div className="sm:col-span-2">
          <span className="rotulo">Imagem</span>
          <SeletorImagem valor={imagemUrl} aoMudar={setImagemUrl} sugestao={nome || 'produto'} desactivado={!podeGravar} />
        </div>

        <div className="sm:col-span-2">
          <label htmlFor="descricao" className="rotulo">Descrição (opcional)</label>
          <textarea id="descricao" rows={2} className={`${campo('descricao')} py-2`} {...register('descricao')} />
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" {...register('enviaCozinha')} />
          Mostrar no ecrã da cozinha (KDS)
        </label>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" {...register('disponivel')} />
          Disponível para venda
        </label>

        {!produto && podeLancarStock && (
          <fieldset className="rounded-lg border border-slate-200 p-3 sm:col-span-2">
            <legend className="px-1">
              <label className="flex items-center gap-2 text-sm font-medium">
                <input type="checkbox" className="h-4 w-4" {...register('controlarStock')} />
                <Boxes className="h-4 w-4 text-slate-500" aria-hidden /> Controlar stock deste produto e lançar o stock inicial
              </label>
            </legend>
            {controlarStock ? (
              <div className="grid gap-3 sm:grid-cols-3">
                <div>
                  <label htmlFor="stockQuantidade" className="rotulo">Quantidade inicial (unidades)</label>
                  <input id="stockQuantidade" inputMode="decimal" className={campo('stockQuantidade')} placeholder="Ex.: 24" {...register('stockQuantidade')} />
                  <ErroCampo mensagem={formState.errors.stockQuantidade?.message} />
                </div>
                <div>
                  <label htmlFor="stockCusto" className="rotulo">Custo por unidade (MT, sem IVA)</label>
                  <input id="stockCusto" inputMode="decimal" className={campo('stockCusto')} placeholder="Ex.: 45,50" {...register('stockCusto')} />
                  <ErroCampo mensagem={formState.errors.stockCusto?.message} />
                </div>
                <div>
                  <label htmlFor="stockMinimo" className="rotulo">Stock mínimo (alerta)</label>
                  <input id="stockMinimo" inputMode="decimal" className={campo('stockMinimo')} placeholder="Ex.: 6" {...register('stockMinimo')} />
                  <ErroCampo mensagem={formState.errors.stockMinimo?.message} />
                </div>
                <p className="text-xs text-slate-500 sm:col-span-3">
                  Para produtos de revenda (cervejas, refrigerantes, águas…): cria o artigo de stock com o mesmo código, a ficha técnica
                  «1 venda = 1 unidade» e a entrada no armazém principal. Cada venda baixa o stock; as compras repõem-no.
                </p>
              </div>
            ) : (
              <p className="text-xs text-slate-500">Pratos confeccionados usam a ficha técnica com os ingredientes (Stock → Fichas técnicas).</p>
            )}
          </fieldset>
        )}
      </form>
    </Modal>
  )
}
