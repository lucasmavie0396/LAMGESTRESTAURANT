// A app de venda corre no WebView do PDA, por vezes bem mais antigo do que os navegadores dos postos.
if (typeof AbortSignal !== 'undefined' && !('timeout' in AbortSignal)) {
  Object.defineProperty(AbortSignal, 'timeout', {
    value: (ms: number) => {
      const controlo = new AbortController()
      setTimeout(() => controlo.abort(), ms)
      return controlo.signal
    },
  })
}
