import { afterEach, describe, expect, it, vi } from 'vitest'
import { novaChaveIdempotencia } from './cliente'

describe('novaChaveIdempotencia', () => {
  afterEach(() => vi.restoreAllMocks())

  it('gera UUID v4 diferentes', () => {
    const a = novaChaveIdempotencia()
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(novaChaveIdempotencia()).not.toBe(a)
  })

  it('funciona sem crypto.randomUUID (postos em http://IP, fora de contexto seguro)', () => {
    vi.spyOn(crypto, 'randomUUID').mockImplementation(() => { throw new TypeError('crypto.randomUUID is not a function') })
    expect(novaChaveIdempotencia()).toHaveLength(36)
  })
})
