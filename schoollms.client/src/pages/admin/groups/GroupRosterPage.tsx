import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, ArrowRightLeft, History, Pencil, Send, Trash2 } from 'lucide-react'
import type { StudyGroupDetail, StudyGroupMember } from '@/api/services/groups'
import { getGroup, getGroupMembers, removeGroupMember } from '@/api/services/groups'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { GroupTransferModal } from './GroupTransferModal'
import { RosterMessageModal } from './RosterMessageModal'

/**
 * Guruh ro'yxati sahifasi — `students-parity.md` §2.1.1 (`/group/:id/students`).
 *
 * Ro'yxatdagi o'quvchini tanlab ota-onalariga Telegram xabari yuboriladi;
 * qatordan o'quvchi kartochkasiga o'tiladi. "Tarix" tugmasi chiqib ketganlarni
 * ham ko'rsatadi — a'zolik O'CHIRILMAYDI, yopiladi.
 */
export function GroupRosterPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [group, setGroup] = useState<StudyGroupDetail | null>(null)
  const [members, setMembers] = useState<StudyGroupMember[]>([])
  const [loading, setLoading] = useState(true)
  const [showHistory, setShowHistory] = useState(false)
  const [selected, setSelected] = useState<string[]>([])
  const [messageOpen, setMessageOpen] = useState(false)
  const [transferFor, setTransferFor] = useState<StudyGroupMember | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    Promise.all([getGroup(id), getGroupMembers(id, showHistory)])
      .then(([g, m]) => {
        setGroup(g)
        setMembers(m)
      })
      .finally(() => setLoading(false))
  }, [id, showHistory])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumot kelganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  const toggle = (studentId: string) =>
    setSelected((prev) =>
      prev.includes(studentId) ? prev.filter((x) => x !== studentId) : [...prev, studentId],
    )

  const activeMembers = members.filter((m) => !m.leftOn)

  const handleRemove = (m: StudyGroupMember) => {
    const reason = prompt(
      `"${m.fullName}" ni guruhdan chiqarasizmi?\nSabab (ixtiyoriy):`,
      '',
    )
    if (reason === null) return
    removeGroupMember(id, m.id, reason.trim() || undefined)
      .then(load)
      .catch((e) =>
        alert(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Chiqarib bo'lmadi",
        ),
      )
  }

  if (loading && !group) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button variant="ghost" onClick={() => navigate('/admin/groups')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">{group?.name}</h1>
            <p className="text-sm text-slate-400">
              {group?.subjectName} · {group?.classes.map((c) => c.name).join(', ')} ·{' '}
              {activeMembers.length} ta o'quvchi
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => setShowHistory((v) => !v)}>
            <History className="h-4 w-4" /> {showHistory ? 'Faqat faol' : 'Tarix bilan'}
          </Button>
          <Button
            variant="secondary"
            disabled={selected.length === 0}
            onClick={() => setMessageOpen(true)}
          >
            <Send className="h-4 w-4" /> Xabar ({selected.length})
          </Button>
          {!group?.isArchived && (
            <Button onClick={() => navigate(`/admin/groups/${id}`)}>
              <Pencil className="h-4 w-4" /> Tahrirlash
            </Button>
          )}
        </div>
      </div>

      {group?.isArchived && (
        <Card className="border-amber-200 bg-amber-50 text-sm text-amber-800">
          Guruh arxivda — ro'yxat faqat tarix sifatida ko'rinadi.
        </Card>
      )}

      <Card className="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="w-10 px-4 py-3"></th>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Sinf</th>
                <th className="px-4 py-3">Qo'shilgan</th>
                {showHistory && <th className="px-4 py-3">Chiqqan / sabab</th>}
                <th className="px-4 py-3 text-right">Amallar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {members.map((m) => (
                <tr key={m.id} className={cn('hover:bg-slate-50/60', m.leftOn && 'text-slate-400')}>
                  <td className="px-4 py-3">
                    {!m.leftOn && (
                      <input
                        type="checkbox"
                        className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                        checked={selected.includes(m.studentId)}
                        onChange={() => toggle(m.studentId)}
                      />
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <Link
                      to={`/admin/students/${m.studentId}`}
                      className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                    >
                      {m.fullName}
                    </Link>
                  </td>
                  <td className="px-4 py-3">{m.className || '—'}</td>
                  <td className="px-4 py-3">{m.joinedOn}</td>
                  {showHistory && (
                    <td className="px-4 py-3">
                      {m.leftOn ? `${m.leftOn}${m.leaveReason ? ` — ${m.leaveReason}` : ''}` : '—'}
                    </td>
                  )}
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-0.5">
                      {!m.leftOn && !group?.isArchived && (
                        <>
                          <button
                            type="button"
                            title="Boshqa guruhga o'tkazish"
                            onClick={() => setTransferFor(m)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <ArrowRightLeft className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title="Guruhdan chiqarish"
                            onClick={() => handleRemove(m)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {members.length === 0 && (
                <tr>
                  <td colSpan={showHistory ? 6 : 5} className="px-4 py-12 text-center text-slate-400">
                    Guruhda o'quvchi yo'q
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <RosterMessageModal
        open={messageOpen}
        studentIds={selected}
        onClose={() => setMessageOpen(false)}
      />

      <GroupTransferModal
        open={Boolean(transferFor)}
        memberId={transferFor?.id ?? ''}
        studentName={transferFor?.fullName ?? ''}
        subjectId={group?.subjectId ?? ''}
        currentGroupId={id}
        onClose={() => setTransferFor(null)}
        onDone={() => {
          setTransferFor(null)
          load()
        }}
      />
    </div>
  )
}
