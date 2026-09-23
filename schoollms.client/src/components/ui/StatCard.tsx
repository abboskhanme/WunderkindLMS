import type { LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Card } from './Card'
import { cn } from '@/lib/utils'

interface StatCardProps {
  label: string
  value: string | number
  icon: LucideIcon
  /** Ikon foni uchun tailwind class, masalan "bg-brand-50" */
  iconBg?: string
  /** Ikon rangi uchun tailwind class, masalan "text-brand-600" */
  iconColor?: string
  hint?: string
  /**
   * Bosilganda ochiladigan sahifa — karta shu ro'yxatga olib boradi (mijoz, 2026-09-23:
   * "aktiv o'quvchilar cardiga bossa aktiv o'quvchilar ro'yxatiga olib o'tsin").
   */
  to?: string
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
  to,
}: StatCardProps) {
  const card = (
    <Card
      className={cn(
        'flex items-center gap-3 p-4',
        to && 'h-full transition group-hover:-translate-y-0.5 group-hover:border-brand-300 group-hover:shadow-md',
      )}
    >
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
  if (!to) return card
  return (
    <Link
      to={to}
      title={`${label} — ro'yxatni ochish`}
      className="group block rounded-2xl focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
    >
      {card}
    </Link>
  )
}
