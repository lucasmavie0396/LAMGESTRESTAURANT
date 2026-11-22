import { TecladoEcra } from './components/TecladoEcra'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { RotaProtegida } from './components/RotaProtegida'
import { AvisosProvider } from './contexts/AvisosContext'
import { LigacaoProvider } from './contexts/LigacaoContext'
import { SessaoProvider } from './contexts/SessaoContext'
import { LayoutPrincipal } from './layouts/LayoutPrincipal'
import { TempoRealProvider } from './contexts/TempoRealContext'
import { AlterarPalavraPasse } from './pages/AlterarPalavraPasse'
import { Login } from './pages/Login'
import { P } from './utils/permissoes'
import {
  PaginaAuditoria,
  PaginaCartoes,
  PaginaCaixa,
  PaginaClientes,
  PaginaCompras,
  PaginaConfiguracoes,
  PaginaDespesas,
  PaginaFacturacao,
  PaginaFuncionarios,
  PaginaReservas,
  PaginaStock,
  PaginaCozinha,
  PaginaDelivery,
  PaginaRelatorios,
  PaginaPos,
  PaginaMesas,
  PaginaProdutos,
  PaginaPerfis,
  PaginaUtilizadores,
  Inicio,
} from './paginasApp'

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
                <Route path="caixa" element={<RotaProtegida permissoes={[P.caixaOperar, P.caixaVerTodas, P.caixaDia]}><PaginaCaixa /></RotaProtegida>} />
                <Route path="pos" element={<RotaProtegida permissoes={[P.posUsar]}><PaginaPos /></RotaProtegida>} />
                <Route path="cozinha" element={<RotaProtegida permissoes={['cozinha.ver']}><PaginaCozinha /></RotaProtegida>} />
                <Route path="delivery" element={<RotaProtegida permissoes={['delivery.gerir']}><PaginaDelivery /></RotaProtegida>} />
                <Route path="cartoes" element={<RotaProtegida permissoes={[P.cartoesVer]}><PaginaCartoes /></RotaProtegida>} />
                <Route path="facturacao" element={<RotaProtegida permissoes={[P.facturacaoVer]}><PaginaFacturacao /></RotaProtegida>} />
                <Route path="relatorios" element={<RotaProtegida permissoes={['relatorios.vendas', 'relatorios.financeiros', 'relatorios.stock', 'facturacao.ver']}><PaginaRelatorios /></RotaProtegida>} />
                <Route path="stock" element={<RotaProtegida permissoes={['stock.ver', 'receitas.gerir']}><PaginaStock /></RotaProtegida>} />
                <Route path="compras" element={<RotaProtegida permissoes={['compras.ver']}><PaginaCompras /></RotaProtegida>} />
                <Route path="clientes" element={<RotaProtegida permissoes={['clientes.ver']}><PaginaClientes /></RotaProtegida>} />
                <Route path="reservas" element={<RotaProtegida permissoes={['reservas.gerir']}><PaginaReservas /></RotaProtegida>} />
                <Route path="despesas" element={<RotaProtegida permissoes={['despesas.registar']}><PaginaDespesas /></RotaProtegida>} />
                <Route path="funcionarios" element={<RotaProtegida permissoes={['funcionarios.ver']}><PaginaFuncionarios /></RotaProtegida>} />
                <Route path="auditoria" element={<RotaProtegida permissoes={['auditoria.ver']}><PaginaAuditoria /></RotaProtegida>} />
                <Route path="configuracoes" element={<RotaProtegida permissoes={['configuracoes.gerir']}><PaginaConfiguracoes /></RotaProtegida>} />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
            <TecladoEcra />
          </BrowserRouter>
        </AvisosProvider>
        </TempoRealProvider>
      </SessaoProvider>
    </LigacaoProvider>
  )
}
