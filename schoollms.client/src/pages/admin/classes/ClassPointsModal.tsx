import { useEffect, useState } from 'react'
import type { DisciplineReason } from '@/types'
import { addClassDisciplinePoint, getDisciplineReasons } from '@/api/services/discipline'
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
 */
export function ClassPointsModal({ open, classId, className, onClose, onDone }: Props) {
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda oldingi tanlov tozalanadi (maqsadli, loyihadagi mavjud naqsh)
    setNote('')
    setError(null)
    getDisciplineReasons().then((all) => {
      const other = all.filter((r) => r.kind === 'other' && r.isActive)
      setReasons(other)
      setReasonId(other[0]?.id ?? '')
    })
  }, [open])

  const reasonLabel = (r: DisciplineReason) => `${r.name} (${r.points > 0 ? '+' : ''}${r.points})`
  const selected = reasons.find((r) => r.id === reasonId)

  const handleSubmit = async () => {
    if (!reasonId || busy) return
    setBusy(true)
    setError(null)
    try {
      const result = await addClassDisciplinePoint(classId, reasonId, note.trim() || undefined)
      alert(
        `${result.applied} ta o'quvchiga ball qo'yildi`
          + (result.notifiedParents > 0 ? ` — ${result.notifiedParents} ta ota-onaga xabar yuborildi` : ''),
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
          <Button onClick={handleSubmit} disabled={busy || !reasonId}>
            {busy ? 'Bajarilmoqda...' : 'Kiritish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          Sinfning <b>har bir faol o'quvchisiga</b> shu sabab bilan alohida yozuv qo'shiladi.
        </div>

        {reasons.length === 0 ? (
          <p className="text-sm text-amber-600">
            Avval "Ball sabablar" bo'limida mustaqil sabab qo'shing.
          </p>
        ) : (
          <Select
            label="Sabab"
            required
            value={reasonId}
            onChange={(e) => setReasonId(e.target.value)}
          >
            {reasons.map((r) => (
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
