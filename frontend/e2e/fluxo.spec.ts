import { execFileSync, spawn } from 'node:child_process'
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { strFromU8, unzipSync } from 'fflate'
import { CREDENCIAIS, PORTA_API } from './comum.mjs'
import { gerarXlsx } from '../src/utils/xlsx'

function credenciais(): { utilizador: string; palavraPasse: string; licencaRenovada: string } {
  return JSON.parse(readFileSync(CREDENCIAIS, 'utf8'))
}

async function entrar(page: Page, palavraPasse = credenciais().palavraPasse) {
  await page.goto('/login')
  await page.getByLabel('Utilizador').fill(credenciais().utilizador)
  await page.getByLabel('Palavra-passe').fill(palavraPasse)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
}

async function criar<T>(request: APIRequestContext, url: string, dados: unknown): Promise<T> {
  const resposta = await request.post(url, { data: dados })
  expect(resposta.ok(), `${url}: ${resposta.status()} ${await resposta.text()}`).toBeTruthy()
  return resposta.json() as Promise<T>
}

/** Abre o dia de operação se ainda não estiver aberto (os caixas só abrem com o dia aberto). */
async function garantirDiaAberto(request: APIRequestContext) {
  const resposta = await request.post('/api/caixa/dias', { data: { data: null, observacoes: null } })
  expect([201, 409], `Abrir Dia: ${resposta.status()} ${await resposta.text()}`).toContain(resposta.status())
}

/** Sessão de caixa do administrador no terminal, depois de garantir o dia aberto. */
async function abrirSessaoCaixa(request: APIRequestContext, terminalId: number, fundoInicial = 0) {
  await garantirDiaAberto(request)
  await criar(request, '/api/caixa/sessoes', { terminalId, fundoInicial, observacoes: null })
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
  if (abrirCaixa) await abrirSessaoCaixa(request, terminal.id, 1000)
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
  test.setTimeout(150_000)
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
  // Responsável da mesa: quem a abriu (o administrador pode passá-la a outro).
  // No topo, junto do número da mesa; as opções menos usadas ficam no menu «⋯» e à vista só Enviar, Pedir conta e Pagar.
  await expect(painel.getByTestId('responsavel-pedido')).toHaveText('Responsável da mesa: Administrador E2E')
  await expect(painel.getByRole('button', { name: 'Desconto' })).toHaveCount(0)
  await painel.getByRole('button', { name: 'Mais opções do pedido' }).click()
  const opcoesPedido = painel.getByRole('menu', { name: 'Opções do pedido' })
  await expect(opcoesPedido.getByRole('menuitem')).toHaveText(['Associar cliente (fidelização)', 'Comentário do pedido', 'Desconto', 'Transferir', 'Passar mesa', 'Cancelar pedido'])
  await page.keyboard.press('Escape')
  await expect(opcoesPedido).toBeHidden()
  await painel.getByRole('button', { name: /^Enviar/ }).click()

  // Chega à cozinha sem recarregar a página.
  const ticket = cozinha.getByRole('article').filter({ hasText: `Mesa ${mesa.nome}` })
  await expect(ticket).toBeVisible()
  await ticket.getByRole('button', { name: 'Iniciar', exact: true }).click()
  // Nome exacto: o filtro «Prontos (n)» da barra superior também contém «Pronto».
  await ticket.getByRole('button', { name: 'Pronto', exact: true }).click()
  await expect(ticket.getByRole('button', { name: 'Entregue', exact: true })).toBeVisible()
  await expect(painel.getByText('Pronto', { exact: true })).toBeVisible()

  // Dois métodos de uma vez: 400 em dinheiro + o resto (50,50) em cartão.
  await painel.getByRole('button', { name: /^Pagar 450,50/ }).click()
  const pagamento = page.getByRole('dialog')
  await pagamento.getByLabel('Valor (MT)', { exact: true }).fill('400')
  await pagamento.getByRole('button', { name: 'Juntar outro método de pagamento' }).click()
  const segundo = pagamento.getByRole('listitem', { name: 'Pagamento 2' })
  await expect(segundo.getByRole('button', { name: 'Cartão', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await expect(segundo.getByLabel('Valor (MT)', { exact: true })).toHaveValue('50.50')
  await pagamento.getByRole('button', { name: /^Confirmar 450,50/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-\d{4}-\d{6}/)).toBeVisible()

  const mesas = await (await page.request.get('/api/mesas')).json() as { id: number; estado: string }[]
  expect(mesas.find((m) => m.id === mesa.id)?.estado).toBe('Livre')

  await page.goto('/relatorios?tipo=vendas')
  await expect(page.getByText('1 pedidos')).toBeVisible()
  await expect(page.getByText(/^450,50\s*MT$/).first()).toBeVisible()

  // Lista de vendas: a venda com os dois métodos, e a exportação Excel abre como .xlsx válido.
  await page.goto('/relatorios?tipo=lista')
  const linha = page.getByRole('row').filter({ hasText: /PED-\d{4}-\d{6}/ }).first()
  await expect(linha).toContainText('Dinheiro, Cartão')
  await expect(linha).toContainText('450,50')
  const [descarga] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Exportar Vendas em Excel' }).click()])
  expect(descarga.suggestedFilename()).toMatch(/^lista-vendas_.*\.xlsx$/)
  const ficheiros = unzipSync(new Uint8Array(readFileSync(await descarga.path())))
  const folha = strFromU8(ficheiros['xl/worksheets/sheet1.xml'])
  expect(folha).toContain('Dinheiro, Cartão')
  expect(folha).toContain('<v>450.5</v>')
})

test('o POS só mostra produtos depois de escolhida a mesa, Take Away ou Delivery', async ({ page }) => {
  test.setTimeout(150_000)
  // Os postos abrem http://IP:porta (contexto não seguro): o navegador não tem crypto.randomUUID.
  await page.addInitScript(() => Object.defineProperty(crypto, 'randomUUID', { value: undefined }))
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

  // Delivery: nome, contacto e endereço obrigatórios (saem no recibo); o pedido fica com o nome.
  await page.getByRole('button', { name: 'Delivery', exact: true }).click()
  const entrega = page.getByRole('dialog', { name: 'Novo pedido Delivery' })
  await entrega.getByLabel('Contacto (obrigatório)').fill('84 555 6666')
  await entrega.getByLabel('Endereço de entrega (obrigatório)').fill('Av. 24 de Julho 50')
  await expect(entrega.getByRole('button', { name: 'Continuar' })).toBeDisabled()
  await entrega.getByLabel('Nome do cliente (obrigatório)').fill('Joana Delivery B')
  await entrega.getByRole('button', { name: 'Continuar' }).click()
  await page.getByRole('button', { name: /Frango B/ }).click()
  await expect(painel.getByText('Delivery', { exact: true })).toBeVisible()
  await expect.poll(async () => (await (await page.request.get('/api/pedidos?pesquisa=Joana%20Delivery%20B')).json() as { itens: { nomeEntrega: string }[] })
    .itens[0]?.nomeEntrega).toBe('Joana Delivery B')

  // Pedir conta com a impressora de caixa em modo PDF (sem impressora no Windows): a conta abre no visualizador.
  const caixaPdf = await criar<{ id: number }>(page.request, '/api/configuracoes/impressoras',
    { nome: 'Caixa PDF E2E', funcao: 'Caixa', nomeWindows: null, larguraPapelMm: 80, estacaoPreparacaoId: null, terminalId: null, modo: 'Pdf' })
  try {
    await painel.getByRole('button', { name: 'Pedir conta' }).click()
    const visualizador = page.getByRole('dialog').filter({ hasText: 'Caixa PDF E2E' })
    await expect(visualizador).toBeVisible()
    const url = await visualizador.locator('iframe').getAttribute('src')
    const pdf = await page.request.get(url!)
    expect(pdf.headers()['content-type']).toBe('application/pdf')
    expect(pdf.headers()['x-frame-options']).toBe('SAMEORIGIN')
    await visualizador.getByRole('button', { name: 'Fechar' }).last().click()
  } finally {
    await page.request.post(`/api/configuracoes/impressoras/${caixaPdf.id}/activo`, { data: { activo: false } })
  }
})

test('ecrã de entrada táctil: tocar no nome e escrever o código no teclado do ecrã', async ({ page, browser }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const garcom = await criar<{ id: number }>(page.request, '/api/utilizadores',
    { nomeUtilizador: `garcom.e2e.${Date.now()}`, nomeCompleto: 'Ana Garçom E2E', palavraPasseTemporaria: 'temporaria1', perfis: ['Garçom'] })
  const pin = await page.request.post(`/api/utilizadores/${garcom.id}/pin`, { data: { pin: '2580', ecraTactil: true, retirar: false } })
  expect(pin.status()).toBe(204)

  // Outro posto (sem sessão), aberto pelo atalho com o ecrã táctil.
  const posto = await (await browser.newContext()).newPage()
  await posto.goto('/login?tactil=1')
  await expect(posto.getByRole('heading', { name: 'Quem vai entrar?' })).toBeVisible()
  await expect(posto).toHaveURL(/\/login$/)
  await posto.getByRole('button', { name: /Ana Garçom E2E/ }).click()

  const teclado = posto.getByLabel('Teclado numérico')
  const escrever = async (codigo: string) => { for (const d of codigo) await teclado.getByRole('button', { name: d, exact: true }).click() }
  await escrever('1357')
  await teclado.getByRole('button', { name: 'Entrar' }).click()
  await expect(posto.getByText('Código errado.')).toBeVisible()

  await escrever('2580')
  await teclado.getByRole('button', { name: 'Entrar' }).click()
  // Palavra-passe temporária: entra e é levado a defini-la.
  await expect(posto).toHaveURL(/\/alterar-palavra-passe$/)
})

test('licença: estado nas Configurações e activação da renovação colando o texto', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  await page.goto('/configuracoes?aba=licenca')
  await expect(page.getByText(/^NUIT 400000002 · válida até/)).toBeVisible()
  await expect(page.getByText('Válida', { exact: true })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Em vigor' })).toContainText('Restaurante E2E')

  await page.getByLabel('Texto da licença').fill(credenciais().licencaRenovada)
  await page.getByRole('button', { name: 'Activar licença' }).click()
  await expect(page.getByText('Licença activada.')).toBeVisible()
  await expect(page.getByText('Restaurante E2E Renovado').first()).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'Em vigor' })).toContainText('Restaurante E2E Renovado')
  // O NUIT do estabelecimento passa a vir da licença (só de leitura).
  await page.goto('/configuracoes')
  await expect(page.getByLabel('NUIT')).toHaveJSProperty('readOnly', true)
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

/** Agente de impressão compilado (dotnet build backend\LAMGestRestaurant.Agente): só corre em Windows. */
const AGENTE = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'backend', 'LAMGestRestaurant.Agente', 'bin', 'Debug', 'net9.0-windows', 'LAMGest-Agente.exe')

