// Gestão: catálogo, impressoras, postos, clientes, auditoria, stock, lojas, dia e relatórios.
import { execFileSync, spawn } from 'node:child_process'
import { existsSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test } from '@playwright/test'
import { PORTA_API } from './comum.mjs'
import { gerarXlsx } from '../src/utils/xlsx'
import { entrar, criar, garantirDiaAberto, abrirSessaoCaixa, AGENTE } from './ajudas'

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

test('sistema: o administrador vê o estado do servidor (cópias de segurança, base e disco)', async ({ page }) => {
  await entrar(page)
  await expect(page.getByRole('link', { name: 'Configurações' })).toBeVisible()
  await page.goto('/configuracoes?aba=sistema')
  await expect(page.getByRole('heading', { name: 'Estado do servidor' })).toBeVisible()
  await expect(page.getByText('LAMGestRestaurantDB_E2E')).toBeVisible()
  await expect(page.getByText('Disco da base')).toBeVisible()
  // A base E2E acabou de ser criada e não tem cópias: ainda não há aviso (só depois do primeiro dia).
  await expect(page.getByRole('alert').filter({ hasText: 'Sem cópias de segurança' })).toHaveCount(0)
  await page.screenshot({ path: test.info().outputPath('estado-servidor.png'), fullPage: true })
})
