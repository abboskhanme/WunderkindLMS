import { useEffect, useState } from 'react'
import { Archive, AlertTriangle } from 'lucide-react'
import type { Student } from '@/types'
import {
  archiveManyStudents,
  getArchiveReasons,
  type ArchiveBlockedStudent,
  type ArchiveReason,
} from '@/api/services/archiveReasons'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { useAuth } from '@/context/auth-context'
import { formatMoney } from '@/lib/utils'

/**
 * O'quvchini (yoki bir nechtasini) arxivga ko'chirish — §2.2.
 *
 * BITTA OYNA, IKKI HOLAT. Bitta o'quvchi va ommaviy arxivlash bir xil qoidaga
 * bo'ysunadi (sabab majburiy, qarzdor rad etiladi), shuning uchun ikkita oyna
 * emas, bitta: ro'yxat uzunligi farq qiladi, xatti-harakat emas.
 *
 * SABAB IKKI QISMDAN. Katalogdan tanlov (guruhlash uchun) + erkin matn
 * (tafsilot uchun). Matn MAJBURIY — "Boshqa" tanlanganda ham, tanlanmaganda ham.
 *
 * QARZDOR RAD ETILADI. Server 400 qaytaradi va qaysi bola qancha qarzi borligini
 * aytadi. Superadmin uchun qo'shimcha tugma chiqadi — lekin AVTOMATIK o'tib
 * ketmaydi: qarz arxivda ham qarzligicha qoladi va buni ekran aniq yozadi.
 */

interface Props {
  /** Arxivlanadigan o'quvchilar. Bo'sh ro'yxat = oyna yopiq. */
  students: Student[]
  onClose: () => void
  /** Arxivlash muvaffaqiyatli tugadi — id'lar va yozilgan izoh bilan. */
  onArchived: (ids: string[], reason: string) => void
}

function errorMessage(e: unknown, fallback: string): string {
  return (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

function blockedFrom(e: unknown): ArchiveBlockedStudent[] {
  return (e as { response?: { data?: { blocked?: ArchiveBlockedStudent[] } } })?.response?.data
    ?.blocked ?? []
}

export function ArchiveStudentsModal({ students, onClose, onArchived }: Props) {
  const { user } = useAuth()
  const isSuperAdmin = user?.role === 'superadmin'
  const open = students.length > 0
  const bulk = students.length > 1

  const [reasons, setReasons] = useState<ArchiveReason[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [blocked, setBlocked] = useState<ArchiveBlockedStudent[]>([])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna har ochilganda holat tozalanadi (maqsadli)
    if (open) {
      setReasonId('')
      setNote('')
      setError(null)
      setBlocked([])
      getArchiveReasons()
        .then(setReasons)
        .catch(() => setReasons([]))
    }
  }, [open])

  const submit = async (force: boolean) => {
    const reason = note.trim()
    if (!reason) return
    setBusy(true)
    setError(null)
    try {
      const ids = students.map((s) => s.id)
      await archiveManyStudents({
        studentIds: ids,
        reason,
        archiveReasonId: reasonId || null,
        force,
      })
      onArchived(ids, reason)
    } catch (e) {
      setBlocked(blockedFrom(e))
      setError(errorMessage(e, 'Arxivlashda xatolik'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={bulk ? `${students.length} ta o'quvchini arxivlash` : "O'quvchini arxivga ko'chirish"}
      size={bulk ? 'md' : 'sm'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          {blocked.length > 0 && isSuperAdmin && (
            <Button variant="danger" onClick={() => submit(true)} disabled={busy || !note.trim()}>
              Baribir arxivlash
            </Button>
          )}
          <Button variant="danger" onClick={() => submit(false)} disabled={busy || !note.trim()}>
            <Archive className="h-4 w-4" />
            {busy ? 'Arxivlanmoqda…' : "Arxivga ko'chirish"}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm text-slate-600">
        {bulk ? (
          <div className="max-h-32 overflow-y-auto rounded-lg bg-slate-50 px-3 py-2">
            {students.map((s) => (
              <div key={s.id} className="text-slate-700">
                {s.fullName} <span className="text-slate-400">— {s.className}</span>
              </div>
            ))}
          </div>
        ) : (
          <p>
            <span className="font-medium text-slate-800">{students[0]?.fullName}</span> o'quvchini
            arxivga ko'chirasiz.
          </p>
        )}

        <p>Tarixiy ma'lumotlar (jurnal, davomat, to'lovlar) saqlanadi, lekin:</p>
        <ul className="ml-5 list-disc space-y-0.5 text-slate-500">
          <li>Faol ro'yxatdan yashirinadi (jurnal/davomat/dashboardda ko'rinmaydi)</li>
          <li>Oylik to'lov hisoblanmaydi</li>
          <li>Login bloklanadi (akkaunt paroli o'chiriladi)</li>
        </ul>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-700">Sabab turi</span>
          <select
            value={reasonId}
            onChange={(e) => setReasonId(e.target.value)}
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">Tanlanmagan</option>
            {reasons.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </select>
          <p className="mt-1 text-xs text-slate-400">
            Hisobotda guruhlash uchun. Ro'yxat "Sozlamalar → Arxivlash sabablari"da boshqariladi.
          </p>
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-700">Izoh (majburiy)</span>
          <input
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="masalan: 9-sinfni bitirdi, hujjatlari topshirildi"
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            autoFocus
          />
        </div>

        {error && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2">
            <p className="flex items-start gap-2 font-medium text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
            </p>
            {blocked.length > 0 && (
              <ul className="mt-2 space-y-0.5 text-amber-900">
                {blocked.map((b) => (
                  <li key={b.studentId}>
                    {b.fullName} <span className="text-amber-700">({b.className})</span> —{' '}
                    {formatMoney(b.debt)} qarz
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
