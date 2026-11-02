import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { RotaProtegida } from './components/RotaProtegida'
import { AvisosProvider } from './contexts/AvisosContext'
import { LigacaoProvider } from './contexts/LigacaoContext'
import { SessaoProvider } from './contexts/SessaoContext'
import { LayoutPrincipal } from './layouts/LayoutPrincipal'
import { PaginaCaixa } from './modules/caixa/PaginaCaixa'
import { PaginaMesas } from './modules/mesas/PaginaMesas'
import { PaginaProdutos } from './modules/produtos/PaginaProdutos'
import { PaginaPerfis } from './modules/utilizadores/PaginaPerfis'
import { PaginaUtilizadores } from './modules/utilizadores/PaginaUtilizadores'
import { AlterarPalavraPasse } from './pages/AlterarPalavraPasse'
import { Inicio } from './pages/Inicio'
import { Login } from './pages/Login'
import { P } from './utils/permissoes'

export default function App() {
  return (
    <LigacaoProvider>
      <SessaoProvider>
        <AvisosProvider>
          <BrowserRouter>
            <Routes>
              <Route path="/login" element={<Login />} />
              <Route
                element={
                  <RotaProtegida>
                    <LayoutPrincipal />
                  </RotaProtegida>
                }
              >
                <Route index element={<Inicio />} />
                <Route path="alterar-palavra-passe" element={<AlterarPalavraPasse />} />
                <Route path="utilizadores" element={<RotaProtegida permissoes={[P.utilizadoresGerir]}><PaginaUtilizadores /></RotaProtegida>} />
                <Route path="perfis" element={<RotaProtegida permissoes={[P.perfisGerir]}><PaginaPerfis /></RotaProtegida>} />
                <Route path="produtos" element={<RotaProtegida permissoes={[P.produtosVer]}><PaginaProdutos /></RotaProtegida>} />
                <Route path="mesas" element={<RotaProtegida permissoes={[P.mesasVer, P.mesasGerir]}><PaginaMesas /></RotaProtegida>} />
                <Route path="caixa" element={<RotaProtegida permissoes={[P.caixaOperar, P.caixaVerTodas]}><PaginaCaixa /></RotaProtegida>} />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </AvisosProvider>
      </SessaoProvider>
    </LigacaoProvider>
  )
}
