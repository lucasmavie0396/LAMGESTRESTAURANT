import { readFileSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { CREDENCIAIS } from './comum.mjs'

function credenciais(): { utilizador: string; palavraPasse: string } {
  return JSON.parse(readFileSync(CREDENCIAIS, 'utf8'))
}

async function entrar(page: Page, palavraPasse = credenciais().palavraPasse) {
  await page.goto('/login')
  await page.getByLabel('Utilizador').fill(credenciais().utilizador)
  await page.getByLabel('Palavra-passe').fill(palavraPasse)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

async function criar<T>(request: APIRequestContext, url: string, dados: unknown): Promise<T> {
  const resposta = await request.post(url, { data: dados })
  expect(resposta.ok(), `${url}: ${resposta.status()} ${await resposta.text()}`).toBeTruthy()
  return resposta.json() as Promise<T>
}

/** Catálogo, sala e caixa mínimos, criados pela API com a sessão do administrador. */
async function prepararRestaurante(request: APIRequestContext, sufixo: string, abrirCaixa = true) {
  const categoria = await criar<{ id: number }>(request, '/api/categorias',
    { nome: `Pratos ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const taxas = await (await request.get('/api/catalogo/taxas')).json() as { id: number; percentagem: number }[]
  await criar(request, '/api/produtos', {
    codigo: `E2E-${sufixo}`, codigoBarras: null, nome: `Frango ${sufixo}`, descricao: null, categoriaId: categoria.id,
    tipo: 'Restaurante', precoVenda: 450.5, custo: 180, taxaImpostoId: taxas.find((t) => t.percentagem === 16)?.id ?? null,
    imagemUrl: null, enviaCozinha: true, disponivel: true, rowVersion: null,
  })
  const zona = await criar<{ id: number }>(request, '/api/zonas', { nome: `Sala ${sufixo}`, ordem: 0 })
  const mesa = await criar<{ id: number; nome: string }>(request, '/api/mesas', { nome: `M${sufixo}`, zonaId: zona.id, capacidade: 4, rowVersion: null })
  const terminal = await criar<{ id: number }>(request, '/api/terminais', { nome: `Caixa ${sufixo}`, descricao: null })
  // Só uma sessão de caixa aberta por utilizador.
  if (abrirCaixa) await criar(request, '/api/caixa/sessoes', { terminalId: terminal.id, fundoInicial: 1000, observacoes: null })
  return { mesa, terminal }
}

test('login recusa credenciais erradas e entra com as correctas', async ({ page }) => {
  await entrar(page, 'errada-123')
  await expect(page.getByText('Utilizador ou palavra-passe inválidos.')).toBeVisible()

  await entrar(page)
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByText('Administrador E2E').first()).toBeVisible()
})

test('venda à mesa: cozinha em tempo real, pagamento, recibo e relatório', async ({ page, context }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const { mesa, terminal } = await prepararRestaurante(page.request, 'A')
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), terminal.id)

  // Ecrã da cozinha aberto noutro separador, como num posto dedicado.
  const cozinha = await context.newPage()
  await cozinha.goto('/cozinha')
  await expect(cozinha.getByText('Sem pedidos para preparar.')).toBeVisible()

  await page.goto('/pos')
  await page.getByRole('button', { name: 'Mesa', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: new RegExp(`^${mesa.nome}`) }).click()
  await page.getByRole('button', { name: /Frango A/ }).click()
  const painel = page.getByLabel('Pedido actual')
  await expect(painel.getByText(`Mesa ${mesa.nome}`)).toBeVisible()
  await painel.getByRole('button', { name: /^Enviar/ }).click()

  // Chega à cozinha sem recarregar a página.
  const ticket = cozinha.getByRole('article').filter({ hasText: `Mesa ${mesa.nome}` })
  await expect(ticket).toBeVisible()
  await ticket.getByRole('button', { name: 'Iniciar', exact: true }).click()
  // Nome exacto: o filtro «Prontos (n)» da barra superior também contém «Pronto».
  await ticket.getByRole('button', { name: 'Pronto', exact: true }).click()
  await expect(ticket.getByRole('button', { name: 'Entregue', exact: true })).toBeVisible()
  await expect(painel.getByText('Pronto', { exact: true })).toBeVisible()

  await painel.getByRole('button', { name: /^Pagar 450,50/ }).click()
  await page.getByRole('dialog').getByRole('button', { name: /^Confirmar 450,50/ }).click()
  await expect(page.getByText(/pago\. Recibo REC-\d{4}-\d{6}/)).toBeVisible()

  const mesas = await (await page.request.get('/api/mesas')).json() as { id: number; estado: string }[]
  expect(mesas.find((m) => m.id === mesa.id)?.estado).toBe('Livre')

  await page.goto('/relatorios?tipo=vendas')
  await expect(page.getByText('1 pedidos')).toBeVisible()
  await expect(page.getByText(/^450,50\s*MT$/).first()).toBeVisible()
})

test('o POS só mostra produtos depois de escolhida a mesa, Take Away ou Delivery', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const { mesa } = await prepararRestaurante(page.request, 'B', false)

  await page.goto('/pos')
  await expect(page.getByRole('heading', { name: 'Iniciar venda' })).toBeVisible()
  await expect(page.getByRole('button', { name: /Frango B/ })).toHaveCount(0)

  // Mesa escolhida directamente no mapa do ecrã inicial.
  await page.getByRole('button', { name: new RegExp(`^${mesa.nome}\\s*Livre`) }).click()
  const painel = page.getByLabel('Pedido actual')
  await expect(painel.getByText(`Mesa ${mesa.nome} (novo)`)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Mesa', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByRole('button', { name: /Frango B/ })).toBeVisible()

  // Take Away pelo endereço (como a partir de outro ecrã): pede o contacto e começa a venda.
  await page.goto('/pos?tipo=TakeAway')
  await page.getByRole('dialog').getByRole('button', { name: 'Continuar' }).click()
  await page.getByRole('button', { name: /Frango B/ }).click()
  await expect(painel.getByText('Take Away', { exact: true })).toBeVisible()
  await expect(painel.getByText(/450,50/).first()).toBeVisible()
})

test('o atalho do posto (?posto=N) associa o computador ao terminal com esse número', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const terminal = await criar<{ id: number }>(page.request, '/api/terminais', { nome: 'Caixa Bar', descricao: null, numero: 5 })

  await page.goto('/pos?posto=5')
  await expect(page.getByText('Posto 5 · Caixa Bar')).toBeVisible()
  await expect(page).toHaveURL(/\/pos$/)
  expect(await page.evaluate(() => localStorage.getItem('lamgest.terminalId'))).toBe(String(terminal.id))

  await page.goto('/?posto=9')
  await expect(page.getByText('Posto 9 não registado')).toBeVisible()
})

test('sem ligação ao servidor as gravações ficam bloqueadas', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  await page.goto('/pos')
  await expect(page.getByRole('button', { name: 'Mesa', exact: true })).toBeEnabled()

  // Simula a queda do servidor: o indicador de ligação deixa de obter resposta.
  await page.route('**/api/saude', (rota) => rota.abort())
  await expect(page.getByRole('alert').filter({ hasText: 'As operações que gravam dados estão bloqueadas' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Mesa', exact: true })).toBeDisabled()

  await page.unroute('**/api/saude')
  await expect(page.getByRole('button', { name: 'Mesa', exact: true })).toBeEnabled({ timeout: 30_000 })
})
