import { useDroppable } from '@dnd-kit/core'
import type { LucideIcon } from 'lucide-react'
import { ChevronLeft, ChevronRight, Pencil, Trash2 } from 'lucide-react'
import type { Lead, Stage } from '@/types'
import { stageColors } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import { LeadCard } from './LeadCard'

interface Props {
  stage: Stage
  leads: Lead[]
  isFirst: boolean
  isLast: boolean
  onCardClick: (lead: Lead) => void
  onEdit: (stage: Stage) => void
  onDelete: (stage: Stage) => void
  onMove: (id: string, dir: -1 | 1) => void
}

export function LeadColumn({
  stage,
  leads,
  isFirst,
  isLast,
  onCardClick,
  onEdit,
  onDelete,
  onMove,
}: Props) {
  const c = stageColors[stage.color]
  const { setNodeRef, isOver } = useDroppable({ id: stage.id })

  return (
    <div className="flex w-72 shrink-0 flex-col overflow-hidden rounded-2xl border border-slate-200">
      <div className={cn('h-1', c.bar)} />
      <div className={cn('flex flex-1 flex-col p-3', c.tint)}>
        <div className="mb-3 flex items-center justify-between gap-1">
          <div className="flex min-w-0 items-center gap-2">
            <span className={cn('h-2.5 w-2.5 shrink-0 rounded-full', c.dot)} />
            <h3 className="truncate text-sm font-semibold text-slate-700">{stage.title}</h3>
            <span className={cn('shrink-0 rounded-full px-1.5 py-0.5 text-xs font-medium', c.badge)}>
              {leads.length}
            </span>
          </div>
          <div className="flex items-center">
            <HBtn icon={ChevronLeft} title="Chapga" disabled={isFirst} onClick={() => onMove(stage.id, -1)} />
            <HBtn icon={ChevronRight} title="O'ngga" disabled={isLast} onClick={() => onMove(stage.id, 1)} />
            <HBtn icon={Pencil} title="Tahrirlash" onClick={() => onEdit(stage)} />
            <HBtn icon={Trash2} title="O'chirish" danger onClick={() => onDelete(stage)} />
          </div>
        </div>

        <div
          ref={setNodeRef}
          className={cn(
            'min-h-[140px] flex-1 space-y-2 rounded-xl p-1 transition',
            isOver && cn('ring-2', c.ring),
          )}
        >
          {leads.map((lead) => (
            <LeadCard key={lead.id} lead={lead} onClick={() => onCardClick(lead)} />
          ))}
          {leads.length === 0 && (
            <p className="px-2 py-6 text-center text-xs text-slate-400">Bo'sh</p>
          )}
        </div>
      </div>
    </div>
  )
}

interface HBtnProps {
  icon: LucideIcon
  title: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}

function HBtn({ icon: Icon, title, onClick, disabled, danger }: HBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-md p-1 transition-colors disabled:opacity-30 disabled:hover:bg-transparent',
        danger
          ? 'text-slate-500 hover:bg-red-100 hover:text-red-600'
          : 'text-slate-500 hover:bg-white hover:text-slate-700',
      )}
    >
      <Icon className="h-3.5 w-3.5" />
    </button>
  )
}
