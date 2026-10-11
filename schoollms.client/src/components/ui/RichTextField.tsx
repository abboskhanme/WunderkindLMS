/**
 * Formatlanadigan matn maydoni: yorliq, formatlash tugmalari, matn va (matn bo'lsa) namuna —
 * Telegramda qanday ko'rinishi. E'lon, ro'yxatdan xabar va yangilik oynalarida bir xil (2026-10-11).
 */
import { useRef } from 'react'
import { FormatToolbar } from './FormatToolbar'
import { RichText } from './RichText'

export function RichTextField({
  label,
  value,
  onChange,
  rows = 5,
  placeholder,
  preview = true,
  disabled = false,
  required = false,
}: {
  label?: string
  value: string
  onChange: (next: string) => void
  rows?: number
  placeholder?: string
  /** Namuna ko'rsatilsinmi (matnda belgi bo'lsa). */
  preview?: boolean
  disabled?: boolean
  required?: boolean
}) {
  const ref = useRef<HTMLTextAreaElement>(null)
  const formatted = /\*\*|__|\+\+|~~|\|\||`|\]\(|^> /m.test(value)
  return (
    <div>
      {label && (
        <span className="mb-1 block text-sm font-medium text-slate-600">
          {label}
          {required && <span className="text-red-500"> *</span>}
        </span>
      )}
      {!disabled && <FormatToolbar textareaRef={ref} value={value} onChange={onChange} className="mb-1.5" />}
      <textarea
        ref={ref}
        rows={rows}
        value={value}
        disabled={disabled}
        required={required}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        className="w-full resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
      />
      {preview && formatted && (
        <div className="mt-2 rounded-lg bg-slate-50 px-3 py-2">
          <p className="mb-1 text-xs font-medium text-slate-400">Telegramda shunday ko'rinadi:</p>
          <RichText markup={value} className="text-sm text-slate-700" />
        </div>
      )}
    </div>
  )
}
