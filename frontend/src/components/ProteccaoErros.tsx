import { Component, type ErrorInfo, type ReactNode } from 'react'
import { AlertTriangle, RefreshCw } from 'lucide-react'
import { reportarErro } from '../api/errosEcra'

interface Props {
  children: ReactNode
  /** Onde está (aparece no log do servidor), ex.: «página» ou «aplicação». */
  origem: string
}

/**
 * Um erro num componente não deixa o ecrã em branco: mostra uma mensagem com «Recarregar» e regista o erro no
 * servidor. Na área das páginas, mudar de página limpa o erro (o resto da aplicação continua a funcionar).
 */
export class ProteccaoErros extends Component<Props, { erro: Error | null }> {
  state = { erro: null as Error | null }

  static getDerivedStateFromError(erro: Error) {
    return { erro }
  }

  componentDidCatch(erro: Error, info: ErrorInfo) {
    const comComponentes = new Error(erro.message)
    comComponentes.name = erro.name
    comComponentes.stack = `${erro.stack ?? ''}\nComponentes:${info.componentStack ?? ''}`
    reportarErro(comComponentes, this.props.origem)
  }

  render() {
    if (!this.state.erro) return this.props.children
    return (
      <div role="alert" className="mx-auto mt-8 max-w-lg space-y-3 rounded-xl border border-red-200 bg-red-50 p-6 text-center text-red-900">
        <AlertTriangle className="mx-auto h-10 w-10 text-red-600" aria-hidden />
        <h2 className="text-lg font-semibold">Ocorreu um erro neste ecrã</h2>
        <p className="text-sm">O erro foi registado no servidor. Os dados já gravados não se perderam. Recarregue o ecrã para continuar.</p>
        <button type="button" className="btn-primario mx-auto" onClick={() => location.reload()}>
          <RefreshCw className="h-4 w-4" aria-hidden /> Recarregar o ecrã
        </button>
      </div>
    )
  }
}
