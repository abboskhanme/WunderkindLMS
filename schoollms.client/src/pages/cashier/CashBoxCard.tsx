import { ArrowLeftRight, ArrowDownCircle, ArrowUpCircle, Pencil, Repeat, Star } from 'lucide-react'
import type { CashBox } from '@/api/services/cashBoxes'
import { cn } from '@/lib/utils'
import { formatSumWithUnit } from './format'

export type CashBoxActionMode = 'in' | 'out' | 'transfer' | 'exchange'

interface Props {
  box: CashBox
  /** Tanlangan (faol ko'rsatilayotgan) kassa — to'ldirilgan kartochka va amal tugmalari shu yerda. */
  selected: boolean
  /** Tahrirlash tugmasi — faqat boshqaruvchi (admin/direktor) uchun ko'rinadi. */
  canManage: boolean
  onSelect: () => void
  onEdit: () => void
  onAction: (mode: CashBoxActionMode) => void
}

/**
 * Bitta kassa kartochkasi (EduSchool tuzilishi — CLAUDE.md: dizayn o'zimizniki).
 *
 * Tanlangan kassa — to'ldirilgan rangli kartochka, balansi katta, mas'ul
 * ismi, "Tahrirlash" tugmasi va standart belgisi (yulduzcha), o'ng tomonda
 * to'rtta amal. Qolganlari — oddiy oq kartochka, bosilsa shu tanlanadi.
 */
export function CashBoxCard({ box, selected, canManage, onSelect, onEdit, onAction }: Props) {
  if (selected) {
    return (
      <div className="rounded-2xl bg-brand-600 p-4 text-white shadow-sm">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="flex items-center gap-1.5 text-sm font-medium text-brand-50">
              {box.isDefault && <Star className="h-3.5 w-3.5 shrink-0 fill-amber-300 text-amber-300" />}
              <span className="truncate">{box.name}</span>
            </p>
            <p className="mt-1 text-2xl font-semibold tabular-nums">{formatSumWithUnit(box.balance)}</p>
            {box.responsibleName && (
              <p className="mt-0.5 truncate text-xs text-brand-100">{box.responsibleName}</p>
            )}
            {!box.isActive && (
              <span className="mt-2 inline-block rounded-md bg-white/15 px-2 py-0.5 text-xs">
                Nofaol
              </span>
            )}
          </div>
          {canManage && (
            <button
              type="button"
              onClick={onEdit}
              title="Tahrirlash"
              aria-label="Kassani tahrirlash"
              className="shrink-0 rounded-lg bg-white/10 p-1.5 transition-colors hover:bg-white/20"
            >
              <Pencil className="h-4 w-4" />
            </button>
          )}
        </div>

        <div className="mt-3 grid grid-cols-2 gap-2">
          <ActionButton
            label="Kirim"
            icon={ArrowDownCircle}
            className="bg-emerald-500 text-white hover:bg-emerald-400"
            onClick={() => onAction('in')}
          />
          <ActionButton
            label="Chiqim"
            icon={ArrowUpCircle}
            className="bg-red-500 text-white hover:bg-red-400"
            onClick={() => onAction('out')}
          />
          <ActionButton
            label="Ko'chirish"
            icon={ArrowLeftRight}
            className="bg-white/10 text-white hover:bg-white/20"
            onClick={() => onAction('transfer')}
          />
          <ActionButton
            label="Ayirboshlash"
            icon={Repeat}
            className="bg-white/10 text-white hover:bg-white/20"
            onClick={() => onAction('exchange')}
          />
        </div>
      </div>
    )
  }

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        'w-full rounded-2xl border border-slate-200 bg-white p-4 text-left transition-colors',
        'hover:border-brand-200 hover:bg-brand-50/30',
      )}
    >
      <p className="flex items-center gap-1.5 text-sm font-medium text-slate-500">
        {box.isDefault && <Star className="h-3.5 w-3.5 shrink-0 fill-amber-400 text-amber-400" />}
        <span className="truncate">{box.name}</span>
        {!box.isActive && (
          <span className="shrink-0 rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-400">
            Nofaol
          </span>
        )}
      </p>
      <p className="mt-1 text-lg font-semibold tabular-nums text-slate-800">
        {formatSumWithUnit(box.balance)}
      </p>
      {box.responsibleName && (
        <p className="mt-0.5 truncate text-xs text-slate-400">{box.responsibleName}</p>
      )}
    </button>
  )
}

function ActionButton({
  label,
  icon: Icon,
  className,
  onClick,
}: {
  label: string
  icon: typeof ArrowDownCircle
  className: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center justify-center gap-1.5 rounded-lg px-2 py-2 text-xs font-medium transition-colors',
        className,
      )}
    >
      <Icon className="h-3.5 w-3.5" />
      {label}
    </button>
  )
}
