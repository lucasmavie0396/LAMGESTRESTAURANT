import './polyfills'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { PaginaVisor } from './modules/visor/PaginaVisor'
import { aplicarLoginTactilDoEndereco, associarPostoDoEndereco } from './utils/terminalLocal'
import { associarPda, emPda } from './utils/pda'
import { aplicarEcraPosto } from './utils/ecraPosto'

const raiz = createRoot(document.getElementById('root')!)
if (window.location.pathname === '/visor') {
  // Visor do cliente: página à parte, sem sessão nem associação do posto.
  raiz.render(<StrictMode><PaginaVisor /></StrictMode>)
} else {
  // O atalho do posto (?posto=N&tactil=1) é resolvido antes de a aplicação arrancar, para o caixa já abrir com o terminal certo.
  associarPda()
  aplicarLoginTactilDoEndereco()
  // Ecrã do posto (modo táctil e tamanho), guardado do último sinal de presença: aplica-se já, antes do login. Os PDAs têm o seu próprio ecrã.
  if (!emPda()) aplicarEcraPosto()
  void associarPostoDoEndereco().finally(() =>
    raiz.render(
      <StrictMode>
        <App />
      </StrictMode>,
    ),
  )
}
