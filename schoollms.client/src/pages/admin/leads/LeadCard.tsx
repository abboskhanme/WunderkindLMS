import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Phone, Cake, GraduationCap } from 'lucide-react'
import type { Lead } from '@/types'
import { genderLabels } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'

/** Faqat ko'rinish (drag overlay uchun ham ishlatiladi) */
export function LeadCardContent({ lead, dragging }: { lead: Lead; dragging?: boolean }) {
  return (
    <div
      className={cn(
        'rounded-xl border border-slate-200 bg-white p-3 shadow-sm',
        dragging && 'rotate-2 shadow-lg ring-2 ring-brand-200',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="font-medium text-slate-800">{lead.fullName}</p>
        <span className="shrink-0 rounded-md bg-brand-50 px-1.5 py-0.5 text-xs font-medium text-brand-700">
          {lead.targetGrade}-sinf
        </span>
      </div>

      <div className="mt-2 space-y-1 text-xs text-slate-500">
        <p className="flex items-center gap-1.5">
          <GraduationCap className="h-3.5 w-3.5" /> {genderLabels[lead.gender]}
        </p>
        <p className="flex items-center gap-1.5">
          <Cake className="h-3.5 w-3.5" /> {formatDate(lead.birthDate)}
        </p>
        <p className="flex items-center gap-1.5">
          <Phone className="h-3.5 w-3.5" /> {lead.parentPhone}
        </p>
        <p className="text-slate-400">{lead.parentFullName}</p>
      </div>

      {lead.note && (
        <p className="mt-2 rounded-md bg-slate-50 px-2 py-1 text-xs text-slate-500">
          {lead.note}
        </p>
      )}
    </div>
  )
}

/** Sudraladigan (draggable) kartochka */
export function LeadCard({ lead, onClick }: { lead: Lead; onClick?: () => void }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: lead.id,
  })

  const style = transform ? { transform: CSS.Translate.toString(transform) } : undefined

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={onClick}
      className={cn('cursor-grab touch-none active:cursor-grabbing', isDragging && 'opacity-40')}
    >
      <LeadCardContent lead={lead} />
    </div>
  )
}
