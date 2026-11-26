import { useEffect, useRef, useState } from 'react'
import { Camera, Loader2, Nfc, Settings } from 'lucide-react'
import { Alerta, Modal } from './ui'
import { definicoesNfc, lerComCamara, lerNfc, temCamara, temNfc, type EstadoNfc } from '../utils/pda'

/**
 * Botões «Câmara» (QR code ou código de barras) e «NFC» (chip dos cartões sem contacto) da app Android, para ler o número
 * de um Cartão Evento. No computador (sem a app) não aparecem: aí usa-se o leitor de cartões ou de códigos.
 */
export function LeituraCartaoTelemovel({ aoLer, desactivado }: { aoLer: (numero: string) => void; desactivado?: boolean }) {
  const [nfcAberto, setNfcAberto] = useState(false)
  const camara = temCamara()
  const nfc = temNfc()
  if (!camara && !nfc) return null
  return (
    <>
      {camara && (
        <button type="button" className="btn-secundario shrink-0" disabled={desactivado} aria-label="Ler o código do cartão com a câmara" title="Ler o código do cartão com a câmara"
          onClick={() => void lerComCamara().then((c) => { if (c.trim()) aoLer(c.trim()) })}>
          <Camera className="h-5 w-5" aria-hidden /><span className="hidden sm:inline">Câmara</span>
        </button>
      )}
      {nfc && (
        <button type="button" className="btn-secundario shrink-0" disabled={desactivado} aria-label="Ler o cartão sem contacto (NFC)" title="Ler o cartão sem contacto (NFC)"
          onClick={() => setNfcAberto(true)}>
          <Nfc className="h-5 w-5" aria-hidden /><span className="hidden sm:inline">NFC</span>
        </button>
      )}
      {nfcAberto && <EsperaNfc aoFechar={() => setNfcAberto(false)} aoLer={(n) => { setNfcAberto(false); aoLer(n) }} />}
    </>
  )
}

/** Janela «Aproxime o cartão»: liga o leitor NFC enquanto está aberta. */
export function EsperaNfc({ aoLer, aoFechar }: { aoLer: (numero: string) => void; aoFechar: () => void }) {
  const [estado, setEstado] = useState<EstadoNfc>('a-ler')
  const [tentativa, setTentativa] = useState(0)
  const aoLerRef = useRef(aoLer)
  useEffect(() => { aoLerRef.current = aoLer })

  useEffect(() => {
    const leitura = lerNfc()
    setEstado(leitura.estado)
    let activa = true
    void leitura.lido.then((n) => { if (activa && n) aoLerRef.current(n) })
    return () => {
      activa = false
      leitura.cancelar()
    }
  }, [tentativa])

  return (
    <Modal titulo="Ler cartão sem contacto" aoFechar={aoFechar}
      rodape={<>
        {estado === 'desligado' && <button type="button" className="btn-primario" onClick={() => setTentativa((t) => t + 1)}>Tentar de novo</button>}
        <button type="button" className="btn-secundario" onClick={aoFechar}>Cancelar</button>
      </>}>
      {estado === 'a-ler' && (
        <div className="flex flex-col items-center gap-3 py-4 text-center" role="status">
          <Nfc className="h-14 w-14 text-primaria-600" aria-hidden />
          <p className="text-lg font-medium">Encoste o cartão às costas do telemóvel</p>
          <p className="flex items-center gap-2 text-sm text-slate-500"><Loader2 className="h-4 w-4 animate-spin" aria-hidden /> À espera do cartão…</p>
          <p className="text-xs text-slate-500">Só lê cartões com chip (sem contacto). Cartões só com banda magnética precisam de um leitor de cartões.</p>
        </div>
      )}
      {estado === 'desligado' && (
        <div className="space-y-3">
          <Alerta tipo="aviso">O NFC deste telemóvel está desligado. Ligue-o nas definições do Android e carregue em «Tentar de novo».</Alerta>
          <button type="button" className="btn-secundario" onClick={() => definicoesNfc()}>
            <Settings className="h-4 w-4" aria-hidden /> Abrir definições do NFC
          </button>
        </div>
      )}
      {estado === 'sem-nfc' && <Alerta tipo="erro">Este telemóvel não tem leitor NFC. Use a câmara (código impresso no cartão) ou um leitor de cartões.</Alerta>}
    </Modal>
  )
}
