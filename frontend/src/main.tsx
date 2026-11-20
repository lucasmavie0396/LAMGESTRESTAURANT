import './polyfills'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { PaginaVisor } from './modules/visor/PaginaVisor'
import { aplicarLoginTactilDoEndereco, associarPostoDoEndereco } from './utils/terminalLocal'
import { associarPda, emPda } from './utils/pda'
import { aplicarEcraPosto } from './utils/ecraPosto'
import { aplicarTema } from './utils/tema'
import { idiomaActual } from './i18n/idioma'
import { ligarTradutor } from './i18n/tradutor'

const raiz = createRoot(document.getElementById('root')!)
// Em Inglês, o dicionário carrega antes de desenhar (em Português não se carrega nada).
const idioma = idiomaActual === 'en'
  ? import('./i18n/en.json').then((m) => { ligarTradutor(m.default) }).catch(() => undefined)
  : Promise.resolve()
void idioma.then(arrancar)

function arrancar() {
  if (window.location.pathname === '/visor') {
    // Visor do cliente: página à parte, sem sessão nem associação do posto.
    raiz.render(<StrictMode><PaginaVisor /></StrictMode>)
  } else {
    // Tema claro/escuro escolhido neste computador, antes de desenhar (sem piscar). O visor do cliente tem as suas cores.
    aplicarTema()
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
}
