import { useEffect, useRef, useState, type PointerEvent as EventoPonteiro } from 'react'
import { ArrowBigUp, ChevronDown, Delete } from 'lucide-react'
import { aoMudarModoTeclado, campoComTeclado, campoNumerico, ecraTactil, escreverNoCampo, lerModoTeclado, type ModoTeclado } from '../utils/teclado'

type Camada = 'letras' | 'acentos' | 'simbolos'

const LETRAS = ['1234567890', 'qwertyuiop', 'asdfghjklç', 'zxcvbnm']
const ACENTOS = ['1234567890', 'áàâãéêíóôõ', 'úüçÁÀÂÃÉÊ', 'ÍÓÔÕÚÇ']
const SIMBOLOS = ['1234567890', '-/:;()€$&@', '.,?!\'"%*+=', '#_<>[]{}']
const NUMEROS = ['789', '456', '123']

/**
 * Teclado no ecrã para postos tácteis sem teclado físico: sobe no fundo quando se toca num campo de texto
 * (pesquisa, motivos, comentários, nomes…) e mostra um teclado numérico nos campos de valores.
 * Modo por posto (Automático: só em ecrãs tácteis; Sempre; Nunca), escolhido no topo do ecrã.
 * As teclas não tiram o foco ao campo: escrevem nele como o teclado físico.
 */
