import { Pencil, Trash2 } from 'lucide-react'
import type { Lead } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { genderLabels } from '@/config/constants'
import { formatDate } from '@/lib/utils'

interface Props {
  lead: Lead | null
  onClose: () => void
  onEdit: (lead: Lead) => void
  onDelete: (lead: Lead) => void
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span className="text-right text-sm font-medium text-slate-800">{value}</span>
    </div>
  )
}

export function LeadDetailModal({ lead, onClose, onEdit, onDelete }: Props) {
  return (
    <Modal
      open={!!lead}
      onClose={onClose}
      title="Lid ma'lumotlari"
      footer={
        lead && (
          <>
            <Button variant="danger" onClick={() => onDelete(lead)} className="mr-auto">
              <Trash2 className="h-4 w-4" /> O'chirish
            </Button>
            <Button variant="secondary" onClick={onClose}>
              Yopish
            </Button>
            <Button onClick={() => onEdit(lead)}>
              <Pencil className="h-4 w-4" /> Tahrirlash
            </Button>
          </>
        )
      }
    >
      {lead && (
        <div>
          <Row label="F.I.SH" value={lead.fullName} />
          <Row label="Jinsi" value={genderLabels[lead.gender]} />
          <Row label="Tug'ilgan kun" value={formatDate(lead.birthDate)} />
          <Row label="Nechinchi sinfga" value={`${lead.targetGrade}-sinf`} />
          <Row label="Ota-onasi" value={lead.parentFullName || '—'} />
          <Row label="Ota-onasi raqami" value={lead.parentPhone || '—'} />
          {lead.note && (
            <div className="pt-3">
              <p className="mb-1 text-sm text-slate-400">Izoh</p>
              <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-700">{lead.note}</p>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
