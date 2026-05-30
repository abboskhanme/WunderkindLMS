import type { InputHTMLAttributes, SelectHTMLAttributes, TextareaHTMLAttributes, ReactNode } from 'react'
import { cn } from '@/lib/utils'

const base =
  'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

interface FieldWrap {
  label?: string
  required?: boolean
}

function Label({ label, required, children }: FieldWrap & { children: ReactNode }) {
  return (
    <label className="block">
      {label && (
        <span className="mb-1 block text-sm font-medium text-slate-600">
          {label}
          {required && <span className="text-red-500"> *</span>}
        </span>
      )}
      {children}
    </label>
  )
}

interface InputProps extends InputHTMLAttributes<HTMLInputElement>, FieldWrap {}

export function Input({ label, required, className, ...rest }: InputProps) {
  return (
    <Label label={label} required={required}>
      <input className={cn(base, className)} {...rest} />
    </Label>
  )
}

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement>, FieldWrap {}

export function Select({ label, required, className, children, ...rest }: SelectProps) {
  return (
    <Label label={label} required={required}>
      <select className={cn(base, 'bg-white', className)} {...rest}>
        {children}
      </select>
    </Label>
  )
}

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement>, FieldWrap {}

export function Textarea({ label, required, className, ...rest }: TextareaProps) {
  return (
    <Label label={label} required={required}>
      <textarea className={cn(base, 'resize-none', className)} {...rest} />
    </Label>
  )
}

const selectBase =
  'rounded-lg border border-slate-200 bg-white px-2 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'))
const MINUTES = Array.from({ length: 60 }, (_, i) => String(i).padStart(2, '0'))

interface Time24InputProps {
  value: string
  onChange: (value: string) => void
  className?: string
  disabled?: boolean
}

/** 24 soatlik vaqt tanlash: ikkita <select> (00-23 soat, 00-59 daqiqa). */
export function Time24Input({ value, onChange, className, disabled }: Time24InputProps) {
  const [hh, mm] = value ? value.split(':') : ['', '']

  const emit = (newH: string, newM: string) => {
    if (!newH && !newM) { onChange(''); return }
    onChange(`${(newH || '00').padStart(2, '0')}:${(newM || '00').padStart(2, '0')}`)
  }

  return (
    <div className={cn('inline-flex items-center gap-1', className)}>
      <select value={hh} disabled={disabled} onChange={(e) => emit(e.target.value, mm ?? '')} className={selectBase}>
        <option value="">--</option>
        {HOURS.map((h) => <option key={h} value={h}>{h}</option>)}
      </select>
      <span className="font-semibold text-slate-400">:</span>
      <select value={mm} disabled={disabled} onChange={(e) => emit(hh ?? '', e.target.value)} className={selectBase}>
        <option value="">--</option>
        {MINUTES.map((m) => <option key={m} value={m}>{m}</option>)}
      </select>
    </div>
  )
}
