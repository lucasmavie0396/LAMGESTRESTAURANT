// Entrada, licença, posto, ligação, tema e idioma.
import { expect, test } from '@playwright/test'
import { credenciais, entrar, criar } from './ajudas'

test('login recusa credenciais erradas e entra com as correctas', async ({ page }) => {
  await entrar(page, 'errada-123')
  await expect(page.getByText('Utilizador ou palavra-passe inválidos.')).toBeVisible()

  await entrar(page)
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByText('Administrador E2E').first()).toBeVisible()
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

test('idioma: o seletor PT/EN no login e na barra troca o ecrã e as mensagens da API para inglês', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('button', { name: 'en', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  await page.locator('#nomeUtilizador').fill(credenciais().utilizador)
  await page.locator('#palavraPasse').fill('errada-123')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  // Mensagem que vem da API, traduzida no ecrã.
  await expect(page.getByText('Invalid username or password.')).toBeVisible()
  await page.locator('#palavraPasse').fill(credenciais().palavraPasse)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByRole('link', { name: 'Register' })).toBeVisible()
  await expect(page.getByText('Administrador E2E').first()).toBeVisible() // o nome do utilizador não se traduz
  await page.goto('/caixa')
  await expect(page.getByRole('heading', { name: 'Register' })).toBeVisible()
  // Volta a Português na barra; a escolha fica neste computador.
  await page.getByRole('button', { name: 'pt', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Caixa' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('link', { name: 'Caixa' })).toBeVisible()
  await expect(page.locator('html')).not.toHaveAttribute('lang', 'en')
})
