// Vendas no POS: mesa, balcão, leitor, cartões, facturas, PDA, visor, balança e acompanhamentos.
import { readFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { strFromU8, unzipSync } from 'fflate'
import { entrar, criar, abrirSessaoCaixa, prepararRestaurante, ean13 } from './ajudas'

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
  await expect(page.locator('body')).toHaveAttribute('data-leitor-codigos', 'activo') // o leitor já está a escutar
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
  await expect(page.locator('body')).toHaveAttribute('data-leitor-codigos', 'activo')
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
