import { useEffect, useState } from 'react'
import { MessageSquareWarning, Lightbulb, CheckCircle2, GraduationCap, Users, Building2, UserRound, Loader2, Settings2 } from 'lucide-react'
import type { Feedback } from '@/types'
import {
  FEEDBACK_STATUS,
  getFeedback,
  getFeedbackAssignees,
  getFeedbackDepartments,
  type FeedbackDepartment,
  type FeedbackDepartmentMember,
} from '@/api/services/feedback'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { FeedbackDetailModal } from './FeedbackDetailModal'
import { FeedbackDepartmentsModal } from './FeedbackDepartmentsModal'

type TypeFilter = '' | 'suggestion' | 'complaint'
type StatusFilter = '' | Feedback['status']

/**
 * Taklif va shikoyatlar — ota-onalar va o'qituvchilar Mini App orqali yuboradi. Ish tartibi
 * (mijoz, 2026-10-07): qabul qilindi → jarayonda → hal qilindi; bo'lim va mas'ulga yo'naltiriladi,
 * mas'ul botdan xabar oladi, yuboruvchi holat va javobni o'z ilovasida ko'radi.
 */
export function FeedbackPage() {
  const [items, setItems] = useState<Feedback[]>([])
  const [loading, setLoading] = useState(true)
  const [type, setType] = useState<TypeFilter>('')
  const [status, setStatus] = useState<StatusFilter>('')
  const [departmentId, setDepartmentId] = useState('')
  const [mine, setMine] = useState(false)
  const [departments, setDepartments] = useState<FeedbackDepartment[]>([])
  const [assignees, setAssignees] = useState<FeedbackDepartmentMember[]>([])
  const [opened, setOpened] = useState<Feedback | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)

  useEffect(() => {
    getFeedbackDepartments().then(setDepartments).catch(() => setDepartments([]))
    getFeedbackAssignees().then(setAssignees).catch(() => setAssignees([]))
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getFeedback(type, status, departmentId, mine)
      .then(setItems)
      .finally(() => setLoading(false))
  }, [type, status, departmentId, mine])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Taklif va shikoyatlar</h1>
          <p className="text-sm text-slate-400">Ota-onalar va o'qituvchilar ilova orqali yuborgan murojaatlar</p>
        </div>
        <Button variant="secondary" onClick={() => setSettingsOpen(true)}>
          <Settings2 className="h-4 w-4" /> Bo'limlar
        </Button>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Seg label="Hammasi" active={type === ''} onClick={() => setType('')} />
        <Seg label="Takliflar" active={type === 'suggestion'} onClick={() => setType('suggestion')} />
        <Seg label="Shikoyatlar" active={type === 'complaint'} onClick={() => setType('complaint')} />
        <span className="mx-1 w-px self-stretch bg-slate-200" />
        <Seg label="Barcha holat" active={status === ''} onClick={() => setStatus('')} />
        {(Object.keys(FEEDBACK_STATUS) as Feedback['status'][]).map((s) => (
          <Seg key={s} label={FEEDBACK_STATUS[s]} active={status === s} onClick={() => setStatus(s)} />
        ))}
        <span className="mx-1 w-px self-stretch bg-slate-200" />
        <select
          value={departmentId}
          onChange={(e) => setDepartmentId(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
        >
          <option value="">Barcha bo'limlar</option>
          {departments.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </select>
        <Seg label="Menga biriktirilgan" active={mine} onClick={() => setMine((v) => !v)} />
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : items.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Murojaatlar yo'q</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {items.map((f) => (
            <Card key={f.id} className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  {f.type === 'complaint' ? (
                    <span className="inline-flex items-center gap-1 rounded-md bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                      <MessageSquareWarning className="h-3.5 w-3.5" /> Shikoyat
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                      <Lightbulb className="h-3.5 w-3.5" /> Taklif
                    </span>
                  )}
                  {f.senderRole === 'teacher' ? (
                    <span className="inline-flex items-center gap-1 rounded-md bg-indigo-50 px-2 py-0.5 text-xs font-medium text-indigo-700">
                      <GraduationCap className="h-3.5 w-3.5" /> O'qituvchi
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">
                      <Users className="h-3.5 w-3.5" /> Ota-ona
                    </span>
                  )}
                  <StatusBadge status={f.status} />
                  {f.departmentName && (
                    <span className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                      <Building2 className="h-3.5 w-3.5" /> {f.departmentName}
                    </span>
                  )}
                  {f.assigneeName && (
                    <span className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                      <UserRound className="h-3.5 w-3.5" /> {f.assigneeName}
                    </span>
                  )}
                </div>
                <Button variant="secondary" onClick={() => setOpened(f)}>
                  Ochish
                </Button>
              </div>
              <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{f.text}</p>
              {f.imageUrl && (
                <a href={f.imageUrl} target="_blank" rel="noreferrer" className="block w-fit">
                  <img
                    src={f.imageUrl}
                    alt="Biriktirilgan rasm"
                    className="max-h-44 rounded-lg border border-slate-200 object-cover"
                  />
                </a>
              )}
              <div className="text-xs text-slate-400">
                {f.senderName || f.parentName || f.studentName || 'Noma\'lum'}
                {f.senderRole === 'parent' && f.studentName ? ` · farzandi: ${f.studentName}` : ''}
                {f.senderRole === 'parent' && f.className ? ` · ${f.className}` : ''}
                {' · '}
                {formatDate(f.createdAt)}
              </div>
              {f.answer && (
                <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                  <span className="font-medium">Javob:</span> {f.answer}
                </p>
              )}
            </Card>
          ))}
        </div>
      )}

      {opened && (
        <FeedbackDetailModal
          item={opened}
          departments={departments}
          assignees={assignees}
          onClose={() => setOpened(null)}
          onSaved={(saved) => {
            setItems((p) => p.map((x) => (x.id === saved.id ? saved : x)))
            setOpened(null)
          }}
        />
      )}
      {settingsOpen && (
        <FeedbackDepartmentsModal
          departments={departments}
          assignees={assignees}
          onClose={() => setSettingsOpen(false)}
          onChanged={setDepartments}
        />
      )}
    </div>
  )
}

function StatusBadge({ status }: { status: Feedback['status'] }) {
  if (status === 'resolved')
    return (
      <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
        <CheckCircle2 className="h-3.5 w-3.5" /> {FEEDBACK_STATUS.resolved}
      </span>
    )
  if (status === 'in_progress')
    return (
      <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
        <Loader2 className="h-3.5 w-3.5" /> {FEEDBACK_STATUS.in_progress}
      </span>
    )
  return (
    <span className="rounded-md bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">{FEEDBACK_STATUS.new}</span>
  )
}

function Seg({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
      )}
    >
      {label}
    </button>
  )
}
