import { Camera } from 'lucide-react'
import { lerComCamara, temCamara } from '../utils/pda'

/**
 * Botão «Ler com a câmara» (QR code ou código de barras) da app Android: abre o leitor da app e devolve o texto lido.
 * No computador (sem a app) não aparece.
 */
export function BotaoCamara({ aoLer, rotulo = 'Ler código com a câmara', desactivado, compacto }: {
  aoLer: (codigo: string) => void
  rotulo?: string
  desactivado?: boolean
  /** Só o ícone (ao lado de campos de pesquisa). */
  compacto?: boolean
}) {
  if (!temCamara()) return null
  return (
    <button type="button" className={compacto ? 'btn-icone shrink-0' : 'btn-secundario shrink-0'} disabled={desactivado} aria-label={rotulo} title={rotulo}
      onClick={() => void lerComCamara().then((codigo) => aoLer(codigo.trim()))}>
      <Camera className="h-5 w-5" aria-hidden />{!compacto && <span className="hidden sm:inline">Câmara</span>}
    </button>
  )
}
