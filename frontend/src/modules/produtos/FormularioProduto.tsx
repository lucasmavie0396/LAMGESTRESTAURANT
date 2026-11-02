import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Loader2 } from 'lucide-react'
import { api, ErroApi } from '../../api/cliente'
import { Alerta, ErroCampo, Modal } from '../../components/ui'
import { useAvisos } from '../../contexts/AvisosContext'
import { useLigacao } from '../../contexts/LigacaoContext'
import type { Categoria, Produto, TaxaImposto, TipoProduto } from '../../types/api'

const valorMonetario = (rotulo: string) =>
  z.coerce
    .number({ invalid_type_error: `${rotulo}: valor inválido.` })
    .min(0, `${rotulo} tem de ser zero ou positivo.`)
    .max(999_999_999, `${rotulo} demasiado alto.`)
    .refine((v) => Number.isInteger(Math.round(v * 100)) && Math.abs(v * 100 - Math.round(v * 100)) < 1e-6, `${rotulo}: no máximo 2 casas decimais.`)

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
  imagemUrl: z.string().trim().max(500, 'Máximo 500 caracteres.'),
  enviaCozinha: z.boolean(),
  disponivel: z.boolean(),
})
type Dados = z.infer<typeof esquema>

export function FormularioProduto({
  produto,
  categorias,
  taxas,
  podeAlterarPreco,
  aoFechar,
  aoGuardar,
}: {
  produto?: Produto
  categorias: Categoria[]
  taxas: TaxaImposto[]
  podeAlterarPreco: boolean
  aoFechar: () => void
  aoGuardar: () => void
}) {
  const avisos = useAvisos()
  const { podeGravar } = useLigacao()
  const [erro, setErro] = useState<string | null>(null)
  const { register, handleSubmit, formState, setError, setValue } = useForm<Dados>({
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
      imagemUrl: produto?.imagemUrl ?? '',
      enviaCozinha: produto?.enviaCozinha ?? true,
      disponivel: produto?.disponivel ?? true,
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

  const submeter = handleSubmit(async (d) => {
    setErro(null)
    const corpo = {
      ...d,
      codigoBarras: d.codigoBarras || null,
      descricao: d.descricao || null,
      imagemUrl: d.imagemUrl || null,
      taxaImpostoId: d.taxaImpostoId ? Number(d.taxaImpostoId) : null,
      tipo: d.tipo as TipoProduto,
      rowVersion: produto?.rowVersion ?? null,
    }
    try {
      if (produto) await api.put(`/api/produtos/${produto.id}`, corpo)
      else await api.post('/api/produtos', corpo)
      avisos.sucesso(produto ? 'Produto actualizado.' : 'Produto criado.')
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
      largura="max-w-2xl"
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
            {categoriasDisponiveis.map((c) => (
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
          <label htmlFor="imagemUrl" className="rotulo">Imagem (endereço, opcional)</label>
          <input id="imagemUrl" className={campo('imagemUrl')} placeholder="/imagens/produtos/frango.jpg" {...register('imagemUrl')} />
          <ErroCampo mensagem={formState.errors.imagemUrl?.message} />
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="descricao" className="rotulo">Descrição (opcional)</label>
          <textarea id="descricao" rows={2} className={`${campo('descricao')} py-2`} {...register('descricao')} />
        </div>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" {...register('enviaCozinha')} />
          Enviar para a cozinha (KDS)
        </label>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" {...register('disponivel')} />
          Disponível para venda
        </label>
      </form>
    </Modal>
  )
}
