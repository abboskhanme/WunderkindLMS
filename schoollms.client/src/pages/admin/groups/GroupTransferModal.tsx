import { useEffect, useState } from 'react'
import type { StudyGroupListItem } from '@/api/services/groups'
import { getGroups, transferGroupMember } from '@/api/services/groups'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  /** A'zolik id'si (guruhdagi yozuv), o'quvchi id'si emas. */
  memberId: string
  studentName: string
  /** Nishon guruhlar AYNAN shu fandan bo'ladi. */
  subjectId: string
  /** Joriy guruh — ro'yxatdan chiqarib tashlanadi. */
  currentGroupId: string
  onClose: () => void
  onDone: () => void
}

/**
 * O'quvchini boshqa guruhga o'tkazish — `students-parity.md` §2.1.1 "Transfer".
 *
 * Nishon ro'yxati FAQAT ayni fandagi faol guruhlardan iborat: bola bitta
 * fandan ko'pi bilan bitta guruhda bo'ladi, shuning uchun boshqa fanga
 * "o'tkazish" tushunchasi umuman yo'q (u yangi guruhga QO'SHISH bo'lardi).
 */
export function GroupTransferModal({
  open,
  memberId,
  studentName,
  subjectId,
  currentGroupId,
  onClose,
  onDone,
}: Props) {
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])
  const [toGroupId, setToGroupId] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda oldingi tanlov va xato tozalanadi (maqsadli)
    if (open && subjectId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumot kelganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
      setToGroupId('')
      setReason('')
      setError(null)
      getGroups({ subjectId }).then((rows) =>
        setGroups(rows.filter((g) => g.id !== currentGroupId && !g.isArchived)),
      )
    }
  }, [open, subjectId, currentGroupId])

  const handleSubmit = async () => {
    if (!toGroupId) return
    setBusy(true)
    setError(null)
    try {
      await transferGroupMember(memberId, toGroupId, reason.trim() || undefined)
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
      title="Boshqa guruhga o'tkazish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={busy || !toGroupId}>
            {busy ? 'Bajarilmoqda...' : "O'tkazish"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          <b>{studentName}</b> shu fan bo'yicha boshqa guruhga o'tkaziladi. Eski a'zolik
          yopiladi va tarixda qoladi.
        </div>

        {groups.length === 0 ? (
          <p className="text-sm text-amber-700">
            Bu fan bo'yicha boshqa faol guruh yo'q — avval yangi guruh oching.
          </p>
        ) : (
          <Select
            label="Qaysi guruhga"
            required
            value={toGroupId}
            onChange={(e) => setToGroupId(e.target.value)}
          >
            <option value="">Tanlang...</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name} ({g.memberCount} ta)
              </option>
            ))}
          </Select>
        )}

        <Textarea
          label="Sabab (ixtiyoriy)"
          rows={2}
          placeholder="Masalan: darajasi oshdi"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
