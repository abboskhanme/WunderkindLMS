import { useState } from 'react'
import { Pencil, Plus, ShieldAlert, Trash2 } from 'lucide-react'
import type { DisciplinePoint } from '@/types'
import { deleteDisciplinePoint, disciplineTypeLabel, disciplineTypeOf } from '@/api/services/discipline'
import { cn, formatDate } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import {
  IncidentFormModal,
  type IncidentEditing,
  type IncidentStudent,
} from '@/pages/admin/discipline/IncidentFormModal'
import { ProfileEmpty, ProfileSection } from './ProfileUi'

interface Props {
  points: DisciplinePoint[]
  /** The 100-based score (the same number as the overview and the sidebar). */
  score: number
  student: IncidentStudent
  /** Full `discipline` key — add, edit and delete; otherwise the history is read-only. */
  canEdit: boolean
  /** Reload the notebook after a change. */
  onChanged: () => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/** "2026-10-15T10:12:00" → "15.10.2026 10:12"; a journal day stays a day. */
function when(iso: string): string {
  const day = formatDate(iso.slice(0, 10))
  return iso.length >= 16 && iso[10] === 'T' ? `${day} ${iso.slice(11, 16)}` : day
}

/**
 * Kartochkaning "Xulq-atvor" tab'i — EduSchool'dagi "Behaviour incidents" (docs/modules/behaviour.md):
 * turi (Ijobiy / Salbiy), ball, sabab, kim va qachon; `discipline` to'liq ruxsatida qo'shish,
 * tahrirlash va o'chirish.
 *
 * Yozuvlar ikki manbadan: qo'lda qo'yilgan ("manual" — xodim yoki o'qituvchi) va jurnaldagi davomat
 * sababi ("attendance" — faqat ko'rish, hamshira/jurnal belgisi). Ma'lumot shaxsiy daftar javobidan.
 */
export function DisciplineTab({ points, score, student, canEdit, onChanged }: Props) {
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<IncidentEditing | null>(null)

  const close = () => {
    setFormOpen(false)
    setEditing(null)
  }

  const remove = async (p: DisciplinePoint) => {
    if (!confirm(`"${p.reasonName}" (${p.points > 0 ? '+' : ''}${p.points}) yozuvini o'chirasizmi?`)) return
    try {
      await deleteDisciplinePoint(p.id)
      onChanged()
    } catch (e) {
      alert(errorText(e, "O'chirib bo'lmadi"))
    }
  }

  return (
    <ProfileSection
      title="Xulq-atvor"
      icon={ShieldAlert}
      action={
        canEdit && (
          <Button
            onClick={() => {
              setEditing(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Harakat qo'shish
          </Button>
        )
      }
    >
      <p className="mb-3 text-sm text-slate-500">
        Joriy ball:{' '}
        <span className={cn('text-base font-semibold', score < 100 ? 'text-red-600' : 'text-emerald-600')}>
          {score}
        </span>
        <span className="ml-1 text-xs text-slate-400">(100 dan boshlanadi)</span>
      </p>

      {points.length === 0 ? (
        <ProfileEmpty>Yozuv yo'q</ProfileEmpty>
      ) : (
        <div className="space-y-2">
          {points.map((p) => {
            const type = disciplineTypeOf(p.points)
            return (
              <div key={p.id} className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2">
                <span
                  className={cn(
                    'shrink-0 rounded-md px-2 py-0.5 text-xs font-medium',
                    type === 'negative' ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                  )}
                >
                  {disciplineTypeLabel[type]}
                </span>
                <span
                  className={cn(
                    'shrink-0 rounded-md px-2 py-0.5 text-sm font-semibold',
                    p.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                  )}
                >
                  {p.points > 0 ? `+${p.points}` : p.points}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-slate-700">{p.reasonName}</p>
                  {p.note && <p className="truncate text-xs text-slate-400">{p.note}</p>}
                </div>
                <div className="shrink-0 text-right text-xs text-slate-400">
                  <p>{when(p.createdAt)}</p>
                  <p title={p.updatedBy ? `Tahrirladi: ${p.updatedBy}` : undefined}>
                    {p.source === 'attendance' ? 'jurnal davomati' : p.createdBy || '—'}
                    {p.updatedAt && ' (tahrirlangan)'}
                  </p>
                </div>
                {canEdit && p.source === 'manual' && (
                  <div className="flex shrink-0 items-center gap-0.5">
                    <button
                      type="button"
                      title="Tahrirlash"
                      onClick={() => {
                        setEditing({
                          id: p.id,
                          reasonId: p.reasonId,
                          reasonName: p.reasonName,
                          points: p.points,
                          note: p.note,
                        })
                        setFormOpen(true)
                      }}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      title="O'chirish"
                      onClick={() => remove(p)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}

      {canEdit && (
        <IncidentFormModal
          open={formOpen}
          student={student}
          editing={editing}
          onClose={close}
          onSaved={() => {
            close()
            onChanged()
          }}
        />
      )}
    </ProfileSection>
  )
}
