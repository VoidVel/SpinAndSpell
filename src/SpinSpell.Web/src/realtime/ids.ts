/**
 * A UUID that works on a phone.
 *
 * `crypto.randomUUID()` is secure-context only — it exists on localhost and over
 * HTTPS, and is `undefined` on a plain-http LAN address. Which is exactly how
 * this app gets playtested: everyone's phone pointed at http://192.168.x.x:5173.
 * Calling it there throws inside the submit handler and the question silently
 * never sends.
 *
 * `crypto.getRandomValues` has no such restriction, so build the v4 by hand.
 */
export function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  const bytes = new Uint8Array(16)

  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes)
  } else {
    for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256)
  }

  bytes[6] = (bytes[6] & 0x0f) | 0x40 // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80 // variant 10

  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
