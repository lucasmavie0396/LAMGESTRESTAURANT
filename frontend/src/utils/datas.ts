// Datas no fuso de Maputo (UTC+2, sem hora de Verão). O servidor guarda UTC e interpreta datas locais no fuso configurado.

const FUSO = 'Africa/Maputo'

/** Hoje (data local) no formato AAAA-MM-DD. */
export function hojeIso() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: FUSO }).format(new Date())
}

/**
 * Dia comercial de hoje (AAAA-MM-DD): com o dia a terminar às `horasFim` (ex.: 6), de madrugada antes dessa hora
 * ainda é o dia anterior. Com 0 é o mesmo que `hojeIso`.
 */
export function hojeComercialIso(horasFim = 0, agora = new Date()) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: FUSO }).format(new Date(agora.getTime() - horasFim * 3_600_000))
}

export function somarDias(iso: string, dias: number) {
  const d = new Date(`${iso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + dias)
  return d.toISOString().slice(0, 10)
}

/** Data AAAA-MM-DD → dd/mm/aaaa. */
export function dataCurta(iso: string) {
  return new Intl.DateTimeFormat('pt-MZ', { timeZone: 'UTC' }).format(new Date(`${iso.slice(0, 10)}T00:00:00Z`))
}

/** Hora local (HH:MM) de um instante UTC. */
export function horaLocal(isoUtc: string) {
  return new Intl.DateTimeFormat('pt-MZ', { hour: '2-digit', minute: '2-digit', timeZone: FUSO }).format(new Date(isoUtc))
}

/** Data e hora locais (de um <input type="date"> e <input type="time">) → ISO UTC. Maputo = UTC+2. */
export function localParaUtc(data: string, hora: string) {
  return new Date(`${data}T${hora}:00+02:00`).toISOString()
}

/** Instante UTC → { data, hora } locais para preencher inputs. */
export function utcParaLocal(isoUtc: string) {
  const partes = new Intl.DateTimeFormat('en-CA', { timeZone: FUSO, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
    .formatToParts(new Date(isoUtc))
  const p = (t: string) => partes.find((x) => x.type === t)?.value ?? '00'
  return { data: `${p('year')}-${p('month')}-${p('day')}`, hora: `${p('hour')}:${p('minute')}` }
}
