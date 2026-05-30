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

export function StatCard({
  label,
  value,
  icon: Icon,
  iconBg = 'bg-brand-50',
  iconColor = 'text-brand-600',
  hint,
}: StatCardProps) {
  return (
    <Card className="flex items-center gap-4">
      <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${iconBg}`}>
        <Icon className={`h-6 w-6 ${iconColor}`} />
      </div>
      <div className="min-w-0">
        <p className="truncate text-sm text-slate-500">{label}</p>
        <p className="text-2xl font-semibold text-slate-800">{value}</p>
        {hint && <p className="mt-0.5 text-xs text-slate-400">{hint}</p>}
      </div>
    </Card>
  )
}