export function TecladoEcra() {
  const [modo, setModo] = useState<ModoTeclado>(lerModoTeclado)
  const [campo, setCampo] = useState<HTMLInputElement | HTMLTextAreaElement | null>(null)
  const [camada, setCamada] = useState<Camada>('letras')
  const [maiusculas, setMaiusculas] = useState(false)
  const painel = useRef<HTMLDivElement>(null)

  useEffect(() => aoMudarModoTeclado(setModo), [])

  const activo = modo === 'sempre' || (modo === 'auto' && ecraTactil())

  useEffect(() => {
    if (!activo) {
      setCampo(null)
      return
    }
    const entrar = (e: FocusEvent) => {
      if (!campoComTeclado(e.target)) return
      setCampo(e.target)
      setCamada('letras')
      setMaiusculas(false)
      // O campo fica visível acima do teclado.
      window.setTimeout(() => (e.target as HTMLElement).scrollIntoView({ block: 'center', behavior: 'smooth' }), 50)
    }
    const sair = (e: FocusEvent) => {
      if (painel.current?.contains(e.relatedTarget as Node)) return
      window.setTimeout(() => {
        if (!campoComTeclado(document.activeElement)) setCampo(null)
      }, 60)
    }
    document.addEventListener('focusin', entrar)
    document.addEventListener('focusout', sair)
    if (campoComTeclado(document.activeElement)) setCampo(document.activeElement)
    return () => {
      document.removeEventListener('focusin', entrar)
      document.removeEventListener('focusout', sair)
    }
  }, [activo])

  // Espaço no fim da página para o teclado não tapar o conteúdo.
  useEffect(() => {
    if (!campo) return
    const altura = painel.current?.offsetHeight ?? 0
    document.body.style.paddingBottom = `${altura}px`
    // Os diálogos (Modal) sobem acima do teclado.
    document.documentElement.style.setProperty('--teclado-altura', `${altura}px`)
    return () => {
      document.body.style.paddingBottom = ''
      document.documentElement.style.removeProperty('--teclado-altura')
    }
  }, [campo, camada])

  if (!campo || !document.body.contains(campo)) return null
  const numerico = campoNumerico(campo)

  // Não deixar o toque tirar o foco ao campo.
  const manterFoco = (e: EventoPonteiro) => e.preventDefault()
  const escrever = (t: string) => {
    escreverNoCampo(campo, maiusculas && camada === 'letras' ? t.toUpperCase() : t)
    if (maiusculas && camada === 'letras') setMaiusculas(false)
  }
  const apagar = () => escreverNoCampo(campo, '', true)
  const fechar = () => {
    campo.blur()
    setCampo(null)
  }
  const tecla = 'flex min-h-12 min-w-0 flex-1 items-center justify-center rounded-lg bg-white text-lg font-medium shadow-sm ring-1 ring-slate-300 active:bg-primaria-100 select-none'
  const especial = 'flex min-h-12 items-center justify-center rounded-lg bg-slate-200 px-3 text-sm font-semibold shadow-sm ring-1 ring-slate-300 active:bg-slate-300 select-none'

  const linhas = camada === 'letras' ? LETRAS : camada === 'acentos' ? ACENTOS : SIMBOLOS
  const decimal = campo instanceof HTMLInputElement && campo.type === 'number' ? '.' : ','

  return (
    <div ref={painel} role="group" aria-label="Teclado no ecrã" data-teclado-ecra onPointerDown={manterFoco} onMouseDown={(e) => e.preventDefault()}
      className="fixed inset-x-0 bottom-0 z-[1000] border-t border-slate-300 bg-slate-100/95 p-2 shadow-[0_-4px_16px_rgba(0,0,0,0.15)] backdrop-blur print:hidden">
      {numerico ? (
        <div className="mx-auto grid max-w-md grid-cols-4 gap-1.5">
          {NUMEROS.map((l, i) => (
            <div key={l} className="col-span-3 grid grid-cols-3 gap-1.5" style={{ gridRow: i + 1 }}>
              {[...l].map((c) => <button key={c} type="button" tabIndex={-1} className={tecla} onClick={() => escrever(c)}>{c}</button>)}
            </div>
          ))}
          <button type="button" tabIndex={-1} className={`${especial} row-span-2`} style={{ gridColumn: 4, gridRow: '1 / span 2' }} onClick={apagar} aria-label="Apagar">
            <Delete className="h-6 w-6" aria-hidden />
          </button>
          <button type="button" tabIndex={-1} className={`${especial} row-span-2 bg-primaria-600 text-white`} style={{ gridColumn: 4, gridRow: '3 / span 2' }} onClick={fechar}>OK</button>
          <div className="col-span-3 grid grid-cols-3 gap-1.5" style={{ gridRow: 4 }}>
            <button type="button" tabIndex={-1} className={tecla} onClick={() => escrever('0')}>0</button>
            <button type="button" tabIndex={-1} className={tecla} onClick={() => escrever('00')}>00</button>
            <button type="button" tabIndex={-1} className={tecla} onClick={() => escrever(decimal)} aria-label="Vírgula decimal">,</button>
          </div>
        </div>
      ) : (
        <div className="mx-auto flex max-w-5xl flex-col gap-1.5">
          {linhas.map((l, i) => (
            <div key={l} className="flex gap-1.5">
              {i === 3 && camada === 'letras' && (
                <button type="button" tabIndex={-1} className={`${especial} ${maiusculas ? 'bg-primaria-200' : ''}`} onClick={() => setMaiusculas((m) => !m)}
                  aria-label="Maiúsculas" aria-pressed={maiusculas}>
                  <ArrowBigUp className="h-5 w-5" aria-hidden />
                </button>
              )}
              {[...l].map((c) => (
                <button key={c} type="button" tabIndex={-1} className={tecla} onClick={() => escrever(c)}>
                  {maiusculas && camada === 'letras' ? c.toUpperCase() : c}
                </button>
              ))}
              {i === 3 && (
                <button type="button" tabIndex={-1} className={especial} onClick={apagar} aria-label="Apagar">
                  <Delete className="h-5 w-5" aria-hidden />
                </button>
              )}
            </div>
          ))}
          <div className="flex gap-1.5">
            <button type="button" tabIndex={-1} className={especial} onClick={() => setCamada((c) => (c === 'simbolos' ? 'letras' : 'simbolos'))}>
              {camada === 'simbolos' ? 'abc' : '?123'}
            </button>
            <button type="button" tabIndex={-1} className={especial} onClick={() => setCamada((c) => (c === 'acentos' ? 'letras' : 'acentos'))}>
              {camada === 'acentos' ? 'abc' : 'áç'}
            </button>
            <button type="button" tabIndex={-1} className={`${tecla} flex-[5]`} onClick={() => escrever(' ')} aria-label="Espaço">espaço</button>
            {campo instanceof HTMLTextAreaElement && (
              <button type="button" tabIndex={-1} className={especial} onClick={() => escrever('\n')}>Linha</button>
            )}
            <button type="button" tabIndex={-1} className={`${especial} bg-primaria-600 px-6 text-white`} onClick={fechar}>OK</button>
            <button type="button" tabIndex={-1} className={especial} onClick={fechar} aria-label="Esconder o teclado">
              <ChevronDown className="h-5 w-5" aria-hidden />
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