test('impressoras do posto: o agente do computador mostra as impressoras desse Windows e a impressora fica ligada ao posto', async ({ page }) => {
  test.setTimeout(180_000) // a lista de impressoras do Windows pode demorar (ex.: redireccionadas por RDP)
  test.skip(process.platform !== 'win32' || !existsSync(AGENTE), 'Precisa do agente de impressão compilado (Windows).')
  // Só corre um agente por sessão do Windows: com o agente instalado neste computador aberto, o do teste fecha-se logo.
  test.skip(execFileSync('tasklist', ['/FI', 'IMAGENAME eq LAMGest-Agente.exe'], { encoding: 'utf8' }).includes('LAMGest-Agente.exe'),
    'Já há um agente de impressão a correr nesta sessão do Windows.')
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const numero = 700 + Math.floor(Math.random() * 250)
  const posto = await criar<{ id: number; nome: string }>(page.request, '/api/terminais', { nome: `Balcão ${numero}`, descricao: null, numero })
  const registo = await criar<{ chave: string }>(page.request, `/api/postos/${numero}/agente`, {})

  // O agente real, como o instalador o deixa no posto (aqui com a configuração num ficheiro temporário).
  const pasta = mkdtempSync(join(tmpdir(), 'lamgest-agente-'))
  const config = join(pasta, 'agente.json')
  writeFileSync(config, JSON.stringify({ servidor: `http://localhost:${PORTA_API}`, posto: numero, chave: registo.chave }))
  const agente = spawn(AGENTE, ['--config', config], { stdio: 'ignore' })
  try {
    const nomes = execFileSync('powershell', ['-NoProfile', '-Command', '(Get-Printer).Name'], { encoding: 'utf8' })
      .split(/\r?\n/).map((n) => n.trim()).filter(Boolean)
    test.skip(nomes.length === 0, 'Este Windows não tem impressoras.')

    // O agente envia as impressoras ao arrancar: o posto aparece ligado na escolha «Ligada a».
    await page.goto('/configuracoes?aba=impressoras')
    await expect(async () => {
      await page.reload()
      await page.getByRole('button', { name: 'Nova impressora' }).click()
      await expect(page.getByRole('dialog').getByLabel('Ligada a').locator('option', { hasText: `Posto ${numero} — ${posto.nome}` })).toHaveText(`Posto ${numero} — ${posto.nome}`, { timeout: 15_000 })
    }).toPass({ timeout: 60_000 })
    const form = page.getByRole('dialog')
    await form.getByLabel('Nome').fill(`Talões ${numero}`)
    await form.getByLabel('Outro centro').check() // só recebe o que uma categoria/produto lhe enviar: nada sai em papel
    await form.getByLabel('Ligada a').selectOption({ label: `Posto ${numero} — ${posto.nome}` })
    const windows = form.getByLabel(/Impressora do Windows \(no posto/)
    await expect(windows.locator('option', { hasText: nomes[0] })).toHaveCount(1)
    await windows.selectOption({ label: nomes[0] })
    await form.getByRole('button', { name: 'Guardar' }).click()
    const linha = page.getByRole('row', { name: new RegExp(`Talões ${numero}`) })
    await expect(linha).toContainText(`Posto ${posto.nome}`)
    await expect(linha).toContainText(nomes[0])

    // Caixa → Terminais: estado do agente do posto.
    await page.goto('/caixa')
    await page.getByRole('button', { name: 'Terminais' }).click()
    await expect(page.getByRole('dialog').getByRole('listitem').filter({ hasText: posto.nome })).toContainText('Impressão ligada')

    const impressoras = await (await page.request.get('/api/configuracoes/impressoras')).json() as { id: number; nome: string }[]
    const criada = impressoras.find((i) => i.nome === `Talões ${numero}`)!
    expect((await page.request.post(`/api/configuracoes/impressoras/${criada.id}/activo`, { data: { activo: false } })).status()).toBe(204)
  } finally {
    agente.kill()
  }
})

test('venda ao Balcão com leitor de código de barras e botões do POS nas configurações', async ({ page }) => {
  test.setTimeout(150_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: 'Balcão E2E', descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  await criar(page.request, '/api/produtos', {
    codigo: 'E2E-BAL', codigoBarras: '7891234567890', nome: 'Água 500ml', descricao: null, categoriaId: categoria.id, tipo: 'Bar', precoVenda: 50, custo: 20,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })
  // A sessão de caixa deste operador (um operador só tem uma aberta).
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; sessaoAbertaId: number | null }[]
  let terminal = terminais.find((t) => t.sessaoAbertaId)?.id
  if (!terminal) {
    terminal = (await criar<{ id: number }>(page.request, '/api/terminais', { nome: 'Caixa Balcão', descricao: null })).id
    await abrirSessaoCaixa(page.request, terminal)
  }
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), terminal)

  // Sem venda iniciada, passar o produto no leitor começa uma venda ao balcão; duas leituras seguidas somam-se.
  await page.goto('/pos')
  await expect(page.getByRole('heading', { name: 'Iniciar venda' })).toBeVisible()
  // O leitor escreve todas as teclas de seguida e termina com Enter.
  const passarNoLeitor = (codigo = '7891234567890') => page.evaluate((c) => {
    for (const key of [...c, 'Enter']) document.body.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  }, codigo)
  await expect(page.getByRole('button', { name: 'Balcão', exact: true })).toBeEnabled() // ecrã pronto a gravar
  await page.waitForTimeout(500)
  await passarNoLeitor()
  const pagar = page.getByRole('button', { name: /^Pagar 50,00/ })
  await expect(pagar).toBeVisible({ timeout: 45_000 }) // a 1.ª venda cria o pedido (lento em máquinas com pouca CPU)
  await passarNoLeitor()
  const pagar100 = page.getByRole('button', { name: /^Pagar 100,00/ })
  await expect(pagar100).toBeVisible()
  await expect(page.getByRole('button', { name: /^Enviar/ })).toHaveCount(0) // no balcão os talões saem ao pagar

  await passarNoLeitor('0000000000')
  await expect(page.getByText('Código 0000000000 não encontrado.')).toBeVisible()

  await pagar100.click()
  await page.getByRole('dialog').getByRole('button', { name: /^Confirmar 100,00/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-\d{4}-\d{6}/)).toBeVisible()
  await expect(page.getByText('Balcão (novo)')).toBeVisible() // pronto para a venda seguinte

  // Configurações: desligar o botão Mesa tira-o do POS.
  await page.goto('/configuracoes?aba=documentos-pos')
  await expect(page.getByLabel('Talão de pedido (cozinha/bar)')).toHaveValue('1')
  await page.getByRole('checkbox', { name: 'Mesa', exact: true }).uncheck()
  await page.getByRole('button', { name: /^Guardar/ }).click()
  await expect(page.getByText('Configurações guardadas.')).toBeVisible()
  await page.goto('/pos')
  await expect(page.getByRole('button', { name: 'Balcão', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Mesa', exact: true })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Mesas' })).toHaveCount(0)

  const repor = await page.request.put('/api/configuracoes', { data: { valores: { 'pos.mesa': 'true' } } })
  expect(repor.ok()).toBeTruthy()
})

test('importar produtos do Excel: erros linha a linha, correcção e produto no catálogo', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const modelo = await page.request.get('/modelos/Modelo-Importacao-Produtos.xlsx')
  expect(modelo.ok()).toBeTruthy()

  const titulos = ['Código', 'Código de barras', 'Nome', 'Descrição', 'Categoria', 'Subcategoria', 'Tipo', 'Preço de venda (MT)', 'Custo (MT)', 'Taxa de imposto', 'Vai à cozinha', 'Disponível', 'Controla stock', 'Stock inicial', 'Stock mínimo']
  const ficheiro = (nome: string, tipo: string) => ({
    name: nome,
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from(gerarXlsx(titulos, [['IMP-E2E', null, 'Sumo importado', null, 'Importados E2E', null, tipo, 75.5, 30, 'IVA 16%', 'Não', 'Sim', 'Sim', 12, 2]], 'Produtos')),
  })

  await page.goto('/produtos')
  await page.getByRole('button', { name: 'Importar Excel' }).click()
  const dialogo = page.getByRole('dialog')
  await expect(dialogo.getByRole('link', { name: 'Descarregar modelo' })).toHaveAttribute('href', '/modelos/Modelo-Importacao-Produtos.xlsx')

  await dialogo.getByLabel('Ficheiro Excel dos produtos').setInputFiles(ficheiro('com-erro.xlsx', 'Cozinha'))
  await expect(dialogo.getByText('1 de 1 linha(s) com erros')).toBeVisible({ timeout: 60_000 })
  await expect(dialogo.getByText('O tipo tem de ser Restaurante, Bar ou Outro.')).toBeVisible()
  await expect(dialogo.getByRole('button', { name: /^Importar/ })).toBeDisabled()

  await dialogo.getByLabel('Ficheiro Excel dos produtos').setInputFiles(ficheiro('certo.xlsx', 'Bar'))
  await expect(dialogo.getByText('1 produto(s) prontos a importar')).toBeVisible({ timeout: 60_000 })
  await expect(dialogo.getByText('Categorias novas:')).toBeVisible()
  await dialogo.getByRole('button', { name: 'Importar 1 produto(s)' }).click()
  await expect(page.getByText('1 produto(s) importado(s).')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('row', { name: /Sumo importado/ })).toContainText('75,50')

  // Produto sem código de barras: o botão Gerar preenche um EAN-13 interno e fica gravado ao guardar.
  await page.getByRole('button', { name: 'Editar Sumo importado' }).click()
  const formulario = page.getByRole('dialog')
  await expect(formulario.getByLabel('Código de barras (opcional)')).toHaveValue('')
  await formulario.getByRole('button', { name: 'Gerar', exact: true }).click()
  await expect(formulario.getByLabel('Código de barras (opcional)')).toHaveValue(/^20\d{11}$/)
  await expect(formulario.getByRole('button', { name: 'Gerar', exact: true })).toBeDisabled()
  const gerado = await formulario.getByLabel('Código de barras (opcional)').inputValue()
  await formulario.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('row', { name: /Sumo importado/ })).toContainText(gerado)
})

