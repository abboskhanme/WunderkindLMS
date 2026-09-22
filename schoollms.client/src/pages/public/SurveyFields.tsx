/**
 * Form controls for the public enrolment page (§6.1).
 *
 * They are local on purpose and they are the only place in the app that needs
 * to be: `components/ui/Input` renders at `text-sm` (14 px) and iOS Safari zooms
 * into any field under 16 px on focus. §6.1 asks for a 16 px base font for
 * exactly that reason, so these controls repeat the shared visual language —
 * same radii, same slate border, same `brand` focus ring — one size up, with a
 * touch target that works with a thumb.
 */
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { useId } from 'react'
import { cn } from '@/lib/utils'
import { formatPhone, phoneDigits } from './phone'

/**
 * Border and ring stay out of the base string: `cn` only concatenates, so two
 * competing `border-*` classes would be resolved by stylesheet order, not by
 * the order they are written here.
 */
const controlBase =
  'w-full rounded-xl border bg-white px-3.5 py-3 text-base text-slate-800 outline-none transition-colors placeholder:text-slate-400'
const controlIdle = 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100'
const controlInvalid = 'border-rose-300 focus:border-rose-400 focus:ring-2 focus:ring-rose-100'

function controlClass(invalid: boolean): string {
  return cn(controlBase, invalid ? controlInvalid : controlIdle)
}

interface FieldProps {
  id: string
  label: string
  required?: boolean
  error?: string
  hint?: string
  children: ReactNode
}

/** Label, control, error line — one layout for every field on the page. */
function Field({ id, label, required, error, hint, children }: FieldProps) {
  return (
    <div>
      <label htmlFor={id} className="mb-1.5 block text-sm font-medium text-slate-600">
        {label}
        {required && <span className="text-rose-500"> *</span>}
      </label>
      {children}
      {hint && !error && <p className="mt-1.5 text-xs text-slate-400">{hint}</p>}
      {error && (
        <p id={`${id}-error`} className="mt-1.5 text-sm text-rose-600">
          {error}
        </p>
      )}
    </div>
  )
}

interface TextFieldProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id' | 'className' | 'value' | 'onChange'> {
  label: string
  value: string
  onValueChange: (value: string) => void
  error?: string
}

export function TextField({ label, value, onValueChange, error, required, ...rest }: TextFieldProps) {
  const id = useId()
  return (
    <Field id={id} label={label} required={required} error={error}>
      <input
        id={id}
        value={value}
        onChange={(e) => onValueChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        className={controlClass(Boolean(error))}
        {...rest}
      />
    </Field>
  )
}

interface PhoneFieldProps {
  label: string
  /** Local part only: up to 9 digits, no prefix. */
  digits: string
  onDigitsChange: (digits: string) => void
  required?: boolean
  error?: string
  hint?: string
  autoComplete?: string
}

/** `+998` is printed, never typed: nine digits is the whole input (§6.1). */
export function PhoneField({
  label,
  digits,
  onDigitsChange,
  required,
  error,
  hint,
  autoComplete,
}: PhoneFieldProps) {
  const id = useId()
  const invalid = Boolean(error)
  return (
    <Field id={id} label={label} required={required} error={error} hint={hint}>
      <div
        className={cn(
          'flex items-center gap-2 rounded-xl border bg-white px-3.5 py-3 transition-colors',
          invalid
            ? 'border-rose-300 focus-within:border-rose-400 focus-within:ring-2 focus-within:ring-rose-100'
            : 'border-slate-200 focus-within:border-brand-400 focus-within:ring-2 focus-within:ring-brand-100',
        )}
      >
        <span className="shrink-0 select-none text-base text-slate-400">+998</span>
        <input
          id={id}
          type="tel"
          inputMode="tel"
          autoComplete={autoComplete}
          placeholder="90 123 45 67"
          value={formatPhone(digits)}
          onChange={(e) => onDigitsChange(phoneDigits(e.target.value))}
          aria-invalid={invalid ? true : undefined}
          aria-describedby={invalid ? `${id}-error` : undefined}
          className="w-full min-w-0 border-0 bg-transparent p-0 text-base text-slate-800 outline-none placeholder:text-slate-300"
        />
      </div>
    </Field>
  )
}

interface SelectFieldProps
  extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id' | 'className' | 'value' | 'onChange'> {
  label: string
  value: string
  onValueChange: (value: string) => void
  error?: string
  children: ReactNode
}

export function SelectField({
  label,
  value,
  onValueChange,
  error,
  required,
  children,
  ...rest
}: SelectFieldProps) {
  const id = useId()
  return (
    <Field id={id} label={label} required={required} error={error}>
      <select
        id={id}
        value={value}
        onChange={(e) => onValueChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        className={controlClass(Boolean(error))}
        {...rest}
      >
        {children}
      </select>
    </Field>
  )
}

interface RadioPillsProps<T extends string> {
  label: string
  name: string
  value: T | ''
  options: ReadonlyArray<{ value: T; label: string }>
  onValueChange: (value: T) => void
  required?: boolean
  error?: string
}

/** Two pills, one real radio group underneath — tappable, and still keyboard-navigable. */
export function RadioPills<T extends string>({
  label,
  name,
  value,
  options,
  onValueChange,
  required,
  error,
}: RadioPillsProps<T>) {
  const id = useId()
  return (
    <fieldset>
      <legend className="mb-1.5 block text-sm font-medium text-slate-600">
        {label}
        {required && <span className="text-rose-500"> *</span>}
      </legend>
      <div className="grid grid-cols-2 gap-2">
        {options.map((option) => {
          const selected = value === option.value
          return (
            <label
              key={option.value}
              className={cn(
                'flex cursor-pointer items-center justify-center rounded-xl border px-3 py-3 text-base transition-colors',
                selected
                  ? 'border-brand-500 bg-brand-50 font-medium text-brand-700'
                  : error
                    ? 'border-rose-300 bg-white text-slate-600'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              <input
                type="radio"
                name={name}
                value={option.value}
                checked={selected}
                onChange={() => onValueChange(option.value)}
                aria-describedby={error ? `${id}-error` : undefined}
                className="sr-only"
              />
              {option.label}
            </label>
          )
        })}
      </div>
      {error && (
        <p id={`${id}-error`} className="mt-1.5 text-sm text-rose-600">
          {error}
        </p>
      )}
    </fieldset>
  )
}

interface HoneypotProps {
  value: string
  onValueChange: (value: string) => void
}

/**
 * The §2.5 honeypot. Named `website`, moved off-screen rather than hidden with
 * `display:none` — the cheapest bots skip fields that are display-none, and fill
 * in everything else they find. Nothing a human can reach: no tab stop, no
 * autofill, hidden from the accessibility tree.
 */
export function Honeypot({ value, onValueChange }: HoneypotProps) {
  return (
    <div aria-hidden="true" className="absolute -left-[9999px] h-px w-px overflow-hidden">
      <input
        type="text"
        name="website"
        value={value}
        onChange={(e) => onValueChange(e.target.value)}
        tabIndex={-1}
        autoComplete="off"
      />
    </div>
  )
}
