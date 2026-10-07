import { useEffect, useState } from 'react'
import { Clock } from 'lucide-react'
import type { Feedback } from '@/types'
import {
  FEEDBACK_STATUS,
  getFeedbackItem,
  updateFeedback,
  type FeedbackDepartment,
  type FeedbackDepartmentMember,
  type FeedbackEvent,
} from '@/api/services/feedback'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatDate } from '@/lib/utils'

interface Props {
  item: Feedback
  departments: FeedbackDepartment[]
  assignees: FeedbackDepartmentMember[]
  onClose: () => void
  onSaved: (item: Feedback) => void
}

/**
 * One murojaat: the text, who sent it, and one step of work — status, department, responsible
 * person, the answer the sender sees, an internal note. The history below shows every step.
 * The bot tells a newly named person (or department) and tells the sender about status and answer.
 */
export function FeedbackDetailModal({ item, departments, assignees, onClose, onSaved }: Props) {
  const [history, setHistory] = useState<FeedbackEvent[] | null>(null)
  const [status, setStatus] = useState<Feedback['status']>(item.status)
  const [departmentId, setDepartmentId] = useState(item.departmentId ?? '')
  const [assigneeId, setAssigneeId] = useState(item.assigneeId ?? '')
  const [answer, setAnswer] = useState('')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    getFeedbackItem(item.id)
      .then((d) => {
        if (active) setHistory(d.history)
      })
      .catch(() => {
        if (active) setHistory([])
      })
    return () => {
      active = false
    }
  }, [item.id])

  // People of the chosen department first — they are who the item is for.
  const members = new Set(departments.find((d) => d.id === departmentId)?.members.map((m) => m.userId) ?? [])
  const people = [...assignees].sort((a, b) => Number(members.has(b.userId)) - Number(members.has(a.userId)))

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      const saved = await updateFeedback(item.id, {
        status: status !== item.status ? status : undefined,
        departmentId: departmentId && departmentId !== item.departmentId ? departmentId : undefined,
        assigneeId: assigneeId && assigneeId !== item.assigneeId ? assigneeId : undefined,
        clearAssignee: !assigneeId && !!item.assigneeId,
        answer: answer.trim() || undefined,
        note: note.trim() || undefined,
      })
      onSaved(saved)
    } catch (err) {
      setError(hrErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const sender =
    item.senderRole === 'teacher'
      ? `O'qituvchi: ${item.senderName}`
      : `Ota-ona: ${item.senderName || item.parentName || "noma'lum"}${item.studentName ? ` · ${item.studentName}` : ''}${item.className ? `, ${item.className}` : ''}`

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={item.type === 'complaint' ? 'Shikoyat' : 'Taklif'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={save} disabled={saving}>
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="space-y-2">
          <p className="text-xs text-slate-400">
            {sender} · {formatDate(item.createdAt)}
          </p>
          <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{item.text}</p>
          {item.imageUrl && (
            <a href={item.imageUrl} target="_blank" rel="noreferrer" className="block w-fit">
              <img src={item.imageUrl} alt="Biriktirilgan rasm" className="max-h-44 rounded-lg border border-slate-200 object-cover" />
            </a>
          )}
          {item.answer && (
            <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
              <span className="font-medium">Berilgan javob:</span> {item.answer}
            </p>
          )}
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <Select label="Holat" value={status} onChange={(e) => setStatus(e.target.value as Feedback['status'])}>
            {(Object.keys(FEEDBACK_STATUS) as Feedback['status'][]).map((s) => (
              <option key={s} value={s}>
                {FEEDBACK_STATUS[s]}
              </option>
            ))}
          </Select>
          <Select label="Bo'lim" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
            <option value="">— tanlanmagan —</option>
            {departments
              .filter((d) => d.isActive || d.id === item.departmentId)
              .map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
          </Select>
          <Select label="Mas'ul" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
            <option value="">— bo'lim (hammasi) —</option>
            {people.map((p) => (
              <option key={p.userId} value={p.userId}>
                {p.fullName}
                {members.has(p.userId) ? ' ★' : ''}
                {p.hasAccess ? '' : ' (ruxsati yo\'q)'}
              </option>
            ))}
          </Select>
        </div>

        <Textarea
          label="Javob (yuboruvchiga botda boradi va ilovasida ko'rinadi)"
          rows={3}
          maxLength={2000}
          value={answer}
          onChange={(e) => setAnswer(e.target.value)}
          placeholder="masalan: Muammo hal qilindi, rahmat!"
        />
        <Textarea
          label="Ichki izoh (faqat maktab ko'radi)"
          rows={2}
          maxLength={2000}
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

        <div>
          <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Tarix</p>
          {history === null ? (
            <Loader label="Yuklanmoqda..." />
          ) : history.length === 0 ? (
            <p className="text-sm text-slate-400">Tarix yo'q (eski murojaat)</p>
          ) : (
            <ol className="space-y-2">
              {history.map((h) => (
                <li key={h.id} className="flex gap-2 text-sm">
                  <Clock className="mt-0.5 h-4 w-4 shrink-0 text-slate-300" />
                  <div className="min-w-0">
                    <p className="text-slate-700">
                      {[
                        h.statusLabel,
                        h.departmentName && `bo'lim: ${h.departmentName}`,
                        h.assigneeName && `mas'ul: ${h.assigneeName}`,
                      ]
                        .filter(Boolean)
                        .join(' · ') || 'Izoh'}
                    </p>
                    {h.note && (
                      <p className="whitespace-pre-wrap break-words text-slate-500">
                        {h.isPublic ? 'Javob: ' : ''}
                        {h.note}
                      </p>
                    )}
                    <p className="text-xs text-slate-400">
                      {h.actorName} · {formatDate(h.createdAt)}
                    </p>
                  </div>
                </li>
              ))}
            </ol>
          )}
        </div>
      </div>
    </Modal>
  )
}