test('produto que pede a quantidade no POS e separadores de armazéns e unidades', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: 'Peso E2E', descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const unidades = await (await page.request.get('/api/stock/unidades')).json() as { id: number; codigo: string }[]
  await criar(page.request, '/api/produtos', {
    codigo: 'E2E-KG', codigoBarras: null, nome: 'Camarão a peso', descricao: null, categoriaId: categoria.id, tipo: 'Restaurante', precoVenda: 100, custo: 40,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null, unidadeMedidaId: unidades.find((u) => u.codigo === 'kg')!.id, armazemId: null, pedeQuantidade: true,
  })

  await page.goto('/pos')
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: /Camarão a peso/ }).click()
  const dialogo = page.getByRole('dialog')
  await expect(dialogo.getByLabel('Quantidade (kg)')).toBeVisible()
  for (const tecla of ['1', ',', '5']) await dialogo.getByRole('button', { name: tecla, exact: true }).click()
  await dialogo.getByRole('button', { name: 'Lançar' }).click()
  await expect(page.getByRole('button', { name: /^Pagar 150,00/ })).toBeVisible()
  await expect(page.getByLabel('Pedido actual').getByText('100,00 MT / kg')).toBeVisible()

  await page.goto('/stock')
  await page.getByRole('tab', { name: 'Armazéns' }).click()
  await expect(page.getByRole('heading', { name: 'Armazém de abate por centro de produção' })).toBeVisible()
  await expect(page.getByText('Principal', { exact: true })).toBeVisible()
  await page.getByRole('tab', { name: 'Unidades' }).click()
  await expect(page.getByRole('row', { name: /Quilograma/ })).toContainText('1 kg = 1 kg')
})

test('Cartão Evento: registar com saldo, pagar no POS passando o cartão e consultar o saldo', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: 'Cartão E2E', descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  await criar(page.request, '/api/produtos', {
    codigo: 'E2E-CRT', codigoBarras: null, nome: 'Sumo evento', descricao: null, categoriaId: categoria.id, tipo: 'Bar', precoVenda: 120, custo: 30,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; sessaoAbertaId: number | null }[]
  let terminal = terminais.find((t) => t.sessaoAbertaId)?.id
  if (!terminal) {
    terminal = (await criar<{ id: number }>(page.request, '/api/terminais', { nome: 'Caixa Cartões', descricao: null })).id
    await abrirSessaoCaixa(page.request, terminal)
  }
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), terminal)

  // Registar no menu Cartões Evento com 500 MT em dinheiro (número gerado).
  await page.goto('/cartoes')
  await page.getByRole('button', { name: 'Registar cartão' }).click()
  const registo = page.getByRole('dialog')
  await registo.getByLabel('Valor inicial (MT)').fill('500')
  await registo.getByRole('button', { name: 'Dinheiro' }).click()
  await registo.getByRole('button', { name: 'Registar' }).click()
  const aviso = page.getByText(/Cartão \d{12} registado com 500,00/)
  await expect(aviso).toBeVisible()
  const numero = (await aviso.textContent())!.match(/\d{12}/)![0]

  // Pagar no POS: Balcão, produto, Pagar, Cartão Evento, passar o cartão (pista magnética) e confirmar.
  await page.goto('/pos')
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: /Sumo evento/ }).click()
  await page.getByRole('button', { name: /^Pagar 120,00/ }).click()
  const pagamento = page.getByRole('dialog')
  await pagamento.getByRole('button', { name: 'Cartão Evento' }).click()
  await pagamento.getByLabel('Cartão Evento').fill(';' + numero + '=2512?')
  await pagamento.getByLabel('Cartão Evento').press('Enter')
  await expect(pagamento.getByText('saldo')).toContainText('500,00')
  await pagamento.getByRole('button', { name: /^Confirmar 120,00/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-\d{4}-\d{6}/)).toBeVisible()

  // Consultar o saldo no menu: 380 MT e o extrato com o pagamento.
  await page.goto('/cartoes')
  await page.getByLabel('Consultar saldo').fill(numero)
  await page.getByLabel('Consultar saldo').press('Enter')
  const cartao = page.getByRole('region', { name: 'Cartão ' + numero })
  await expect(cartao).toContainText('380,00')
  await expect(cartao.getByRole('row', { name: /Pagamento/ })).toContainText('-120,00')

  // Etiqueta: escolher QR code ou código de barras (só a pré-visualização; não se imprime).
  await cartao.getByRole('button', { name: 'Imprimir etiqueta' }).click()
  const etiqueta = page.getByRole('dialog', { name: `Etiqueta do cartão ${numero}` })
  await expect(etiqueta.getByRole('img')).toHaveAttribute('src', /codigo=CodigoBarras/)
  await etiqueta.getByLabel('QR code').check()
  await expect(etiqueta.getByRole('img')).toHaveAttribute('src', /codigo=QrCode/)
  await etiqueta.getByRole('button', { name: 'Cancelar' }).click()

  // Extrato por caixa (registo e pagamento no mesmo caixa) e por datas (saldo no início e no fim).
  await cartao.getByRole('radio', { name: 'Por caixa' }).click()
  await expect(cartao.getByLabel('Caixa')).toBeVisible()
  await expect(cartao).toContainText('2 movimentos')
  await expect(cartao.getByRole('row', { name: /Pagamento/ })).toContainText('-120,00')
  await cartao.getByRole('radio', { name: 'Por datas' }).click()
  await expect(cartao).toContainText(/saldo no início 0,00.*no fim 380,00/)

  // Descarga: a lista geral mostra o saldo e o acumulado; descarrega-se este cartão com devolução em dinheiro.
  page.on('dialog', (d) => void d.accept())
  await page.getByRole('button', { name: 'Descarregar todos os saldos' }).click()
  const todos = page.getByRole('dialog', { name: 'Descarregar todos os cartões' })
  await expect(todos.getByRole('row', { name: new RegExp(numero) })).toContainText('380,00')
  await todos.getByRole('button', { name: 'Cancelar' }).click()
  await cartao.getByRole('button', { name: 'Descarregar saldo' }).click()
  const descarga = page.getByRole('dialog', { name: `Descarregar o cartão ${numero}` })
  await expect(descarga.getByRole('row', { name: new RegExp(numero) })).toContainText('380,00')
  await expect(descarga.getByText('Total (1 cartão)')).toBeVisible()
  await descarga.getByLabel('Motivo').fill('Fim do evento')
  await descarga.getByRole('button', { name: /^Descarregar 380,00/ }).click()
  const concluida = page.getByRole('dialog', { name: 'Descarga concluída' })
  await expect(concluida).toContainText('Dinheiro devolvido por')
  await concluida.getByRole('button', { name: 'Fechar' }).last().click()
  await expect(cartao.locator('p.text-3xl')).toContainText('0,00')
  await expect(cartao.getByRole('button', { name: 'Descarregar saldo' })).toHaveCount(0)

  // Relatório das descargas.
  await page.goto('/relatorios?tipo=cartoes')
  const relatorio = page.getByRole('heading', { name: 'Descargas de saldo' }).locator('../..')
  await expect(relatorio.getByRole('row', { name: new RegExp(numero) })).toContainText('Devolvido')
})

test('POS: Fatura a Crédito com cliente novo e Pagamento de Fatura parcial com recibo', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: 'Crédito E2E', descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  await criar(page.request, '/api/produtos', {
    codigo: 'E2E-CRD', codigoBarras: null, nome: 'Jantar crédito', descricao: null, categoriaId: categoria.id, tipo: 'Bar', precoVenda: 1000, custo: 100,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; sessaoAbertaId: number | null }[]
  let terminal = terminais.find((t) => t.sessaoAbertaId)?.id
  if (!terminal) {
    terminal = (await criar<{ id: number }>(page.request, '/api/terminais', { nome: 'Caixa Crédito', descricao: null })).id
    await abrirSessaoCaixa(page.request, terminal)
  }
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), terminal)
  const nome = `Cliente Fiado ${Date.now() % 100000}`

  // Fatura a Crédito: o cliente não existe, cria-se ali mesmo.
  await page.goto('/pos')
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: /Jantar crédito/ }).click()
  await page.getByRole('button', { name: /^Pagar 1.?000,00/ }).click()
  const pagamento = page.getByRole('dialog', { name: /^Pagamento/ })
  await pagamento.getByRole('button', { name: 'Fatura a Crédito' }).click()
  await expect(pagamento.getByRole('button', { name: /^Confirmar/ })).toBeDisabled()
  await pagamento.getByLabel('Cliente da factura').fill(nome)
  await expect(pagamento.getByText('Cliente não encontrado.')).toBeVisible()
  await pagamento.getByRole('button', { name: 'Novo cliente' }).click()
  const ficha = page.getByRole('dialog', { name: 'Novo cliente' })
  await expect(ficha.locator('input').first()).toHaveValue(nome)
  await ficha.getByRole('button', { name: 'Guardar' }).click()
  await expect(pagamento.getByText(nome)).toBeVisible()
  await pagamento.getByRole('button', { name: /^Confirmar 1.?000,00/ }).click()
  const aviso = page.getByText(/pago\. Fatura a crédito FT-\d{4}-\d{6}/)
  await expect(aviso).toBeVisible()
  const ftPos = (await aviso.textContent())!.match(/FT-\d{4}-\d{6}/)![0]

  // Uma segunda factura do mesmo cliente (emitida em Facturação), para escolher qual se paga.
  const clientes = await (await page.request.get(`/api/clientes?pesquisa=${encodeURIComponent(nome)}`)).json() as { itens: { id: number }[] }
  const clienteId = clientes.itens[0].id
  const outra = await page.request.post('/api/documentos/facturas', {
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    data: { clienteId, clienteNome: null, clienteNuit: null, clienteEndereco: null, data: null, observacoes: null,
      linhas: [{ produtoId: null, descricao: 'Serviço extra', quantidade: 1, precoUnitario: 500, desconto: null, taxaImpostoId: null }] },
  })
  expect(outra.ok()).toBeTruthy()

  // Pagamento de Fatura: escolhe a factura, pagamento parcial com o valor ao lado do que está em dívida, forma de pagamento e finaliza.
  await page.getByRole('button', { name: 'Pagamento de Fatura' }).click()
  const recibo = page.getByRole('dialog', { name: 'Pagamento de Fatura' })
  await recibo.getByLabel('Cliente').fill(nome)
  await recibo.getByRole('button', { name: new RegExp(nome) }).click()
  await expect(recibo.getByRole('radio', { name: /FT-/ })).toHaveCount(2)
  await expect(recibo.getByRole('button', { name: /^(Confirmar|Finalizar)/ })).toBeDisabled() // ainda sem factura escolhida
  await recibo.getByRole('radio', { name: new RegExp(ftPos) }).check()
  await recibo.getByRole('radio', { name: 'Pagamento parcial' }).click()
  await expect(recibo.getByText('Valor em dívida').locator('..')).toContainText(/1.?000,00/)
  await recibo.getByLabel('Valor pago (MT)').fill('400')
  await expect(recibo.getByText(/Fica em dívida:/)).toContainText('600,00')
  await recibo.getByRole('button', { name: 'Dinheiro', exact: true }).click()
  await recibo.getByRole('button', { name: /^Finalizar 400,00/ }).click()
  await expect(page.getByText('Recibo emitido.')).toBeVisible()
  const conta = await (await page.request.get(`/api/documentos/conta-corrente/${clienteId}`)).json() as { totalEmDivida: number; facturas: { numero: string; emDivida: number }[] }
  expect(conta.facturas.find((f) => f.numero === ftPos)?.emDivida).toBe(600)
  expect(conta.totalEmDivida).toBe(1100)

  // Pagamento completo da outra factura.
  await page.getByRole('button', { name: 'Pagamento de Fatura' }).click()
  await recibo.getByLabel('Cliente').fill(nome)
  await recibo.getByRole('button', { name: new RegExp(nome) }).click()
  const outraNumero = conta.facturas.find((f) => f.numero !== ftPos)!.numero
  await recibo.getByRole('radio', { name: new RegExp(outraNumero) }).check()
  await recibo.getByRole('button', { name: 'Dinheiro', exact: true }).click()
  await recibo.getByRole('button', { name: /^Confirmar 500,00/ }).click()
  await expect(page.getByText('Recibo emitido.').first()).toBeVisible()
  await expect.poll(async () => ((await (await page.request.get(`/api/documentos/conta-corrente/${clienteId}`)).json()) as { totalEmDivida: number }).totalEmDivida).toBe(600)
})

