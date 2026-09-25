import { useLayoutEffect, useRef, type ChangeEvent } from 'react'
import { cn } from '@/lib/utils'
import { localDigits, toPhoneValue } from '@/lib/phone'

interface PhoneInputProps {
  label?: string
  required?: boolean
  /** Any stored spelling; shown as `+998 97 666 66 66`. */
  value: string | null | undefined
  /** Always `+998 XX XXX XX XX` (possibly partial), or `''` when cleared. */
  onChange: (value: string) => void
  disabled?: boolean
  autoFocus?: boolean
  className?: string
}

/** Local part grouped as `97 666 66 66`. */
function group(d: string): string {
  return [d.slice(0, 2), d.slice(2, 5), d.slice(5, 7), d.slice(7, 9)].filter(Boolean).join(' ')
}

/** Caret index in `group(d)` that sits right after the n-th digit. */
function caretAfterDigits(formatted: string, n: number): number {
  if (n <= 0) return 0
  let seen = 0
  for (let i = 0; i < formatted.length; i++) {
    if (/\d/.test(formatted[i]) && ++seen === n) return i + 1
  }
  return formatted.length
}

/**
 * Uzbek phone field: `+998` is printed beside the input and never typed, the
 * nine local digits are grouped as they are typed. Looks like `Input`.
 */
export function PhoneInput({ label, required, value, onChange, disabled, autoFocus, className }: PhoneInputProps) {
  const ref = useRef<HTMLInputElement>(null)
  const pendingCaret = useRef<number | null>(null)
  const digits = localDigits(value)
  const shown = group(digits)

  useLayoutEffect(() => {
    if (pendingCaret.current === null || !ref.current) return
    const pos = caretAfterDigits(shown, pendingCaret.current)
    ref.current.setSelectionRange(pos, pos)
    pendingCaret.current = null
  }, [shown])

  const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value
    const caret = e.target.selectionStart ?? raw.length
    const rawDigits = raw.replace(/\D/g, '')
    let before = raw.slice(0, caret).replace(/\D/g, '').length
    if (rawDigits.length >= 12 && rawDigits.startsWith('998')) before = Math.max(0, before - 3)

    let next = localDigits(raw)
    // Deleting only a space would leave the digits unchanged — take the digit beside it.
    if (next === digits && raw.length < shown.length) {
      const forward = (e.nativeEvent as InputEvent).inputType === 'deleteContentForward'
      const at = forward ? before : before - 1
      if (at >= 0 && at < next.length) {
        next = next.slice(0, at) + next.slice(at + 1)
        before = at
      }
    }
    pendingCaret.current = Math.min(before, next.length)
    onChange(toPhoneValue(next))
  }

  const field = (
    <div
      className={cn(
        'flex w-full items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm transition-colors focus-within:border-brand-400 focus-within:ring-2 focus-within:ring-brand-100',
        disabled && 'bg-slate-50',
        className,
      )}
    >
      <span className="shrink-0 select-none text-slate-400">+998</span>
      <input
        ref={ref}
        type="tel"
        inputMode="tel"
        autoComplete="tel-national"
        placeholder="90 123 45 67"
        value={shown}
        onChange={handleChange}
        disabled={disabled}
        autoFocus={autoFocus}
        className="w-full min-w-0 border-0 bg-transparent p-0 text-sm text-slate-800 outline-none placeholder:text-slate-300 disabled:text-slate-500"
      />
    </div>
  )

  if (!label) return field
  return (
    <label className="block">
      <span className="mb-1 block text-sm font-medium text-slate-600">
        {label}
        {required && <span className="text-red-500"> *</span>}
      </span>
      {field}
    </label>
  )
}
