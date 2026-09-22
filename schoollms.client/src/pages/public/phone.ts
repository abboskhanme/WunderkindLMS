/**
 * Phone helpers for the public enrolment form (§6.1).
 *
 * The parent types nine digits; `+998` is printed beside the field and never
 * typed, so it can neither be deleted nor doubled. Kept apart from
 * `SurveyFields.tsx` because that file exports components only.
 */

/** Digits only, at most the 9 that follow +998. */
export function phoneDigits(raw: string): string {
  return raw.replace(/\D/g, '').slice(0, 9)
}

/** `901234567` → `90 123 45 67`. */
export function formatPhone(digits: string): string {
  const d = phoneDigits(digits)
  return [d.slice(0, 2), d.slice(2, 5), d.slice(5, 7), d.slice(7, 9)].filter(Boolean).join(' ')
}

/** What is sent to the server — the number as the parent sees it (§2.4: stored as typed). */
export function fullPhone(digits: string): string {
  return `+998 ${formatPhone(digits)}`.trim()
}
