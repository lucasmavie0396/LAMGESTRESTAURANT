import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { RotaProtegida } from './components/RotaProtegida'
import { AvisosProvider } from './contexts/AvisosContext'
import { LigacaoProvider } from './contexts/LigacaoContext'
import { SessaoProvider } from './contexts/SessaoContext'
import { LayoutPrincipal } from './layouts/LayoutPrincipal'
import { PaginaCaixa } from './modules/caixa/PaginaCaixa'
import { PaginaClientes } from './modules/clientes/PaginaClientes'
import { PaginaCompras } from './modules/compras/PaginaCompras'
import { PaginaConfiguracoes } from './modules/configuracoes/PaginaConfiguracoes'
import { PaginaDespesas } from './modules/despesas/PaginaDespesas'
import { PaginaFacturacao } from './modules/facturacao/PaginaFacturacao'
import { PaginaFuncionarios } from './modules/pessoal/PaginaFuncionarios'
import { PaginaReservas } from './modules/reservas/PaginaReservas'
import { PaginaStock } from './modules/stock/PaginaStock'
import { PaginaCozinha } from './modules/cozinha/PaginaCozinha'
import { PaginaDelivery } from './modules/delivery/PaginaDelivery'
import { PaginaRelatorios } from './modules/relatorios/PaginaRelatorios'
import { PaginaPos } from './modules/pos/PaginaPos'
import { TempoRealProvider } from './contexts/TempoRealContext'
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
        <TempoRealProvider>
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
                <Route path="pos" element={<RotaProtegida permissoes={[P.posUsar]}><PaginaPos /></RotaProtegida>} />
                <Route path="cozinha" element={<RotaProtegida permissoes={['cozinha.ver']}><PaginaCozinha /></RotaProtegida>} />
                <Route path="delivery" element={<RotaProtegida permissoes={['delivery.gerir']}><PaginaDelivery /></RotaProtegida>} />
                <Route path="facturacao" element={<RotaProtegida permissoes={[P.facturacaoVer]}><PaginaFacturacao /></RotaProtegida>} />
                <Route path="relatorios" element={<RotaProtegida permissoes={['relatorios.vendas', 'relatorios.financeiros', 'relatorios.stock']}><PaginaRelatorios /></RotaProtegida>} />
                <Route path="stock" element={<RotaProtegida permissoes={['stock.ver', 'receitas.gerir']}><PaginaStock /></RotaProtegida>} />
                <Route path="compras" element={<RotaProtegida permissoes={['compras.ver']}><PaginaCompras /></RotaProtegida>} />
                <Route path="clientes" element={<RotaProtegida permissoes={['clientes.ver']}><PaginaClientes /></RotaProtegida>} />
                <Route path="reservas" element={<RotaProtegida permissoes={['reservas.gerir']}><PaginaReservas /></RotaProtegida>} />
                <Route path="despesas" element={<RotaProtegida permissoes={['despesas.registar']}><PaginaDespesas /></RotaProtegida>} />
                <Route path="funcionarios" element={<RotaProtegida permissoes={['funcionarios.ver']}><PaginaFuncionarios /></RotaProtegida>} />
                <Route path="configuracoes" element={<RotaProtegida permissoes={['configuracoes.gerir']}><PaginaConfiguracoes /></RotaProtegida>} />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </AvisosProvider>
        </TempoRealProvider>
      </SessaoProvider>
    </LigacaoProvider>
  )
}
