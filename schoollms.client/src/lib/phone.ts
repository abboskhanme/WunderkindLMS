/**
 * Uzbek phone numbers — the only kind the school deals with.
 *
 * Canonical form, both typed and shown: `+998 97 666 66 66`. The server keys
 * phones by their last nine digits (`PhoneUtil.Key`), so the spaces are free.
 */

const COUNTRY = '998'

/**
 * Whether `raw` carries the country code: written out as `+998…` (any length —
 * a half-typed `+998 90 12` included), or 12+ bare digits starting with `998`.
 * Bare `998…` shorter than that is a local number — `99 812 34 56` is valid.
 */
export function hasCountryCode(raw: string | null | undefined): boolean {
  const s = raw ?? ''
  if (/^\s*\+\s*998/.test(s)) return true
  const d = s.replace(/\D/g, '')
  return d.length >= 12 && d.startsWith(COUNTRY)
}

/**
 * The local nine digits of any spelling: `+998 97 666 66 66`, `998976666666`,
 * `97 666-66-66`.
 *
 * ⚠️ The `+` matters: `toPhoneValue` stores a PARTIAL number as `+998 90 12`,
 * and reading that back must give `9012`, not `998901` — otherwise every
 * keystroke in `PhoneInput` rewrote the number and a digit could not be deleted.
 */
export function localDigits(raw: string | null | undefined): string {
  let d = (raw ?? '').replace(/\D/g, '')
  if (hasCountryCode(raw)) d = d.slice(COUNTRY.length)
  return d.slice(0, 9)
}

/** `976666666` → `97 666 66 66`; partial input is grouped as far as it goes. */
function groupLocal(d: string): string {
  return [d.slice(0, 2), d.slice(2, 5), d.slice(5, 7), d.slice(7, 9)].filter(Boolean).join(' ')
}

/** Value an input stores: `+998 97 666 66 66`, or `''` when nothing is typed. */
export function toPhoneValue(raw: string | null | undefined): string {
  const d = localDigits(raw)
  return d ? `+${COUNTRY} ${groupLocal(d)}` : ''
}

/**
 * For display. A recognisably Uzbek number (9 local digits, or 12 with the
 * country code) is printed as `+998 97 666 66 66`; anything else — a legacy
 * or foreign entry — is shown exactly as stored rather than mangled.
 */
export function formatPhone(raw: string | null | undefined): string {
  const s = (raw ?? '').trim()
  const d = s.replace(/\D/g, '')
  if (d.length === 9 || (d.length === 12 && d.startsWith(COUNTRY))) return toPhoneValue(d)
  return s
}

/** `tel:` link target: `+998976666666`. */
export function phoneHref(raw: string | null | undefined): string {
  const d = localDigits(raw)
  return d.length === 9 ? `tel:+${COUNTRY}${d}` : `tel:${(raw ?? '').replace(/[^\d+]/g, '')}`
}
