import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { associarPostoDoEndereco } from './utils/terminalLocal'

// O atalho do posto (?posto=N) é resolvido antes de a aplicação arrancar, para o caixa já abrir com o terminal certo.
void associarPostoDoEndereco().finally(() =>
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
)
