// Auxiliares comuns aos testes E2E (sessão, criação pela API, restaurante mínimo).
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, type APIRequestContext, type Page } from '@playwright/test'
import { CREDENCIAIS } from './comum.mjs'

export function credenciais(): { utilizador: string; palavraPasse: string; licencaRenovada: string } {
  return JSON.parse(readFileSync(CREDENCIAIS, 'utf8'))
}

export async function entrar(page: Page, palavraPasse = credenciais().palavraPasse) {
  await page.goto('/login')
  await page.getByLabel('Utilizador').fill(credenciais().utilizador)
  await page.getByLabel('Palavra-passe').fill(palavraPasse)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
}

export async function criar<T>(request: APIRequestContext, url: string, dados: unknown): Promise<T> {
  const resposta = await request.post(url, { data: dados })
  expect(resposta.ok(), `${url}: ${resposta.status()} ${await resposta.text()}`).toBeTruthy()
  return resposta.json() as Promise<T>
}

/** Abre o dia de operação se ainda não estiver aberto (os caixas só abrem com o dia aberto). */

export async function garantirDiaAberto(request: APIRequestContext) {
  const resposta = await request.post('/api/caixa/dias', { data: { data: null, observacoes: null } })
  expect([201, 409], `Abrir Dia: ${resposta.status()} ${await resposta.text()}`).toContain(resposta.status())
}

/** Sessão de caixa do administrador no terminal, depois de garantir o dia aberto. */

export async function abrirSessaoCaixa(request: APIRequestContext, terminalId: number, fundoInicial = 0) {
  await garantirDiaAberto(request)
  await criar(request, '/api/caixa/sessoes', { terminalId, fundoInicial, observacoes: null })
}

/** Catálogo, sala e caixa mínimos, criados pela API com a sessão do administrador. */

export async function prepararRestaurante(request: APIRequestContext, sufixo: string, abrirCaixa = true) {
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

export const AGENTE = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'backend', 'LAMGestRestaurant.Agente', 'bin', 'Debug', 'net9.0-windows', 'LAMGest-Agente.exe')

export function ean13(doze: string) {
  const soma = [...doze].reduce((s, c, i) => s + Number(c) * (i % 2 === 0 ? 1 : 3), 0)
  return doze + ((10 - (soma % 10)) % 10)
}
