import { useEffect, useMemo, useState } from 'react'
import type { DisciplineReason, DisciplineType } from '@/types'
import {
  addClassDisciplinePoint,
  disciplineTypeLabel,
  disciplineTypeOf,
  getDisciplineReasons,
} from '@/api/services/discipline'
import { cn } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  classId: string
  className: string
  onClose: () => void
  onDone: () => void
}

/**
 * Butun sinfga bitta intizomiy ball — C-6 (students-parity.md §2.2.3): EduSchool'dagi
 * "sinf uchun harakat qo'shish" (`editBehaviorIncidents`, `POST /behavior-incidents/class`)
 * ning nusxasi. Faqat MUSTAQIL intizomiy sabablar ko'rsatiladi ("other") — davomat sababi
 * jurnal orqali, dars kesimida qo'yiladi, bu yerdan emas.
 *
 * Sabab bo'yicha xabar bayrog'i (`notifyParent`) yoqilgan bo'lsa, ota-onaga Telegram
 * xabari HAR BIR o'quvchi uchun alohida ketadi (§6.3) — serverdagi bir xil qoida, faqat
 * bitta so'rovda ko'p marta.
 *
 * EduSchool tartibi (2026-10-15, docs/modules/behaviour.md): avval TURI (Ijobiy / Salbiy), keyin
 * faqat o'sha turdagi sabablar.
 */
export function ClassPointsModal({ open, classId, className, onClose, onDone }: Props) {
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [type, setType] = useState<DisciplineType>('negative')
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda oldingi tanlov tozalanadi (maqsadli, loyihadagi mavjud naqsh)
    setNote('')
    setError(null)
    setType('negative')
    setReasonId('')
    getDisciplineReasons().then((all) => {
      setReasons(all.filter((r) => r.kind === 'other' && r.isActive && r.canUse !== false))
    })
  }, [open])

  const ofType = useMemo(
    () => reasons.filter((r) => disciplineTypeOf(r.points) === type).sort((a, b) => a.name.localeCompare(b.name)),
    [reasons, type],
  )
  const reasonLabel = (r: DisciplineReason) => `${r.name} (${r.points > 0 ? '+' : ''}${r.points})`
  const selected = ofType.find((r) => r.id === reasonId)

  const pickType = (next: DisciplineType) => {
    setType(next)
    setReasonId('')
  }

  const handleSubmit = async () => {
    if (!selected || busy) return
    setBusy(true)
    setError(null)
    try {
      const result = await addClassDisciplinePoint(classId, reasonId, note.trim() || undefined)
      alert(
        `${result.applied} ta o'quvchiga ball qo'yildi`
          + (result.notifiedParents > 0 ? ` — ${result.notifiedParents} ta ota-onaga xabar yuborildi` : '')
          + (result.skipped ? ` — ${result.skipped} ta o'quvchi bu muddatda allaqachon olgan, o'tkazib yuborildi` : ''),
      )
      onDone()
    } catch (e) {
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Ball qo'yib bo'lmadi",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${className} — sinfga ball qo'yish`}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={busy || !selected}>
            {busy ? 'Bajarilmoqda...' : 'Kiritish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          Sinfning <b>har bir faol o'quvchisiga</b> shu sabab bilan alohida yozuv qo'shiladi.
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-700">Turi</span>
          <div className="grid grid-cols-2 gap-2">
            {(['positive', 'negative'] as const).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => pickType(t)}
                className={cn(
                  'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                  type === t
                    ? t === 'positive'
                      ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                      : 'border-red-300 bg-red-50 text-red-600'
                    : 'border-slate-200 bg-white text-slate-500 hover:bg-slate-50',
                )}
              >
                {disciplineTypeLabel[t]}
              </button>
            ))}
          </div>
        </div>

        {ofType.length === 0 ? (
          <p className="text-sm text-amber-600">
            {reasons.length === 0
              ? 'Avval "Ball sabablar" bo\'limida mustaqil sabab qo\'shing.'
              : `${disciplineTypeLabel[type]} faol sabab yo'q — "Ball sabablar" bo'limida qo'shing.`}
          </p>
        ) : (
          <Select
            label="Sabab"
            required
            value={reasonId}
            onChange={(e) => setReasonId(e.target.value)}
          >
            <option value="">— tanlang —</option>
            {ofType.map((r) => (
              <option key={r.id} value={r.id}>
                {reasonLabel(r)}
              </option>
            ))}
          </Select>
        )}

        {selected?.notifyParent && (
          <p className="text-xs text-brand-600">
            Bu sabab bilan har bir o'quvchining ota-onasiga Telegram xabari ketadi.
          </p>
        )}

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          placeholder="Masalan: sinf tozalik reydi"
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
