// Início da produção (apaga os dados de teste: tem de ser o último).
import { expect, test } from '@playwright/test'
import { credenciais, entrar } from './ajudas'

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
