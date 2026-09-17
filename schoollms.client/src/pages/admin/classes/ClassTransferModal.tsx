import { useEffect, useState } from 'react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import { transferClassMember } from '@/api/services/classMemberships'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  /** Sinf a'zoligi id'si (o'quvchi id'si emas). */
  membershipId: string
  studentName: string
  /** Joriy sinf — ro'yxatdan chiqarib tashlanadi. */
  currentClassId: string
  /** Nishon sinflar faqat SHU darajadan bo'ladi. */
  grade: number
  onClose: () => void
  onDone: () => void
}

/**
 * O'quvchini boshqa sinfga o'tkazish — `students-parity.md` §2.2.1 "Transfer".
 *
 * Nishon ro'yxati AYNI DARAJADAGI sinflardan iborat (5-A → 5-B). Darajani
 * o'zgartirish — o'quv yilini yakunlash amali, ro'yxat tugmasi emas; server
 * ham buni rad etadi.
 *
 * "Guruhlarda qolsin" sukut bo'yicha YOQIQ (EduSchool'dagi kabi). O'chirilsa,
 * yangi sinf BOQMAYDIGAN o'quv guruhlaridagi a'zolik yopiladi.
 */
export function ClassTransferModal({
  open,
  membershipId,
  studentName,
  currentClassId,
  grade,
  onClose,
  onDone,
}: Props) {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [toClassId, setToClassId] = useState('')
  const [keepGroups, setKeepGroups] = useState(true)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda oldingi tanlov tozalanadi (maqsadli)
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
      setToClassId('')
      setKeepGroups(true)
      setReason('')
      setError(null)
      getClasses().then((rows) =>
        setClasses(
          rows.filter((c) => c.id !== currentClassId && c.grade === grade && !c.isArchived),
        ),
      )
    }
  }, [open, currentClassId, grade])

  const handleSubmit = async () => {
    if (!toClassId) return
    setBusy(true)
    setError(null)
    try {
      await transferClassMember(membershipId, toClassId, keepGroups, reason.trim() || undefined)
      onDone()
    } catch (e) {
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "O'tkazib bo'lmadi",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Boshqa sinfga o'tkazish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={busy || !toClassId}>
            {busy ? 'Bajarilmoqda...' : "O'tkazish"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          <b>{studentName}</b> shu darajadagi boshqa sinfga o'tkaziladi. Eski a'zolik yopiladi
          va tarixda qoladi.
        </div>

        {classes.length === 0 ? (
          <p className="text-sm text-amber-700">
            {grade}-darajada boshqa sinf yo'q — o'tkazadigan joy topilmadi.
          </p>
        ) : (
          <Select
            label="Qaysi sinfga"
            required
            value={toClassId}
            onChange={(e) => setToClassId(e.target.value)}
          >
            <option value="">Tanlang...</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        )}

        <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 px-3 py-2.5">
          <input
            type="checkbox"
            className="mt-0.5 h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
            checked={keepGroups}
            onChange={(e) => setKeepGroups(e.target.checked)}
          />
          <span>
            <span className="block text-sm font-medium text-slate-700">Guruhlarda qolsin</span>
            <span className="block text-xs text-slate-400">
              Belgi olib tashlansa, yangi sinf boqmaydigan o'quv guruhlaridagi a'zolik yopiladi.
            </span>
          </span>
        </label>

        <Textarea
          label="Sabab (ixtiyoriy)"
          rows={2}
          placeholder="Masalan: ota-onaning iltimosiga ko'ra"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