test('app do PDA: menus conforme as permissões, venda em ecrã pequeno e recibo na impressora do PDA', async ({ page }) => {
  test.setTimeout(180_000)
  await page.setViewportSize({ width: 360, height: 720 })
  await entrar(page)
  await expect(page.getByRole('button', { name: 'Abrir menu' })).toBeVisible({ timeout: 60_000 })
  await criar(page.request, '/api/produtos', {
    codigo: 'E2E-PDA', codigoBarras: null, nome: 'Refresco PDA', descricao: null,
    categoriaId: (await criar<{ id: number }>(page.request, '/api/categorias', { nome: 'PDA E2E', descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })).id,
    tipo: 'Bar', precoVenda: 75, custo: 20, taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; sessaoAbertaId: number | null }[]
  let terminal = terminais.find((t) => t.sessaoAbertaId)?.id
  if (!terminal) {
    terminal = (await criar<{ id: number }>(page.request, '/api/terminais', { nome: 'Caixa PDA', descricao: null })).id
    await abrirSessaoCaixa(page.request, terminal)
  }
  // A ponte que a app Android acrescenta, com a impressora simulada.
  await page.addInitScript((id) => {
    const estado = { destino: 'pda', impressos: [] as string[] }
    Object.assign(window, { impressosPda: estado.impressos, LamgestPDA: {
      terminalId: () => id, posto: () => 'Balcão E2E', destino: () => estado.destino, definirDestino: (d: string) => { estado.destino = d },
      temImpressora: () => true, colunas: () => 32, imprimir: (j: string) => { estado.impressos.push(j); return '' }, configurar: () => undefined,
      // Câmara simulada: devolve o código guardado em window.codigoCamara.
      lerCodigo: () => setTimeout(() => window.dispatchEvent(new CustomEvent('lamgest-codigo', { detail: (window as unknown as { codigoCamara: string }).codigoCamara })), 50),
    } })
  }, terminal)

  // App completa: os menus são os das permissões de quem entra (o administrador vê tudo, incluindo a administração).
  await page.goto('/relatorios')
  await expect(page).toHaveURL(/\/relatorios$/, { timeout: 60_000 })
  await expect(page.getByRole('heading', { name: /Relatório/ }).first()).toBeVisible({ timeout: 60_000 })
  await page.getByRole('button', { name: 'Abrir menu' }).click()
  const menu = page.getByRole('navigation', { name: 'Menu principal' })
  for (const item of ['POS', 'Caixa', 'Produtos', 'Stock', 'Relatórios', 'Configurações'])
    await expect(menu.getByRole('link', { name: item, exact: true })).toBeVisible()
  await expect(page.getByText('Balcão E2E')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Este PDA' })).toHaveAttribute('aria-pressed', 'true')
  await menu.getByRole('link', { name: 'POS' }).click()

  // Venda ao balcão: o recibo sai no PDA, em 32 colunas.
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: /Refresco PDA/ }).click()
  await expect(page.getByRole('tab', { name: /Pedido · 75,00/ })).toBeVisible()
  await page.screenshot({ path: 'test-results/pda-venda.png' })
  await page.getByRole('tab', { name: /Pedido · 75,00/ }).click()
  const pagar = page.getByRole('button', { name: /^Pagar 75,00/ })
  await pagar.scrollIntoViewIfNeeded()
  await pagar.click()
  const pagamento = page.getByRole('dialog')
  await pagamento.getByRole('button', { name: 'Dinheiro' }).click()
  await pagamento.getByRole('button', { name: /^Confirmar 75,00/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-\d{4}-\d{6}/)).toBeVisible()
  await expect.poll(() => page.evaluate(() => (window as unknown as { impressosPda: string[] }).impressosPda.length)).toBeGreaterThan(0)
  const linhas = await page.evaluate(() => JSON.parse((window as unknown as { impressosPda: string[] }).impressosPda[0]) as { t: string }[])
  expect(linhas.some((l) => l.t.includes('VENDA A DINHEIRO'))).toBeTruthy()
  expect(linhas.some((l) => l.t.includes('Refresco PDA'))).toBeTruthy()
  expect(Math.max(...linhas.map((l) => l.t.length))).toBeLessThanOrEqual(32)

  // Pagamento com Cartão Evento: tocar no campo do cartão abre a câmara e o código lido traz o saldo.
  const cartao = await page.request.post('/api/cartoes', {
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    data: { numero: null, titular: 'PDA', valorInicial: 200, metodoPagamentoId: ((await (await page.request.get('/api/caixa/metodos-pagamento')).json()) as { id: number; codigo: string }[]).find((m) => m.codigo === 'DINHEIRO')!.id },
  })
  expect(cartao.ok(), await cartao.text()).toBeTruthy()
  const numeroCartao = ((await cartao.json()) as { numero: string }).numero
  await page.evaluate((n) => Object.assign(window, { codigoCamara: n }), numeroCartao)
  await page.getByRole('tab', { name: 'Produtos' }).click()
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: /Refresco PDA/ }).click()
  await page.getByRole('tab', { name: /Pedido · 75,00/ }).click()
  await page.getByRole('button', { name: /^Pagar 75,00/ }).click()
  const comCartao = page.getByRole('dialog')
  await comCartao.getByRole('button', { name: 'Cartão Evento' }).click()
  await expect(comCartao.getByLabel('Cartão Evento')).toHaveAttribute('placeholder', 'Toque para ler com a câmara')
  await comCartao.getByLabel('Cartão Evento').click()
  await expect(comCartao.getByText(numeroCartao)).toBeVisible()
  await expect(comCartao.getByText(/saldo/)).toContainText('200,00')
  await comCartao.getByRole('button', { name: /^Confirmar 75,00/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-/).last()).toBeVisible()
})

test('configurações → postos: posto online, caixa e ligação dos tablets com código e QR code', async ({ page }) => {
  test.setTimeout(180_000)
  page.on('dialog', (d) => void d.accept())
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const nome = `Posto Tablets ${Date.now() % 100000}`
  const posto = await criar<{ id: number }>(page.request, '/api/terminais', { nome, descricao: 'Sala principal', numero: null })
  // Este navegador fica associado ao posto: o sinal de presença marca-o online.
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), posto.id)
  await page.goto('/configuracoes?aba=postos')
  const cartao = page.getByRole('listitem', { name: `Posto ${nome}` })
  await expect(cartao).toContainText('Este computador')
  await expect(cartao.getByText('Online', { exact: true })).toBeVisible({ timeout: 60_000 })
  await expect(cartao).toContainText('Sala principal')

  // Ligar os tablets: código de emparelhamento (só agora), endereço do servidor e QR code.
  await cartao.getByRole('button', { name: 'Ligar tablets a este posto' }).click()
  const tablets = page.getByRole('dialog', { name: `Tablets ligados a ${nome}` })
  await expect(tablets.getByLabel('Código de emparelhamento')).toHaveText(/^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$/)
  await expect(tablets.getByRole('img', { name: 'QR code de emparelhamento dos tablets' })).toBeVisible()
  await tablets.getByRole('button', { name: 'Fechar' }).last().click()
  await expect(cartao.getByText('Tablets', { exact: true })).toBeVisible()
  await cartao.getByRole('button', { name: 'Retirar tablets' }).click()
  await expect(cartao.getByRole('button', { name: 'Ligar tablets a este posto' })).toBeVisible()

  // Ecrã do posto (monitores tácteis de 15" a 1024x768): modo táctil e tamanho aplicam-se logo neste computador e ficam para a próxima abertura.
  await expect(cartao).toContainText('normal · tamanho 100%')
  await cartao.getByRole('button', { name: 'Ecrã', exact: true }).click()
  const dialogoEcra = page.getByRole('dialog', { name: `Ecrã do posto ${nome}` })
  await dialogoEcra.getByRole('checkbox', { name: /Modo ecrã táctil/ }).check()
  await dialogoEcra.getByLabel('Tamanho do sistema').selectOption('110')
  await dialogoEcra.getByRole('button', { name: 'Guardar' }).click()
  await expect(cartao).toContainText('modo táctil · tamanho 110%')
  await expect(page.locator('html[data-tactil]')).toHaveCount(1)
  expect(await page.evaluate(() => document.documentElement.style.fontSize)).toBe('110%')
  await page.reload()
  await expect(page.locator('html[data-tactil]')).toHaveCount(1)
  // Repor: o sinal de presença seguinte devolve o ecrã normal.
  const repor = await page.request.put(`/api/configuracoes/postos/${posto.id}/ecra`, { data: { tactil: false, escala: 100 } })
  expect(repor.ok()).toBeTruthy()
  await page.reload()
  await expect(page.locator('html[data-tactil]')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.style.fontSize)).toBe('')
  await page.evaluate(() => localStorage.removeItem('lamgest.terminalId'))
})

