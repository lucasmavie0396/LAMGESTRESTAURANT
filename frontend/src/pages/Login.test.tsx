import { afterEach, describe, expect, it, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { Login } from './Login'
import { LigacaoProvider } from '../contexts/LigacaoContext'
import { SessaoProvider } from '../contexts/SessaoContext'
import { RespostaHttp, servidorFalso, type Rotas } from '../testes/servidorFalso'

const sessao = {
  id: 1, nomeUtilizador: 'caixa1', nomeCompleto: 'Caixa Um', perfis: ['Caixa'], permissoes: ['pos.usar'], deveAlterarPalavraPasse: false,
}

function desenharLogin(rotas: Rotas) {
  const { pedidos } = servidorFalso({ 'GET /api/auth/me': new RespostaHttp(401), 'GET /api/lojas*': { actual: null, outras: [] }, ...rotas })
  render(
    <MemoryRouter initialEntries={['/login']}>
      <LigacaoProvider>
        <SessaoProvider>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/" element={<p>Página inicial</p>} />
          </Routes>
        </SessaoProvider>
      </LigacaoProvider>
    </MemoryRouter>,
  )
  return pedidos
}

async function preencher(utilizador: string, palavraPasse: string) {
  fireEvent.change(await screen.findByLabelText('Utilizador'), { target: { value: utilizador } })
  fireEvent.change(screen.getByLabelText('Palavra-passe'), { target: { value: palavraPasse } })
  const entrar = screen.getByRole('button', { name: 'Entrar' })
  await waitFor(() => expect(entrar).toBeEnabled()) // depois de confirmar a ligação ao servidor
  fireEvent.click(entrar)
}

describe('ecrã de entrada', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    localStorage.clear()
  })

  it('campos vazios mostram o que falta sem contactar o servidor', async () => {
    const pedidos = desenharLogin({})
    await preencher('', '')
    expect(await screen.findByText('Indique o nome de utilizador.')).toBeInTheDocument()
    expect(screen.getByText('Indique a palavra-passe.')).toBeInTheDocument()
    expect(pedidos.filter((p) => p.url.endsWith('/api/auth/login'))).toHaveLength(0)
  })

  it('palavra-passe errada mostra a mensagem do servidor e continua no ecrã de entrada', async () => {
    desenharLogin({ 'POST /api/auth/login': new RespostaHttp(401, { title: 'Não autorizado', detail: 'Utilizador ou palavra-passe inválidos.' }) })
    await preencher('caixa1', 'errada')
    expect(await screen.findByText('Utilizador ou palavra-passe inválidos.')).toBeInTheDocument()
    expect(screen.queryByText('Página inicial')).not.toBeInTheDocument()
  })

  it('muitas tentativas a partir do mesmo computador: mostra que tem de aguardar', async () => {
    desenharLogin({ 'POST /api/auth/login': new RespostaHttp(429, { title: 'Demasiadas tentativas', detail: 'Demasiadas tentativas a partir deste computador. Aguarde um minuto e tente de novo.' }) })
    await preencher('caixa1', 'qualquer')
    expect(await screen.findByText(/Aguarde um minuto/)).toBeInTheDocument()
  })

  it('credenciais certas entram e abrem a página inicial', async () => {
    const pedidos = desenharLogin({ 'POST /api/auth/login': sessao })
    await preencher('  caixa1 ', 'segredo123')
    expect(await screen.findByText('Página inicial')).toBeInTheDocument()
    expect(pedidos.find((p) => p.url.endsWith('/api/auth/login'))?.corpo).toEqual({ nomeUtilizador: 'caixa1', palavraPasse: 'segredo123' })
  })

  it('sem base de dados no servidor não deixa entrar e explica porquê', async () => {
    desenharLogin({ 'GET /api/saude': { api: true, baseDados: false, migracoesPendentes: false, horaServidorUtc: '' } })
    expect(await screen.findByText(/Não é possível iniciar sessão/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeDisabled()
  })
})
