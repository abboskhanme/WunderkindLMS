import { useEffect, useState } from 'react'
import { Check, X, Paperclip, Save } from 'lucide-react'
import type { AssignmentResult, SubmissionRow } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

type Filter = 'all' | 'done' | 'pending'

interface Props {
  open: boolean
  onClose: () => void
  title: string
  fetchResult: () => Promise<AssignmentResult>
  /**
   * Faqat o'qituvchi uchun — o'quvchi holatini va ballini saqlash (admin uchun berilmaydi → faqat ko'rish).
   * completed = bajardimi, score = qo'yilgan ball (yo'q bo'lsa null).
   */
  onSave?: (studentId: string, completed: boolean, score: number | null) => Promise<void>
}

export function SubmissionsModal({ open, onClose, title, fetchResult, onSave }: Props) {
  const [result, setResult] = useState<AssignmentResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<Filter>('all')
  const [busyId, setBusyId] = useState<string | null>(null)
  // O'qituvchi tahrirlayotgan ball qoralamalari (studentId -> matn).
  const [draft, setDraft] = useState<Record<string, string>>({})

  const editable = !!onSave

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda natijalarni yuklash (maqsadli)
    setLoading(true)
    setFilter('all')
    fetchResult()
      .then((res) => {
        setResult(res)
        setDraft(Object.fromEntries(res.rows.map((r) => [r.studentId, r.score?.toString() ?? ''])))
      })
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat ochilganda yuklaymiz
  }, [open])

  const allRows = result?.rows ?? []
  const total = allRows.length
  const done = allRows.filter((r) => r.completed).length
  const pct = total > 0 ? Math.round((done / total) * 100) : 0
  const maxScore = result?.maxScore ?? 100
  const isFileFormat = result?.format === 'file' || result?.format === 'video'

  const rows = allRows.filter((r) =>
    filter === 'all' ? true : filter === 'done' ? r.completed : !r.completed,
  )

  const patchRow = (studentId: string, patch: Partial<SubmissionRow>) =>
    setResult((prev) =>
      prev ? { ...prev, rows: prev.rows.map((r) => (r.studentId === studentId ? { ...r, ...patch } : r)) } : prev,
    )

  // Bajardi/Bajarmadi — ballni saqlab qoladi (avto-baholangan testni o'chirib yubormaslik uchun).
  const onToggleRow = async (r: SubmissionRow) => {
    if (!onSave || busyId) return
    const next = !r.completed
    setBusyId(r.studentId)
    try {
      await onSave(r.studentId, next, r.score)
      patchRow(r.studentId, {
        completed: next,
        submittedAt: next ? r.submittedAt ?? new Date().toISOString() : null,
      })
    } finally {
      setBusyId(null)
    }
  }

  // Ballni saqlash (bo'sh = ballni olib tashlash). Ball qo'yilsa — bajargan deb belgilanadi.
  const onSaveScore = async (r: SubmissionRow) => {
    if (!onSave || busyId) return
    const raw = (draft[r.studentId] ?? '').trim()
    if (raw !== '' && Number.isNaN(Number(raw))) return
    const parsed = raw === '' ? null : Math.max(0, Math.round(Number(raw)))
    setBusyId(r.studentId)
    try {
      await onSave(r.studentId, true, parsed)
      patchRow(r.studentId, { score: parsed, completed: true })
    } finally {
      setBusyId(null)
    }
  }

  return (
    <Modal open={open} onClose={onClose} size="lg" title={title}>
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !result ? (
        <p className="py-10 text-center text-slate-400">Ma'lumot yo'q</p>
      ) : (
        <div className="space-y-4">
          {/* Xulosa + progress */}
          <div>
            <div className="mb-1 flex items-center justify-between text-sm">
              <span className="font-medium text-slate-700">
                {done} / {total} bajardi
              </span>
              <span className="font-semibold text-emerald-600">{pct}%</span>
            </div>
            <div className="h-2 w-full overflow-hidden rounded-full bg-slate-100">
              <div className="h-full rounded-full bg-emerald-500 transition-all" style={{ width: `${pct}%` }} />
            </div>
          </div>

          {/* Filtr */}
          <div className="flex flex-wrap gap-2">
            <FilterChip active={filter === 'all'} onClick={() => setFilter('all')} label={`Hammasi ${total}`} />
            <FilterChip
              active={filter === 'done'}
              onClick={() => setFilter('done')}
              label={`Bajardi ${done}`}
              tone="emerald"
            />
            <FilterChip
              active={filter === 'pending'}
              onClick={() => setFilter('pending')}
              label={`Bajarmadi ${total - done}`}
              tone="red"
            />
          </div>

          {/* Ro'yxat */}
          <div className="space-y-1.5">
            {rows.length === 0 && (
              <p className="py-8 text-center text-sm text-slate-400">
                {total === 0 ? "Bu topshiriqda o'quvchi yo'q" : "Bu filtrda o'quvchi yo'q"}
              </p>
            )}
            {rows.map((r) => {
              const dirty = (draft[r.studentId] ?? '') !== (r.score?.toString() ?? '')
              return (
                <div key={r.studentId} className="rounded-lg border border-slate-100 px-3 py-2">
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex min-w-0 items-center gap-2.5">
                      <span
                        className={cn(
                          'flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold',
                          r.completed ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-500',
                        )}
                      >
                        {r.studentName.charAt(0)}
                      </span>
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-slate-800">{r.studentName}</p>
                        <p className="text-xs text-slate-400">
                          {r.className}
                          {r.completed && r.submittedAt ? ` · ${formatDateTime(r.submittedAt)}` : ''}
                        </p>
                      </div>
                    </div>

                    <div className="flex shrink-0 items-center gap-2">
                      {/* Ball — o'qituvchi tahrirlaydi, admin faqat ko'radi */}
                      {r.completed &&
                        (editable ? (
                          <div className="flex items-center gap-1">
                            <input
                              type="number"
                              min={0}
                              max={maxScore}
                              value={draft[r.studentId] ?? ''}
                              onChange={(e) =>
                                setDraft((prev) => ({ ...prev, [r.studentId]: e.target.value }))
                              }
                              onKeyDown={(e) => {
                                if (e.key === 'Enter') onSaveScore(r)
                              }}
                              placeholder="ball"
                              className="w-16 rounded-lg border border-slate-200 px-2 py-1 text-right text-sm outline-none focus:border-brand-400"
                            />
                            <span className="text-xs text-slate-400">/ {maxScore}</span>
                            {dirty && (
                              <button
                                type="button"
                                disabled={busyId === r.studentId}
                                onClick={() => onSaveScore(r)}
                                title="Ballni saqlash"
                                className="rounded-lg border border-emerald-300 bg-emerald-50 p-1 text-emerald-700 transition-colors hover:bg-emerald-100 disabled:opacity-50"
                              >
                                <Save className="h-3.5 w-3.5" />
                              </button>
                            )}
                          </div>
                        ) : (
                          <span
                            className={cn(
                              'rounded-full px-2 py-0.5 text-xs font-semibold',
                              r.score != null ? 'bg-brand-50 text-brand-700' : 'bg-slate-100 text-slate-400',
                            )}
                          >
                            {r.score != null ? `${r.score} / ${maxScore} ball` : 'baholanmagan'}
                          </span>
                        ))}

                      {/* Holat — o'qituvchi o'zgartiradi, admin faqat ko'radi */}
                      {editable ? (
                        <button
                          type="button"
                          disabled={busyId === r.studentId}
                          onClick={() => onToggleRow(r)}
                          className={cn(
                            'inline-flex shrink-0 items-center gap-1 rounded-lg border px-2.5 py-1 text-xs font-medium transition-colors disabled:opacity-50',
                            r.completed
                              ? 'border-emerald-300 bg-emerald-50 text-emerald-700 hover:bg-emerald-100'
                              : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                          )}
                        >
                          {r.completed ? <Check className="h-3.5 w-3.5" /> : <X className="h-3.5 w-3.5" />}
                          {r.completed ? 'Bajardi' : 'Bajarmadi'}
                        </button>
                      ) : (
                        <span
                          className={cn(
                            'inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
                            r.completed ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
                          )}
                        >
                          {r.completed ? <Check className="h-3 w-3" /> : null}
                          {r.completed ? 'Bajardi' : 'Bajarmadi'}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* O'quvchi yuborgan javob: yozma matn yoki fayl havolasi */}
                  {r.completed && r.answerText && (
                    <p className="mt-2 whitespace-pre-wrap rounded-md bg-slate-50 px-2.5 py-1.5 text-xs text-slate-600">
                      {r.answerText}
                    </p>
                  )}
                  {r.completed && isFileFormat && r.fileUrl && (
                    <a
                      href={r.fileUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="mt-2 inline-flex items-center gap-1 rounded-md border border-slate-200 px-2.5 py-1 text-xs font-medium text-brand-700 transition-colors hover:bg-brand-50"
                    >
                      <Paperclip className="h-3.5 w-3.5" /> Yuborilgan faylni ochish
                    </a>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}
    </Modal>
  )
}

function FilterChip({
  active,
  onClick,
  label,
  tone = 'brand',
}: {
  active: boolean
  onClick: () => void
  label: string
  tone?: 'brand' | 'emerald' | 'red'
}) {
  const activeCls =
    tone === 'emerald'
      ? 'bg-emerald-600 text-white'
      : tone === 'red'
        ? 'bg-red-500 text-white'
        : 'bg-brand-600 text-white'
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-full px-3 py-1 text-xs font-medium transition-colors',
        active ? activeCls : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
      )}
    >
      {label}
    </button>
  )
}

function formatDateTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}
