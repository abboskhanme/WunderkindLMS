import type { LucideIcon } from 'lucide-react'
import { Card } from './Card'

interface StatCardProps {
  label: string
  value: string | number
  icon: LucideIcon
  /** Ikon foni uchun tailwind class, masalan "bg-brand-50" */
  iconBg?: string
  /** Ikon rangi uchun tailwind class, masalan "text-brand-600" */
  iconColor?: string
  hint?: string
}

/**
 * KICHIKROQ VA BIR QATORDA (mijoz, 2026-09-19: "cardlarni ... biroz
 * kichiklashtirish kerak, cardlarni ichidagi malumot sig'may ham qolyapti").
 *
 * Uchta qoida: ichki bo'shliq kamaydi, raqam bir qatorda turadi va sig'masa
 * "..." bilan kesiladi (to'lig'i hoverda). Ilgari "310 580 000 so'm" ikkiga
 * bo'linib, kartochka bo'yiga cho'zilardi.
 */
export function StatCard({
  label,
  value,
  icon: Icon,
  iconBg = 'bg-brand-50',
  iconColor = 'text-brand-600',
  hint,
}: StatCardProps) {
  return (
    <Card className="flex items-center gap-3 p-4">
      <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${iconBg}`}>
        <Icon className={`h-5 w-5 ${iconColor}`} />
      </div>
      <div className="min-w-0">
        <p className="truncate text-xs text-slate-500">{label}</p>
        <p
          className="truncate text-xl font-semibold text-slate-800"
          title={typeof value === 'string' ? value : undefined}
        >
          {value}
        </p>
        {hint && (
          <p className="truncate text-xs text-slate-400" title={hint}>
            {hint}
          </p>
        )}
      </div>
    </Card>
  )
}
