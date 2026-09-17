import { useEffect, useState } from 'react'
import { UserMinus, UserPlus, X } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import {
  getTeacherGroupMembers,
  getTeacherGroupCandidates,
  addTeacherGroupMembers,
  removeTeacherGroupMember,
  type TeacherGroupMember,
  type TeacherGroupCandidate,
} from '@/api/services/teacher'

interface Props {
  open: boolean
  groupId: string
  groupName: string
  /** Backend TeacherGroupDto.canEditRoster — FAQAT guruhga biriktirilgan o'qituvchiga true. */
  canEdit: boolean
  onClose: () => void
  /** Ro'yxat o'zgarganda (qo'shildi/chiqarildi) — ro'yxat sahifasidagi a'zolar sonini yangilash uchun. */
  onChanged: () => void
}

function errMsg(e: unknown, fallback: string): string {
  return (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

/**
 * Guruh ro'yxati — ko'rish har bir yetadigan o'qituvchiga ochiq, tahrirlash
 * (qo'shish/chiqarish) esa FAQAT guruhga biriktirilgan ("yetakchi")
 * o'qituvchiga (<c>canEdit</c> — backenddagi X-3 qoidasi, TeacherPortalController.cs).
 */
export function GroupRosterModal({ open, groupId, groupName, canEdit, onClose, onChanged }: Props) {
  const [members, setMembers] = useState<TeacherGroupMember[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [showAdd, setShowAdd] = useState(false)
  const [candidates, setCandidates] = useState<TeacherGroupCandidate[]>([])
  const [candidatesLoading, setCandidatesLoading] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [adding, setAdding] = useState(false)

  const [removingId, setRemovingId] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [removing, setRemoving] = useState(false)

  const load = () => {
    setLoading(true)
    setError(null)
    getTeacherGroupMembers(groupId)
      .then(setMembers)
      .catch((e) => setError(errMsg(e, "Ro'yxatni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    if (!open) return
    // Oyna har ochilganda oldingi holat (tanlov, xato, forma) tozalanadi.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oynani ochishda holatni tiklash (maqsadli)
    setShowAdd(false)
    setSelected(new Set())
    setRemovingId(null)
    setReason('')
    setError(null)
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, groupId])

  if (!open) return null

  const openAddPanel = () => {
    setShowAdd(true)
    setCandidatesLoading(true)
    getTeacherGroupCandidates(groupId)
      .then(setCandidates)
      .catch((e) => setError(errMsg(e, "Nomzodlarni yuklab bo'lmadi")))
      .finally(() => setCandidatesLoading(false))
  }

  const toggleCandidate = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const handleAdd = async () => {
    if (selected.size === 0) return
    setAdding(true)
    setError(null)
    try {
      await addTeacherGroupMembers(groupId, [...selected])
      setShowAdd(false)
      setSelected(new Set())
      load()
      onChanged()
    } catch (e) {
      setError(errMsg(e, "Qo'shib bo'lmadi"))
    } finally {
      setAdding(false)
    }
  }

  const handleRemove = async (memberId: string) => {
    setRemoving(true)
    setError(null)
    try {
      await removeTeacherGroupMember(groupId, memberId, reason)
      setRemovingId(null)
      setReason('')
      load()
      onChanged()
    } catch (e) {
      setError(errMsg(e, "Chiqarib bo'lmadi"))
    } finally {
      setRemoving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${groupName} — ro'yxat`}
      size="lg"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      <div className="space-y-4">
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </p>
        )}

        {!canEdit && (
          <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Ro'yxatni faqat ko'rasiz — tahrirlash huquqi guruhga biriktirilgan o'qituvchida.
          </p>
        )}

        {canEdit && !showAdd && (
          <div className="flex justify-end">
            <Button variant="secondary" onClick={openAddPanel}>
              <UserPlus className="h-4 w-4" /> O'quvchi qo'shish
            </Button>
          </div>
        )}

        {canEdit && showAdd && (
          <div className="space-y-2 rounded-xl border border-slate-200 bg-slate-50 p-3">
            <div className="flex items-center justify-between">
              <h4 className="text-sm font-semibold text-slate-700">Nomzodlar</h4>
              <button
                type="button"
                onClick={() => setShowAdd(false)}
                className="text-slate-400 hover:text-slate-600"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            {candidatesLoading ? (
              <Loader label="Yuklanmoqda..." />
            ) : candidates.length === 0 ? (
              <p className="py-4 text-center text-xs text-slate-400">Nomzod topilmadi</p>
            ) : (
              <div className="max-h-56 space-y-1 overflow-y-auto">
                {candidates.map((c) => {
                  const busy = c.currentGroupId != null
                  return (
                    <label
                      key={c.studentId}
                      className={cn(
                        'flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm',
                        busy ? 'cursor-not-allowed text-slate-400' : 'cursor-pointer hover:bg-white',
                      )}
                    >
                      <input
                        type="checkbox"
                        disabled={busy}
                        checked={selected.has(c.studentId)}
                        onChange={() => toggleCandidate(c.studentId)}
                        className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                      />
                      <span className="flex-1 truncate">{c.fullName}</span>
                      <span className="text-xs text-slate-400">{c.className}</span>
                      {busy && (
                        <span className="text-[10px] text-amber-600">{c.currentGroupName} da</span>
                      )}
                    </label>
                  )
                })}
              </div>
            )}
            <div className="flex justify-end">
              <Button onClick={handleAdd} disabled={adding || selected.size === 0}>
                {adding ? "Qo'shilmoqda..." : `Tanlanganlarni qo'shish (${selected.size})`}
              </Button>
            </div>
          </div>
        )}

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : members.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-400">Guruhda o'quvchi yo'q</p>
        ) : (
          <div className="space-y-1.5">
            {members.map((m) => (
              <div key={m.id} className="rounded-lg border border-slate-100 px-3 py-2">
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <p className="text-sm font-medium text-slate-700">{m.fullName}</p>
                    <p className="text-xs text-slate-400">{m.className}</p>
                  </div>
                  {canEdit && removingId !== m.id && (
                    <Button variant="ghost" onClick={() => setRemovingId(m.id)} title="Guruhdan chiqarish">
                      <UserMinus className="h-4 w-4" />
                    </Button>
                  )}
                </div>
                {canEdit && removingId === m.id && (
                  <div className="mt-2 space-y-2 rounded-lg bg-red-50 p-2">
                    <Textarea
                      label="Chiqarish sababi (ixtiyoriy)"
                      rows={2}
                      value={reason}
                      onChange={(e) => setReason(e.target.value)}
                    />
                    <div className="flex justify-end gap-2">
                      <Button
                        variant="secondary"
                        onClick={() => {
                          setRemovingId(null)
                          setReason('')
                        }}
                      >
                        Bekor qilish
                      </Button>
                      <Button variant="danger" onClick={() => handleRemove(m.id)} disabled={removing}>
                        {removing ? 'Chiqarilmoqda...' : 'Chiqarish'}
                      </Button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </Modal>
  )
}
