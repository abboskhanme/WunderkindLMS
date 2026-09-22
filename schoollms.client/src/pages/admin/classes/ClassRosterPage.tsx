import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, ArrowRightLeft, Plus, Search, Send, UserMinus } from 'lucide-react'
import type { ClassCandidate, ClassRoster, ClassRosterRow } from '@/api/services/classMemberships'
import {
  EMPTY_MEMBERSHIP,
  addClassMember,
  getClassCandidates,
  getClassRoster,
  removeClassMember,
} from '@/api/services/classMemberships'
import { useAuth } from '@/context/auth-context'
import { formatMoney, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { RosterMessageModal } from '../groups/RosterMessageModal'
import { ClassTransferModal } from './ClassTransferModal'

/**
 * Sinf ro'yxati — `docs/modules/students-parity.md` §2.2 (C-2).
 *
 * Qo'shish, sabab bilan chiqarish, ayni darajadagi boshqa sinfga o'tkazish va
 * tanlanganlarning ota-onalariga Telegram xabari. Har bir amal serverda
 * `students.class_name` ni VA `class_memberships` yozuvini birga yangilaydi —
 * jurnal, davomat va hisobotlar bugungiday ishlayveradi.
 */
export function ClassRosterPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()

  const [roster, setRoster] = useState<ClassRoster | null>(null)
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [addOpen, setAddOpen] = useState(false)
  const [messageOpen, setMessageOpen] = useState(false)
  const [transferFor, setTransferFor] = useState<ClassRosterRow | null>(null)

  // Qoldiq — pul ma'lumoti (SPEC §4.3). Server uni faqat moliyani ko'ra
  // oladiganga to'ldiradi; ekran ustunni ham o'shalarga ko'rsatadi, aks holda
  // hamma joyda "0 so'm" turib, yolg'on ma'lumot bo'lardi.
  const canSeeBalance =
    user?.role === 'admin' ||
    user?.role === 'superadmin' ||
    Boolean(user?.permissions?.includes('finance'))

  const load = useCallback(() => {
    setLoading(true)
    getClassRoster(id, search.trim() || undefined)
      .then(setRoster)
      .finally(() => setLoading(false))
  }, [id, search])

  useEffect(() => {
    const t = setTimeout(load, 250)
    return () => clearTimeout(t)
  }, [load])

  const failed = (e: unknown, fallback: string) =>
    alert(
      (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback,
    )

  const toggle = (studentId: string) =>
    setSelected((prev) =>
      prev.includes(studentId) ? prev.filter((x) => x !== studentId) : [...prev, studentId],
    )

  const handleRemove = (row: ClassRosterRow) => {
    const reason = prompt(
      `"${row.fullName}" ni sinfdan chiqarasizmi?\nSabab (majburiy):`,
      '',
    )
    if (reason === null) return
    if (!reason.trim()) {
      alert('Sinfdan chiqarish sababini yozing')
      return
    }
    removeClassMember(row.membershipId, reason.trim())
      .then(load)
      .catch((e) => failed(e, "Chiqarib bo'lmadi"))
  }

  if (loading && !roster) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button title="Orqaga" aria-label="Orqaga" variant="ghost" onClick={() => navigate('/admin/classes')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">
              {roster?.className} — ro'yxat
            </h1>
            <p className="text-sm text-slate-400">
              {roster?.students.length ?? 0} ta o'quvchi
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="secondary"
            disabled={selected.length === 0}
            onClick={() => setMessageOpen(true)}
          >
            <Send className="h-4 w-4" /> Xabar ({selected.length})
          </Button>
          <Button onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4" /> O'quvchi qo'shish
          </Button>
        </div>
      </div>

      <Card>
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
            placeholder="Ism bo'yicha qidirish..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
      </Card>

      <Card className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="w-10 px-4 py-3"></th>
                <th className="w-10 px-4 py-3">#</th>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Qo'shilgan</th>
                {canSeeBalance && <th className="px-4 py-3">Qoldiq</th>}
                <th className="px-4 py-3 text-right">Amallar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {roster?.students.map((row, i) => {
                const tracked = row.membershipId !== EMPTY_MEMBERSHIP
                return (
                  <tr key={row.studentId} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <input
                        type="checkbox"
                        className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                        checked={selected.includes(row.studentId)}
                        onChange={() => toggle(row.studentId)}
                      />
                    </td>
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3">
                      <Link
                        to={`/admin/students/${row.studentId}`}
                        className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                      >
                        {row.fullName}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-slate-500">{tracked ? row.joinedOn : '—'}</td>
                    {canSeeBalance && (
                      <td
                        className={cn(
                          'px-4 py-3 font-medium',
                          row.balance < 0 ? 'text-red-600' : 'text-slate-600',
                        )}
                      >
                        {formatMoney(row.balance)}
                      </td>
                    )}
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        {tracked ? (
                          <>
                            <button
                              type="button"
                              title="Boshqa sinfga o'tkazish"
                              onClick={() => setTransferFor(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <ArrowRightLeft className="h-4 w-4" />
                            </button>
                            <button
                              type="button"
                              title="Sinfdan chiqarish"
                              onClick={() => handleRemove(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                            >
                              <UserMinus className="h-4 w-4" />
                            </button>
                          </>
                        ) : (
                          // Migratsiya backfill'i bu bolani qamramagan (sinf nomi
                          // katalogdagi sinfga mos tushmagan). Amal berish o'rniga
                          // buni ochiq aytamiz — jimgina noto'g'ri yozuv yaratishdan
                          // ko'ra administrator kartochkadan tuzatgani ma'qul.
                          <span
                            className="text-xs text-amber-600"
                            title="Sinf a'zoligi yozuvi yo'q — o'quvchi kartochkasida sinfni qayta saqlang"
                          >
                            a'zolik yozuvi yo'q
                          </span>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
              {roster?.students.length === 0 && (
                <tr>
                  <td colSpan={canSeeBalance ? 6 : 5} className="px-4 py-12 text-center text-slate-400">
                    Sinfda o'quvchi yo'q
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <AddStudentModal
        open={addOpen}
        classId={id}
        onClose={() => setAddOpen(false)}
        onDone={() => {
          setAddOpen(false)
          load()
        }}
      />

      <RosterMessageModal
        open={messageOpen}
        studentIds={selected}
        onClose={() => setMessageOpen(false)}
      />

      <ClassTransferModal
        open={Boolean(transferFor)}
        membershipId={transferFor?.membershipId ?? ''}
        studentName={transferFor?.fullName ?? ''}
        currentClassId={id}
        grade={roster?.grade ?? 0}
        onClose={() => setTransferFor(null)}
        onDone={() => {
          setTransferFor(null)
          load()
        }}
      />
    </div>
  )
}

/**
 * Sinfga o'quvchi qo'shish. Ro'yxatda FAQAT sinfsiz, arxivlanmagan o'quvchilar
 * (§2.2.1) — sinfli bolani ikkinchi sinfga qo'shish emas, O'TKAZISH kerak.
 */
function AddStudentModal({
  open,
  classId,
  onClose,
  onDone,
}: {
  open: boolean
  classId: string
  onClose: () => void
  onDone: () => void
}) {
  const [candidates, setCandidates] = useState<ClassCandidate[]>([])
  const [search, setSearch] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    getClassCandidates(classId, search.trim() || undefined).then(setCandidates)
  }, [open, classId, search])

  const add = (studentId: string) => {
    setBusy(true)
    addClassMember(classId, studentId)
      .then(({ warning }) => {
        // C-4: sig'im OGOHLANTIRISHI — amal allaqachon bajarilgan, faqat xabar beramiz.
        if (warning) alert(warning)
        onDone()
      })
      .catch((e) =>
        alert(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Qo'shib bo'lmadi",
        ),
      )
      .finally(() => setBusy(false))
  }

  return (
    <Modal open={open} onClose={onClose} title="Sinfga o'quvchi qo'shish" size="md">
      <div className="space-y-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
            placeholder="Ism bo'yicha qidirish..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <p className="text-xs text-slate-400">
          Ro'yxatda faqat hali sinfga biriktirilmagan o'quvchilar bor. Boshqa sinfdagi bolani
          ko'chirish uchun uning qatoridagi "o'tkazish" tugmasidan foydalaning.
        </p>

        <div className="max-h-80 overflow-y-auto">
          {candidates.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Sinfsiz o'quvchi yo'q</p>
          ) : (
            candidates.map((c) => (
              <button
                key={c.studentId}
                type="button"
                disabled={busy}
                onClick={() => add(c.studentId)}
                className="flex w-full items-center justify-between rounded-lg px-2 py-2 text-left text-sm text-slate-700 transition-colors hover:bg-brand-50 disabled:opacity-50"
              >
                <span className="truncate">{c.fullName}</span>
                <Plus className="h-4 w-4 shrink-0 text-brand-600" />
              </button>
            ))
          )}
        </div>
      </div>
    </Modal>
  )
}
