import './polyfills'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { aplicarLoginTactilDoEndereco, associarPostoDoEndereco } from './utils/terminalLocal'
import { associarPda } from './utils/pda'

// O atalho do posto (?posto=N&tactil=1) é resolvido antes de a aplicação arrancar, para o caixa já abrir com o terminal certo.
associarPda()
aplicarLoginTactilDoEndereco()
void associarPostoDoEndereco().finally(() =>
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
)