test('conta corrente do cliente: extrato, histórico de facturas, facturas a crédito e papel 80 mm / A4', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const nome = `Empresa Conta E2E ${Date.now()}`
  const cliente = await criar<{ id: number }>(page.request, '/api/clientes',
    { tipo: 'Empresa', nome, telefone: null, email: null, endereco: 'Maputo', nuit: '400123123', observacoes: null, cartaoFidelidade: null, rowVersion: null })
  const emitir = async <T,>(url: string, dados: unknown) => {
    const r = await page.request.post(url, { data: dados, headers: { 'Idempotency-Key': crypto.randomUUID() } })
    expect(r.ok(), `${url}: ${r.status()} ${await r.text()}`).toBeTruthy()
    return r.json() as Promise<T>
  }
  const linha = (valor: number) => [{ produtoId: null, descricao: 'Catering E2E', quantidade: 1, precoUnitario: valor, desconto: null, taxaImpostoId: null }]
  const ft1 = await emitir<{ id: number; numero: string }>('/api/documentos/facturas',
    { clienteId: cliente.id, clienteNome: null, clienteNuit: null, clienteEndereco: null, data: null, observacoes: null, linhas: linha(1000) })
  const ft2 = await emitir<{ id: number; numero: string }>('/api/documentos/facturas',
    { clienteId: cliente.id, clienteNome: null, clienteNuit: null, clienteEndereco: null, data: null, observacoes: null, linhas: linha(500) })
  const metodos = await (await page.request.get('/api/caixa/metodos-pagamento')).json() as { id: number; codigo: string; contaNaGaveta: boolean }[]
  const semGaveta = metodos.find((m) => !m.contaNaGaveta && m.codigo !== 'CARTAO_EVENTO')!
  const recibo = await emitir<{ numero: string }>('/api/documentos/recibos', {
    clienteId: cliente.id, metodoPagamentoId: semGaveta.id, referencia: 'E2E-RC-1', sessaoCaixaId: null, observacoes: null, liquidacoes: [{ facturaId: ft1.id, valor: 400 }],
  })

  // Ficha do cliente: resumo da conta corrente e atalho para o extrato.
  await page.goto('/clientes')
  await page.getByPlaceholder('Nome, telefone, NUIT ou cartão…').fill(nome)
  await page.getByText(nome, { exact: true }).click()
  const conta = page.getByRole('region', { name: 'Conta corrente' })
  await expect(conta).toContainText(/1\s?100,00/)
  await expect(conta).toContainText('2 factura(s)')
  await conta.getByRole('link', { name: 'Extrato' }).click()

  // Extrato: 1 500 de facturas − 400 do recibo = 1 100, com saldo linha a linha (última coluna).
  await expect(page.getByRole('heading', { name: 'Relatório — Contas correntes' })).toBeVisible()
  await expect(page.getByRole('radio', { name: 'Extrato de conta corrente' })).toHaveAttribute('aria-checked', 'true')
  const linhaDe = (numero: string) => page.getByRole('row').filter({ has: page.getByRole('cell', { name: numero, exact: true }) })
  await expect(linhaDe(ft1.numero).getByRole('cell').last()).toHaveText(/1\s?000,00/)
  await expect(linhaDe(ft2.numero).getByRole('cell').last()).toHaveText(/1\s?500,00/)
  await expect(linhaDe(recibo.numero).getByRole('cell').last()).toHaveText(/1\s?100,00/)

  // Histórico de facturas do cliente e facturas a crédito (situação de cada uma).
  await page.getByRole('radio', { name: 'Histórico de facturas do cliente' }).click()
  await expect(page.getByRole('row', { name: new RegExp(recibo.numero) })).toBeVisible()
  await page.getByRole('radio', { name: 'Facturas a crédito e dívidas' }).click()
  await expect(linhaDe(ft1.numero)).toContainText('Parcial')
  await expect(linhaDe(ft2.numero)).toContainText('Em dívida')

  // Imprimir: documento da vista aberta, com o cliente, e escolha do papel (80 mm, 58 mm ou A4).
  await page.getByRole('button', { name: 'Imprimir documento' }).click()
  const imprimir = page.getByRole('dialog', { name: 'Imprimir' })
  await expect(imprimir.getByLabel('Documento')).toHaveValue('FacturasCredito')
  await expect(imprimir).toContainText(`Cliente: ${nome}`)
  await imprimir.getByLabel('Documento').selectOption('ExtratoCliente')
  await expect(imprimir.getByText('Talão 80 mm')).toBeVisible()
  await imprimir.getByText('Folha A4').click()
  await imprimir.getByRole('button', { name: 'Cancelar' }).click()
  // O documento A4 do extrato tem os mesmos números.
  const hoje = new Date().toLocaleDateString('sv-SE', { timeZone: 'Africa/Maputo' })
  const documento = await (await page.request.get(`/api/relatorios/documento?tipo=ExtratoCliente&desde=${hoje}&ate=${hoje}&clienteId=${cliente.id}`)).json() as { linhas: { texto: string; direita: string | null }[] }
  expect(documento.linhas.find((l) => l.texto.startsWith('SALDO EM'))?.direita?.replace(/\s/g, '')).toContain('1100,00')
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


test('menu Auditoria: quem fez o quê, com filtros e detalhes', async ({ page }) => {
  test.setTimeout(120_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const nome = `Auditoria E2E ${Date.now() % 100000}`
  await criar(page.request, '/api/clientes', { tipo: 'Particular', nome, telefone: null, email: null, endereco: null, nuit: null, observacoes: null, cartaoFidelidade: null, rowVersion: null })

  await page.getByRole('link', { name: 'Auditoria' }).click()
  await expect(page.getByRole('heading', { name: 'Auditoria' })).toBeVisible()
  await expect(page.getByRole('row', { name: /Entrou no sistema/ }).first()).toBeVisible()
  await page.getByLabel('Módulo').selectOption({ label: 'Clientes' })
  await page.getByLabel('Pesquisar').fill(nome)
  const linha = page.getByRole('row', { name: /Criou um cliente/ })
  await expect(linha).toHaveCount(1)
  await linha.click()
  await expect(page.getByText(nome).last()).toBeVisible() // detalhes do registo

  // Atalho «Dinheiro no caixa»: junta aberturas e fechos de caixa, entradas, saídas e gaveta (de módulos diferentes).
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; sessaoAbertaId: number | null }[]
  if (!terminais.some((t) => t.sessaoAbertaId)) {
    const terminal = await criar<{ id: number }>(page.request, '/api/terminais', { nome: `Caixa Auditoria ${Date.now() % 100000}`, descricao: null })
    await abrirSessaoCaixa(page.request, terminal.id)
  }
  await page.getByLabel('Pesquisar').fill('')
  await page.getByRole('radio', { name: 'Dinheiro no caixa' }).click()
  await expect(page.getByLabel('Módulo')).toBeDisabled()
  await expect(page.getByRole('row', { name: /Abriu o caixa/ }).first()).toBeVisible()
  await expect(page.getByRole('row', { name: /Criou um cliente/ })).toHaveCount(0)
  await page.getByRole('radio', { name: 'Tudo' }).click()
  await expect(page.getByLabel('Módulo')).toBeEnabled()
})
test('ecrã táctil: teclado no ecrã na pesquisa e comentários dos produtos e do pedido', async ({ page }) => {
  test.setTimeout(150_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const { mesa } = await prepararRestaurante(page.request, 'K', false)
  // Posto sem teclado físico: o teclado no ecrã fica sempre activo.
  await page.getByRole('combobox').filter({ hasText: 'Teclado: Automático' }).selectOption('sempre')

  await page.goto('/pos')
  await page.getByRole('button', { name: 'Mesa', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: new RegExp(`^${mesa.nome}`) }).click()
  const teclado = page.getByRole('group', { name: 'Teclado no ecrã' })
  await page.getByRole('searchbox', { name: 'Pesquisar produto' }).click()
  await expect(teclado).toBeVisible()
  for (const letra of 'frango') await teclado.getByRole('button', { name: letra, exact: true }).click()
  await expect(page.getByRole('searchbox', { name: 'Pesquisar produto' })).toHaveValue('frango')
  await teclado.getByRole('button', { name: 'OK' }).click()
  await expect(teclado).toBeHidden()
  await page.getByRole('button', { name: /Frango K/ }).click()

  // Comentário do produto: sugestão + texto livre escrito no teclado do ecrã.
  const painel = page.getByLabel('Pedido actual')
  await painel.getByRole('button', { name: 'Comentário de Frango K' }).click()
  const dialogo = page.getByRole('dialog', { name: 'Comentário: Frango K' })
  await dialogo.getByRole('button', { name: 'Sem cebola' }).click()
  await dialogo.getByLabel('Comentário livre').click()
  await expect(teclado).toBeVisible()
  await teclado.getByRole('button', { name: '?123' }).click()
  await teclado.getByRole('button', { name: ',', exact: true }).click()
  await teclado.getByRole('button', { name: 'abc' }).click()
  for (const letra of ' sem batata') await teclado.getByRole('button', { name: letra === ' ' ? 'Espaço' : letra, exact: true }).click()
  await expect(dialogo.getByLabel('Comentário livre')).toHaveValue('Sem cebola, sem batata')
  await dialogo.getByRole('button', { name: 'Guardar' }).click()
  await expect(painel.getByTestId('comentario-item')).toHaveText('— Sem cebola, sem batata')

  // Comentário do pedido inteiro.
  await painel.getByRole('button', { name: 'Mais opções do pedido' }).click()
  await painel.getByRole('menuitem', { name: 'Comentário do pedido' }).click()
  const doPedido = page.getByRole('dialog', { name: /^Comentário: pedido/ })
  await doPedido.getByLabel('Comentário livre').fill('Cliente com pressa')
  await doPedido.getByRole('button', { name: 'Guardar' }).click()
  await expect(painel.getByTestId('comentario-pedido')).toHaveText('Cliente com pressa')

  // Depois de enviar, o comentário ainda se muda (sai um talão de alteração).
  await painel.getByRole('button', { name: /^Enviar/ }).click()
  await expect(painel.getByText('Pendente')).toBeVisible()
  await painel.getByRole('button', { name: 'Comentário de Frango K' }).click()
  await expect(page.getByText(/sai um talão de ALTERAÇÃO/)).toBeVisible()
  await page.getByRole('dialog', { name: 'Comentário: Frango K' }).getByRole('button', { name: 'Cancelar' }).click()
  await page.getByRole('combobox').filter({ hasText: 'Teclado: Sempre' }).selectOption('auto')
})

test('inventário: folha de contagem para imprimir, cega por omissão, com a ordem escolhida', async ({ page }) => {
  test.setTimeout(120_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const nome = `Arm Contagem ${Date.now() % 100000}`
  await criar(page.request, '/api/stock/armazens', { nome })
  const unidades = await (await page.request.get('/api/stock/unidades')).json() as { id: number; codigo: string }[]
  await criar(page.request, '/api/stock/artigos', {
    codigo: `CONT-${Date.now() % 100000}`, nome: 'Arroz (contagem)', tipo: 'Ingrediente', unidadeMedidaId: unidades.find((u) => u.codigo === 'kg')!.id,
    stockMinimo: 0, permiteStockNegativo: false, localizacao: 'Prateleira E2E',
  })

  await page.getByRole('link', { name: 'Stock' }).click()
  await page.getByRole('tab', { name: 'Inventários' }).click()
  await page.getByRole('button', { name: 'Novo inventário' }).click()
  await page.getByLabel('Armazém').selectOption({ label: nome })
  await page.getByRole('button', { name: 'Abrir inventário' }).click()
  const inventario = page.getByRole('dialog', { name: new RegExp(`^Inventário n\\.º \\d+ · ${nome}`) })
  await expect(inventario).toBeVisible({ timeout: 60_000 })
  const id = Number((await inventario.getAttribute('aria-label'))!.match(/n\.º (\d+)/)![1])

  // No ecrã, a mesma ordem da folha: por localização, com o título do grupo.
  await inventario.getByLabel('Ordem dos artigos').selectOption('Localizacao')
  await expect(inventario.getByRole('row', { name: 'Prateleira E2E', exact: true })).toBeVisible()

  await inventario.getByRole('button', { name: 'Imprimir folha de contagem' }).click()
  const imprimir = page.getByRole('dialog', { name: 'Imprimir' })
  await expect(imprimir.getByText('Folha de contagem (inventário)')).toBeVisible()
  await expect(imprimir.getByLabel(/Mostrar o saldo do sistema/)).not.toBeChecked()
  await imprimir.getByLabel('Ordem dos artigos').selectOption('Localizacao')
  await imprimir.getByText('Folha A4', { exact: true }).click()
  await imprimir.getByLabel(/Uma página por grupo/).check()
  const pedido = page.waitForRequest((r) => r.url().includes('/api/relatorios/documento?'))
  await imprimir.getByRole('button', { name: 'Imprimir' }).click()
  expect((await pedido).url()).toMatch(/ordem=Localizacao.*paginaPorGrupo=true/)
  await expect(imprimir).toBeHidden()

  const folha = await (await page.request.get(`/api/relatorios/documento?tipo=FolhaContagem&inventarioId=${id}`)).json() as { linhas: { texto: string; celulas?: string[] | null }[] }
  expect(folha.linhas.map((l) => l.texto)).toEqual(expect.arrayContaining(['FOLHA DE CONTAGEM']))
  expect(folha.linhas.some((l) => l.celulas?.join('|') === 'Código|Artigo|Un.|Contado')).toBeTruthy()
})

test('visor do cliente: segundo monitor configurado no posto mostra a venda, o troco e o agradecimento', async ({ page, context }) => {
  test.setTimeout(180_000)
  page.on('dialog', (d) => void d.accept())
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const sufixo = String(Date.now() % 100000)
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: `Visor ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const codigoBarras = `789${sufixo.padStart(10, '0')}`
  await criar(page.request, '/api/produtos', {
    codigo: `E2E-VIS${sufixo}`, codigoBarras, nome: `Sumo Visor ${sufixo}`, descricao: null, categoriaId: categoria.id, tipo: 'Bar', precoVenda: 50, custo: 20,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })
  // O posto deste computador (o do caixa aberto do operador).
  const terminais = await (await page.request.get('/api/terminais')).json() as { id: number; nome: string; sessaoAbertaId: number | null }[]
  let posto = terminais.find((t) => t.sessaoAbertaId)
  if (!posto) {
    posto = { ...(await criar<{ id: number; nome: string }>(page.request, '/api/terminais', { nome: `Caixa Visor ${sufixo}`, descricao: null })), sessaoAbertaId: null }
    await abrirSessaoCaixa(page.request, posto.id)
  }
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), posto.id)

  // Configurações → Postos → Visor do cliente: segundo monitor, guardar e testar.
  await page.goto('/configuracoes?aba=postos')
  const cartao = page.getByRole('listitem', { name: `Posto ${posto.nome}` })
  await expect(cartao).toContainText('sem visor')
  await cartao.getByRole('button', { name: 'Visor do cliente' }).click()
  const dialogo = page.getByRole('dialog', { name: `Visor do cliente — ${posto.nome}` })
  await dialogo.getByRole('radio', { name: /Segundo monitor/ }).check()
  await dialogo.getByRole('button', { name: 'Guardar e testar' }).click()
  await expect(page.getByText(`Mensagem de teste enviada para o visor do posto ${posto.nome}.`)).toBeVisible()
  const endereco = (await dialogo.getByLabel('Endereço do visor').textContent())!
  expect(endereco).toMatch(new RegExp(`/visor[?]terminal=${posto.id}&chave=[0-9a-f]{32}$`))
  await dialogo.getByRole('button', { name: 'Fechar' }).last().click()
  await expect(cartao).toContainText('segundo monitor (automático)')

  // A página do visor (o que o agente abre no segundo monitor), sem sessão.
  const visor = await (await context.browser()!.newContext()).newPage()
  await visor.goto(endereco)
  await expect(visor).toHaveTitle('LAMGest — Visor do cliente')
  await expect(visor.getByText(`Teste do visor — ${posto.nome}`)).toBeVisible()
  const ecra = visor.getByLabel('Visor do cliente')

  // Venda ao balcão: o visor mostra o artigo e o total logo.
  await page.goto('/pos')
  await expect(page.getByRole('button', { name: 'Balcão', exact: true })).toBeEnabled()
  await expect(visor.getByText('Bem-vindo!')).toBeVisible({ timeout: 15_000 }) // o POS aberto põe o visor em repouso
  await page.waitForTimeout(500)
  await page.evaluate((c) => {
    for (const key of [...c, 'Enter']) document.body.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  }, codigoBarras)
  const pagar = page.getByRole('button', { name: /^Pagar 50,00/ })
  await expect(pagar).toBeVisible({ timeout: 45_000 })
  await expect(ecra).toHaveAttribute('data-fase', 'Venda')
  await expect(visor.getByLabel('Artigos')).toContainText(`Sumo Visor ${sufixo}`)
  await expect(visor.getByLabel('Total')).toHaveText(/50,00/)

  // Pagamento: o valor recebido escrito no POS mostra o troco ao cliente; depois de pago, o troco e o agradecimento.
  await pagar.click()
  const pagamento = page.getByRole('dialog')
  await pagamento.getByLabel('Valor recebido (MT)').fill('100')
  await expect(ecra).toHaveAttribute('data-fase', 'Pagamento')
  await expect(visor.getByLabel('Troco')).toHaveText(/50,00/)
  await pagamento.getByRole('button', { name: /^Confirmar 50,00/ }).click()
  await expect(page.getByText(/pago\. Venda a dinheiro VD-\d{4}-\d{6}/)).toBeVisible()
  await expect(ecra).toHaveAttribute('data-fase', 'Pago')
  await expect(visor.getByText('Obrigado, volte sempre!')).toBeVisible()
  await expect(visor.getByLabel('Troco')).toHaveText(/50,00/)
  // Passado o tempo do agradecimento, volta às boas-vindas (o POS já está pronto para a venda seguinte).
  await expect(visor.getByText('Bem-vindo!')).toBeVisible({ timeout: 20_000 })

  // Desligar o visor: a página deixa de mostrar o conteúdo.
  const desligar = await page.request.put(`/api/configuracoes/postos/${posto.id}/visor`, { data: { tipo: 'Nenhum', porta: null, protocolo: 'EscPos', velocidade: 9600, colunas: 20, ecra: null } })
  expect(desligar.status()).toBe(204)
  await visor.reload()
  await expect(visor.getByText('Visor do cliente desligado.')).toBeVisible()
  await visor.close()
  await page.evaluate(() => localStorage.removeItem('lamgest.terminalId'))
})

test('lojas ligadas: recusa a própria loja e o seletor muda para outra loja', async ({ page }) => {
  test.setTimeout(150_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  await page.goto('/configuracoes?aba=lojas')
  await expect(page.getByRole('heading', { name: 'Lojas ligadas' })).toBeVisible()
  await page.getByLabel('Endereço da outra loja na VPN').fill(`http://localhost:${PORTA_API}`)
  await page.getByRole('button', { name: 'Acrescentar loja' }).click()
  await expect(page.getByText('Esse endereço é desta própria loja.')).toBeVisible()

  // Sem um segundo servidor, a «outra loja» é o mesmo sistema noutro endereço ([::1] em vez de localhost), só na base E2E.
  const outro = `http://[::1]:${new URL(page.url()).port}`
  execFileSync('sqlcmd', ['-S', process.env.LAMGEST_TESTES_SQL ?? '.\\SYSON', '-E', '-b', '-d', 'LAMGestRestaurantDB_E2E', '-Q',
    `INSERT INTO LojasLigadas (Identificador, Nome, Nuit, Endereco, Cor, AdicionadaEmUtc) VALUES ('e2e-outra', N'Loja E2E Polana', NULL, '${outro}', '#dc2626', SYSUTCDATETIME())`])
  try {
    await page.reload()
    await expect(page.getByRole('listitem', { name: 'Loja Loja E2E Polana' })).toBeVisible()
    const seletor = page.getByRole('combobox', { name: 'Loja' })
    await seletor.selectOption({ label: 'Loja E2E Polana' })
    await expect(page).toHaveURL(`${outro}/login`)
    // Na entrada da outra loja, o seletor também aparece (lojas que esse servidor conhece).
    await expect(page.getByRole('combobox', { name: 'Loja' })).toBeVisible()
  } finally {
    execFileSync('sqlcmd', ['-S', process.env.LAMGEST_TESTES_SQL ?? '.\\SYSON', '-E', '-b', '-d', 'LAMGestRestaurantDB_E2E', '-Q',
      "DELETE FROM LojasLigadas WHERE Identificador = 'e2e-outra'"])
  }
})

test('Abrir Dia / Fechar Dia: fecho do dia de todos os caixas e caixas bloqueados sem dia', async ({ page }) => {
  test.setTimeout(120_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  await garantirDiaAberto(page.request)
  // O dia só fecha com os caixas todos fechados: fecham-se pela API os que os testes anteriores deixaram abertos.
  type Sessao = { id: number; rowVersion: string; porMetodo: { metodoPagamentoId: number; contar: boolean }[] }
  const abertas = await (await page.request.get('/api/caixa/sessoes?estado=Aberta&tamanho=100')).json() as { itens: { id: number }[] }
  for (const { id } of abertas.itens) {
    const s = await (await page.request.get(`/api/caixa/sessoes/${id}`)).json() as Sessao
    await criar(page.request, `/api/caixa/sessoes/${id}/fechar`, {
      contagens: s.porMetodo.filter((m) => m.contar).map((m) => ({ metodoPagamentoId: m.metodoPagamentoId, valorContado: 0 })),
      observacoes: 'Fecho E2E', rowVersion: s.rowVersion,
    })
  }

  await page.getByRole('link', { name: 'Caixa' }).click()
  await expect(page.getByTestId('estado-dia')).toHaveText(/^Dia .+ aberto$/)
  await page.getByRole('button', { name: 'Fechar Dia' }).click()
  const fechar = page.getByRole('dialog', { name: /^Fechar Dia/ })
  await expect(fechar.getByTestId('resumo-dia')).toContainText('Ponto de situação')
  await expect(fechar.getByText('Resumo por caixa')).toBeVisible()
  await fechar.getByRole('button', { name: 'Fechar Dia' }).click()
  await page.getByRole('button', { name: 'Confirmar fecho do dia' }).click()
  const fecho = page.getByRole('dialog', { name: /^Fecho do dia .+ · FD-/ })
  await expect(fecho.getByTestId('resumo-dia')).toContainText('Dia fechado')
  await expect(fecho.getByText('Produtos vendidos por categoria')).toBeVisible()
  const numero = (await fecho.getByRole('heading').first().textContent())!.split('· ')[1]
  await fecho.getByRole('button', { name: 'Fechar', exact: true }).last().click()

  // Sem dia aberto: o caixa não abre e a mesma data não se reabre.
  await expect(page.getByTestId('estado-dia')).toHaveText('Dia não aberto')
  await expect(page.getByText(new RegExp(`Último fecho: .+(${numero})`))).toBeVisible()
  const terminal = await criar<{ id: number }>(page.request, '/api/terminais', { nome: `Caixa Dia ${Date.now() % 100000}`, descricao: null })
  const recusa = await page.request.post('/api/caixa/sessoes', { data: { terminalId: terminal.id, fundoInicial: 0, observacoes: null } })
  expect(recusa.status()).toBe(422)
  expect(await recusa.text()).toContain('Abrir Dia')
  await page.getByRole('button', { name: 'Abrir Dia' }).click()
  const abrir = page.getByRole('dialog', { name: 'Abrir Dia' })
  await abrir.getByRole('button', { name: 'Abrir Dia' }).click()
  await expect(abrir.getByText(/já foi aberto e fechado/)).toBeVisible()
  await abrir.getByRole('button', { name: 'Cancelar' }).click()

  // Histórico e documento do fecho (o mesmo que se imprime em 80 mm ou A4).
  await page.getByRole('tab', { name: 'Dias de operação' }).click()
  await expect(page.getByRole('row', { name: new RegExp(numero) })).toContainText('Fechado')
  const estado = await (await page.request.get('/api/caixa/dias/estado')).json() as { ultimoFechado: { id: number } }
  const documento = await (await page.request.get(`/api/relatorios/documento?tipo=FechoDia&diaId=${estado.ultimoFechado.id}`)).json() as { linhas: { texto: string }[] }
  expect(documento.linhas.map((l) => l.texto)).toEqual(expect.arrayContaining(['FECHO DO DIA', '6. Produtos vendidos por categoria']))
})

/** Junta o algarismo de controlo EAN-13 aos 12 algarismos dados. */
function ean13(doze: string) {
  const soma = [...doze].reduce((s, c, i) => s + Number(c) * (i % 2 === 0 ? 1 : 3), 0)
  return doze + ((10 - (soma % 10)) % 10)
}

test('balança: etiqueta com o peso lida no POS e balança ligada ao posto com «Usar peso»', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const sufixo = String(Date.now() % 100000)
  const plu = 10000 + (Date.now() % 80000)
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: `Talho ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const unidades = await (await page.request.get('/api/stock/unidades')).json() as { id: number; codigo: string }[]
  await criar(page.request, '/api/produtos', {
    codigo: `E2E-PESO${sufixo}`, codigoBarras: null, nome: `Picanha ${sufixo}`, descricao: null, categoriaId: categoria.id, tipo: 'Restaurante', precoVenda: 1250, custo: 600,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null, unidadeMedidaId: unidades.find((u) => u.codigo === 'kg')!.id,
    armazemId: null, pedeQuantidade: true, codigoBalanca: plu,
  })
  // Posto deste computador com número (o agente da balança autentica-se com ele).
  const numero = 900 + (Date.now() % 99)
  const posto = await criar<{ id: number; nome: string }>(page.request, '/api/terminais', { nome: `Caixa Peso ${sufixo}`, descricao: null, numero })
  const agente = await criar<{ chave: string }>(page.request, `/api/postos/${numero}/agente`, {})
  await page.evaluate((id) => localStorage.setItem('lamgest.terminalId', String(id)), posto.id)

  // Configurações → Documentos e POS: etiquetas da balança com o peso (exemplo do formato).
  await page.goto('/configuracoes?aba=documentos-pos')
  const etiquetas = page.getByRole('region', { name: 'Etiquetas da balança' })
  await etiquetas.getByRole('checkbox', { name: 'Ler as etiquetas da balança no POS' }).check()
  await expect(etiquetas.getByTestId('exemplo-etiqueta')).toHaveText('21 00123 00485 C')
  await page.getByRole('button', { name: /^Guardar/ }).click()
  await expect(page.getByText('Configurações guardadas.')).toBeVisible()

  try {
    // POS: a etiqueta (escrita na pesquisa e Enter, como o leitor) lança o produto com 0,485 kg.
    await page.goto('/pos')
    await page.getByRole('button', { name: 'Balcão', exact: true }).click()
    const pesquisa = page.getByLabel('Pesquisar produto')
    await pesquisa.fill(ean13(`21${plu}00485`))
    await pesquisa.press('Enter')
    await expect(page.getByRole('button', { name: /^Pagar 606,25/ })).toBeVisible()
    await expect(pesquisa).toHaveValue('')

    // Configurações → Postos → Balança: a pedido com «W» na COM4.
    await page.goto('/configuracoes?aba=postos')
    const cartao = page.getByRole('listitem', { name: `Posto ${posto.nome}` })
    await expect(cartao).toContainText('sem balança')
    await cartao.getByRole('button', { name: 'Balança' }).click()
    const dialogo = page.getByRole('dialog', { name: `Balança — ${posto.nome}` })
    await dialogo.getByRole('radio', { name: /A pedido \(«W»\)/ }).check()
    await dialogo.getByLabel('Porta COM').fill('COM4')
    await dialogo.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(cartao).toContainText('a pedido («w») em COM4 (9600 bps, 8N1)')

    // POS: o diálogo do peso pede o peso ao agente; o agente (aqui simulado) envia o que lê na balança.
    const leitura = (peso: number, estavel: boolean) => page.request.post('/api/agente/balanca/leitura', {
      headers: { 'X-Posto': String(numero), 'X-Agente-Token': agente.chave }, data: { peso, estavel, erro: null },
    })
    await page.goto('/pos')
    await page.getByRole('button', { name: 'Balcão', exact: true }).click()
    await page.getByRole('button', { name: new RegExp(`Picanha ${sufixo}`) }).click()
    const balanca = page.getByTestId('balanca-pos')
    await expect(balanca).toBeVisible()
    const usar = balanca.getByRole('button', { name: 'Usar peso' })
    await expect(async () => {
      expect((await leitura(1.2, false)).ok()).toBeTruthy()
      await expect(balanca).toContainText('A estabilizar…', { timeout: 1500 })
    }).toPass({ timeout: 20_000 })
    await expect(usar).toBeDisabled()
    await expect(async () => {
      expect((await leitura(1.25, true)).ok()).toBeTruthy()
      await expect(usar).toBeEnabled({ timeout: 1500 })
    }).toPass({ timeout: 20_000 })
    await expect(balanca).toContainText('1,250 kg')
    await expect(page.getByRole('dialog').getByLabel('Ou escreva o peso (kg)')).toBeVisible()
    await usar.click()
    // Venda nova ao balcão: 1,25 kg × 1 250,00 (calculado no servidor).
    await expect(page.getByRole('button', { name: /^Pagar 1562,50/ })).toBeVisible()
  } finally {
    await page.request.put('/api/configuracoes', { data: { valores: { 'balanca.etiquetas': 'false' } } })
    await page.request.put(`/api/configuracoes/postos/${posto.id}/balanca`, { data: { modo: 'Nenhuma', porta: null, velocidade: 9600, formato: 'Oito_N_1' } })
  }
})

test('categoria só de stock: agrupa as matérias-primas sem aparecer no POS', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const nome = `Carnes MP ${Date.now() % 100000}`

  // Produtos → Categorias → Nova categoria, marcada «Não mostrar no POS».
  await page.goto('/produtos')
  await page.getByRole('tab', { name: 'Categorias' }).click()
  await page.getByRole('button', { name: 'Nova categoria' }).click()
  const dialogo = page.getByRole('dialog', { name: 'Nova categoria' })
  await dialogo.getByLabel('Nome', { exact: true }).fill(nome)
  await dialogo.getByRole('checkbox', { name: 'Não mostrar no POS (só para os artigos de stock)' }).check()
  await dialogo.getByRole('button', { name: 'Guardar' }).click()
  const linha = page.getByRole('listitem').filter({ hasText: nome })
  await expect(linha).toContainText('Só stock · não aparece no POS')

  // Matéria-prima nessa categoria (artigo de stock).
  const categorias = await (await page.request.get('/api/categorias')).json() as { id: number; nome: string }[]
  const categoria = categorias.find((c) => c.nome === nome)!
  const unidades = await (await page.request.get('/api/stock/unidades')).json() as { id: number; codigo: string }[]
  const artigo = `Picanha crua ${Date.now() % 100000}`
  await criar(page.request, '/api/stock/artigos', {
    codigo: `MP${Date.now() % 1000000}`, nome: artigo, tipo: 'Ingrediente', unidadeMedidaId: unidades.find((u) => u.codigo === 'kg')!.id, stockMinimo: 0,
    permiteStockNegativo: false, categoriaId: categoria.id, localizacao: null,
  })

  // O POS não mostra a categoria.
  await page.goto('/pos')
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  const separadores = page.getByRole('tablist', { name: 'Categorias' })
  await expect(separadores.getByRole('tab', { name: 'Todas' })).toBeVisible()
  await expect(separadores.getByRole('tab', { name: nome })).toHaveCount(0)

  // Stock → Artigos: filtro pela categoria.
  await page.goto('/stock')
  await page.getByRole('tab', { name: 'Artigos' }).click()
  await page.getByLabel('Filtrar por categoria').selectOption({ label: `${nome} (só stock)` })
  await expect(page.getByText(artigo)).toBeVisible()
})

test('acompanhamentos: o prato pede a escolha, sem mudar o preço', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const sufixo = Date.now() % 100000
  const prato = `Bife E2E ${sufixo}`
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: `Pratos AC ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  await criar(page.request, '/api/produtos', {
    codigo: `E2E-AC${sufixo}`, codigoBarras: null, nome: prato, descricao: null, categoriaId: categoria.id, tipo: 'Restaurante', precoVenda: 500, custo: 200,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
  })

  // Produtos → Acompanhamentos → Novo grupo: escolher 1 entre Arroz e Salada fria, no prato.
  await page.goto('/produtos')
  await page.getByRole('tab', { name: 'Acompanhamentos' }).click()
  await page.getByRole('button', { name: 'Novo grupo' }).click()
  const editor = page.getByRole('dialog', { name: 'Novo grupo de acompanhamentos' })
  await editor.getByLabel('Nome do grupo').fill(`Acompanhamento ${sufixo}`)
  await editor.getByLabel('Opção 1', { exact: true }).fill('Arroz')
  await editor.getByRole('button', { name: 'Opção', exact: true }).click()
  await editor.getByLabel('Opção 2', { exact: true }).fill('Salada fria')
  await editor.getByLabel('Procurar prato').fill(prato)
  await editor.getByRole('button', { name: new RegExp(prato) }).click()
  await expect(editor.getByRole('button', { name: `Retirar ${prato}` })).toBeVisible()
  await editor.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('listitem').filter({ hasText: `Acompanhamento ${sufixo}` })).toContainText(`Pratos: ${prato}`)

  // POS: ao tocar no prato, abre a escolha; só lança depois de escolher.
  await page.goto('/pos')
  await page.getByRole('button', { name: 'Balcão', exact: true }).click()
  await page.getByRole('button', { name: new RegExp(prato) }).click()
  const escolha = page.getByRole('dialog', { name: prato })
  await expect(escolha.getByRole('button', { name: 'Lançar' })).toBeDisabled()
  await escolha.getByRole('button', { name: 'Salada fria' }).click()
  await escolha.getByRole('button', { name: 'Arroz' }).click() // uma só escolha: troca
  await expect(escolha.getByRole('button', { name: 'Salada fria' })).toHaveAttribute('aria-pressed', 'false')
  await escolha.getByRole('button', { name: 'Lançar' }).click()
  await expect(page.getByLabel('Pedido actual').getByTestId('acompanhamentos-item')).toHaveText('+ Arroz')
  await expect(page.getByRole('button', { name: /^Pagar 500,00/ })).toBeVisible()
})

test('fichas de artigo e produto: código automático, armazém do sistema e cursor no campo do erro', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const sufixo = Date.now() % 100000

  // Stock → Artigos → Novo artigo: código automático (só leitura); sem nome, o cursor vai para o nome.
  await page.goto('/stock')
  await page.getByRole('tab', { name: 'Artigos' }).click()
  await page.getByRole('button', { name: 'Novo artigo' }).click()
  const ficha = page.getByRole('dialog', { name: 'Novo artigo' })
  await expect(ficha.getByLabel('Código')).toHaveValue(/^A\d{4,}$/)
  await expect(ficha.getByLabel('Código')).not.toBeEditable()
  await ficha.getByRole('button', { name: 'Guardar' }).click()
  await expect(ficha.getByText('Indique o nome.')).toBeVisible()
  await expect(ficha.getByLabel('Nome')).toBeFocused()
  await ficha.getByLabel('Nome').fill(`Óleo E2E ${sufixo}`)
  await expect(ficha.getByLabel('Armazém', { exact: true })).toHaveValue(/\d+/)
  await ficha.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByText(/Artigo A\d{4,} criado/)).toBeVisible()

  // Produto com stock: passar de «un» para kg é recusado e o cursor fica na unidade.
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: `Unidades E2E ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const produto = `Queijo E2E ${sufixo}`
  await criar(page.request, '/api/produtos', {
    codigo: `E2E-UN${sufixo}`, codigoBarras: null, nome: produto, descricao: null, categoriaId: categoria.id, tipo: 'Restaurante', precoVenda: 100, custo: 40,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null,
    stockInicial: { quantidade: 3, custoUnitario: 40, stockMinimo: 0, armazemId: null },
  })
  await page.goto('/produtos')
  await page.getByLabel('Pesquisar produtos').fill(produto)
  await page.getByRole('button', { name: `Editar ${produto}` }).click()
  const formulario = page.getByRole('dialog', { name: `Editar ${produto}` })
  await formulario.getByLabel('Unidade', { exact: true }).selectOption({ label: 'Quilograma (kg)' })
  await formulario.getByRole('button', { name: 'Guardar' }).click()
  await expect(formulario.getByText(/Acerte primeiro o stock para zero/).first()).toBeVisible()
  await expect(formulario.getByLabel('Unidade', { exact: true })).toBeFocused()
})

test('artigo com stock inicial e ficha técnica com o armazém de cada ingrediente', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const sufixo = Date.now() % 100000
  const artigo = `Farinha E2E ${sufixo}`
  const armazens = await (await page.request.get('/api/stock/armazens')).json() as { id: number; nome: string; principal: boolean }[]
  const principal = armazens.find((a) => a.principal)!

  // Novo artigo com stock inicial: o saldo aparece logo na lista.
  await page.goto('/stock')
  await page.getByRole('tab', { name: 'Artigos' }).click()
  await page.getByRole('button', { name: 'Novo artigo' }).click()
  const ficha = page.getByRole('dialog', { name: 'Novo artigo' })
  await ficha.getByLabel('Nome').fill(artigo)
  await ficha.getByLabel(/^Quantidade/).fill('12.5')
  await ficha.getByLabel('Custo unitário (MT, sem IVA)').fill('40')
  await ficha.getByRole('button', { name: 'Guardar' }).click()
  await expect(ficha).toBeHidden({ timeout: 60_000 })
  await page.getByLabel('Pesquisar artigos').fill(artigo)
  await expect(page.getByRole('row', { name: new RegExp(artigo) })).toContainText('12,5', { timeout: 60_000 })

  // Ficha técnica: «Aplicar a todos os ingredientes» escolhe o armazém de onde sai.
  const categoria = await criar<{ id: number }>(page.request, '/api/categorias', { nome: `Pães ${sufixo}`, descricao: null, categoriaPaiId: null, estacaoPreparacaoId: null, cor: null, ordem: 0 })
  const produto = await criar<{ id: number; nome: string }>(page.request, '/api/produtos', {
    codigo: `E2E-PA${sufixo}`, codigoBarras: null, nome: `Pão E2E ${sufixo}`, descricao: null, categoriaId: categoria.id, tipo: 'Restaurante', precoVenda: 20, custo: 5,
    taxaImpostoId: null, imagemUrl: null, enviaCozinha: false, disponivel: true, rowVersion: null, controlaStock: false,
  })
  await page.getByRole('tab', { name: 'Fichas técnicas' }).click()
  await page.getByLabel('Pesquisar produto').fill(produto.nome)
  await page.getByRole('row', { name: new RegExp(produto.nome) }).click()
  const editor = page.getByRole('dialog', { name: `Ficha técnica: ${produto.nome}` })
  await editor.getByLabel('Ingrediente 1', { exact: true }).selectOption({ label: `${artigo} (kg)` })
  await editor.getByLabel('Quantidade 1', { exact: true }).fill('0.1')
  await editor.getByLabel('Aplicar a todos os ingredientes').selectOption({ label: principal.nome })
  await expect(editor.getByLabel('Armazém 1', { exact: true })).toHaveValue(String(principal.id))
  await editor.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByText('Ficha técnica criada.')).toBeVisible()
  const versoes = await (await page.request.get(`/api/receitas/produtos/${produto.id}`)).json() as { linhas: { armazemId: number | null }[] }[]
  expect(versoes[0].linhas[0].armazemId).toBe(principal.id)
})

test('relatórios de acompanhamentos, existências e transferências, e a condição do fecho do dia', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const armazens = await (await page.request.get('/api/stock/armazens')).json() as { id: number; nome: string; principal: boolean }[]
  const principal = armazens.find((a) => a.principal)!

  await page.goto('/relatorios?tipo=acompanhamentos')
  await expect(page.getByRole('heading', { name: 'Relatório — Acompanhamentos' })).toBeVisible()
  await expect(page.getByText('Escolhas', { exact: true })).toBeVisible()

  await page.getByRole('tab', { name: 'Existências por armazém' }).click()
  await page.getByLabel('Armazém', { exact: true }).selectOption({ label: principal.nome })
  await expect(page).toHaveURL(new RegExp(`armazem=${principal.id}`))
  await expect(page.getByText('Valor em stock', { exact: true })).toBeVisible()

  await page.getByRole('tab', { name: 'Transferências' }).click()
  await expect(page.getByText('Valor transferido', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Imprimir documento' }).click()
  await expect(page.getByRole('dialog').locator('select').first()).toHaveValue('Transferencias')
  await page.keyboard.press('Escape')

  // Configurações → Documentos e POS: a condição existe e vem ligada (comportamento de sempre).
  await page.goto('/configuracoes?aba=documentos-pos')
  await expect(page.getByRole('checkbox', { name: 'Permitir fechar o dia com contas abertas' })).toBeChecked()
})

test('modo escuro: o botão da barra alterna o tema e a escolha fica neste computador', async ({ page }) => {
  await entrar(page)
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  const html = page.locator('html')
  const fundo = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor)
  await expect(html).not.toHaveClass(/escuro/)
  const claro = await fundo()

  await page.getByRole('button', { name: 'Mudar para o modo escuro' }).click()
  await expect(html).toHaveClass(/escuro/)
  expect(await fundo()).not.toBe(claro)
  // Fica guardado: depois de recarregar continua escuro.
  await page.reload()
  await expect(page.getByText('Ligado ao servidor')).toBeVisible()
  await expect(html).toHaveClass(/escuro/)
  await page.getByRole('button', { name: 'Mudar para o modo claro' }).click()
  await expect(html).not.toHaveClass(/escuro/)
  expect(await fundo()).toBe(claro)
})

// Último teste do ficheiro: apaga os dados da base E2E (os anteriores já correram).
test('modo de testes: apagar os dados de teste e iniciar a produção', async ({ page }) => {
  test.setTimeout(180_000)
  await entrar(page)
  await expect(page.getByText('Modo de testes: as vendas e os documentos podem ser apagados')).toBeVisible({ timeout: 60_000 })
  await page.getByRole('link', { name: 'Iniciar produção' }).click()
  await expect(page.getByText('O sistema está em modo de testes')).toBeVisible()
  await expect(page.getByRole('row', { name: /Pedidos \(vendas\)/ })).not.toContainText(/^Pedidos \(vendas\)0$/)

  await page.getByRole('button', { name: /Apagar dados de teste e iniciar produção/ }).click()
  const dialogo = page.getByRole('dialog', { name: 'Apagar dados de teste e iniciar produção' })
  const botao = dialogo.getByRole('button', { name: 'Apagar e iniciar produção' })
  await dialogo.getByLabel('A sua palavra-passe').fill(credenciais().palavraPasse)
  await dialogo.getByLabel(/Escreva/).fill('apagar')
  await expect(botao).toBeDisabled()
  await dialogo.getByLabel(/Escreva/).fill('APAGAR DADOS DE TESTE')
  await botao.click()
  await expect(page.getByText('Dados de teste apagados')).toBeVisible({ timeout: 120_000 })
  await expect(page.getByRole('heading', { name: 'Sistema em produção' })).toBeVisible()
  await page.reload()
  await expect(page.getByText('Modo de testes: as vendas e os documentos podem ser apagados')).toHaveCount(0)
  const pedidos = await (await page.request.get('/api/pedidos?tamanho=5')).json() as { total: number }
  expect(pedidos.total).toBe(0)
})
