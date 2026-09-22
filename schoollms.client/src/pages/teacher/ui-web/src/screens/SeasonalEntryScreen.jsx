import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AlertCircle, BarChart3, CheckCircle2, Loader2, Plus, Users } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import {
  SCORE_LABEL,
  commentProblem,
  formatScore,
  normalizeComment,
  parseScore,
  periodCaption,
  periodKey,
  periodKindLabel,
  periodQuery,
  saveSummary,
  seasonalErrorMessage,
  seasonalErrorView,
  validPeriod,
} from '../lib/seasonal'

// Mavsumiy baholash — bulk entry for one (class, subject, period)
// (admission-and-testing.md §3.3 screen 11 via §3.4 screen 14, §6.6 `POST /bulk`).
//
// Behaviour mirrors the admin grid (`admin/seasonal-marks/SeasonalEntryGrid.tsx`):
//  • the pupils load with any stored score and comment prefilled;
//  • only rows that differ from what the server sent are saved, in one request;
//  • a stored row emptied in BOTH fields is sent as `{ score: null, comment: null }`,
//    which the server treats as "delete this mark";
//  • a score outside 0–100 and a 1–2 character comment are refused here, not clamped.
export default function SeasonalEntryScreen({ params, onBack }) {
  const { classId, className, subjectId, subjectName } = params || {}
  const period = validPeriod(params?.period)
  const title = className && subjectName ? `${className} · ${subjectName}` : 'Mavsumiy baholash'

  if (!classId || !subjectId || !period) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <ScreenHeader title="Mavsumiy baholash" onBack={onBack} titleSize={16} />
        <EmptyState
          icon={<EmptyIllustration><BarChart3 size={30} /></EmptyIllustration>}
          title="Sinf tanlanmagan"
          subtitle="Avval davr, sinf va fanni tanlang."
        />
      </div>
    )
  }

  return (
    <EntryScreen
      classId={classId}
      subjectId={subjectId}
      period={period}
      title={title}
      onBack={onBack}
    />
  )
}

function EntryScreen({ classId, subjectId, period, title, onBack }) {
  const key = periodKey(period)
  const studentsQ = useFetch(
    () => api.seasonalStudents({ classId, subjectId, ...periodQuery(period) }),
    [classId, subjectId, key],
  )
  const rows = studentsQ.data

  const [dirty, setDirty] = useState(false)
  const [leaveOpen, setLeaveOpen] = useState(false)
  // Survives the grid's remount after a save; cleared by the next edit.
  const [notice, setNotice] = useState(null)

  const onDirtyChange = useCallback((d) => {
    setDirty(d)
    if (d) setNotice(null)
  }, [])

  const save = async (changes) => {
    const result = await api.seasonalBulk({ classId, subjectId, ...periodQuery(period), rows: changes })
    setNotice(saveSummary(result))
    studentsQ.reload()
  }

  // The header back button asks before unsaved scores are thrown away.
  // (The device back gesture is handled by App.jsx's history stack and cannot be held here;
  // a tab close or reload is caught by `beforeunload` inside the grid.)
  const back = onBack ? () => (dirty ? setLeaveOpen(true) : onBack()) : null

  const count = Array.isArray(rows) ? rows.length : 0
  const subtitle = `${periodKindLabel(period.kind)}, ${periodCaption(period)}${count ? ` · ${count} o'quvchi` : ''}`

  let body
  if (studentsQ.loading && !rows) {
    body = <Loading label="O'quvchilar yuklanmoqda…" />
  } else if (studentsQ.error) {
    body = <ErrorState error={seasonalErrorView(studentsQ.error)} onRetry={studentsQ.reload} />
  } else if (!rows || rows.length === 0) {
    body = (
      <EmptyState
        icon={<EmptyIllustration><Users size={30} /></EmptyIllustration>}
        title="O'quvchilar yo'q"
        subtitle="Bu sinfda o'quvchi topilmadi."
      />
    )
  } else {
    body = (
      <EntryGrid
        rows={rows}
        busy={studentsQ.loading}
        notice={notice}
        onSave={save}
        onDirtyChange={onDirtyChange}
      />
    )
  }

  return (
    <div className="relative h-full flex flex-col bg-bg">
      <ScreenHeader title={title} subtitle={subtitle} onBack={back} titleSize={16} />
      {body}

      <AppSheet open={leaveOpen} onClose={() => setLeaveOpen(false)} title="Saqlanmagan o'zgarishlar">
        <div className="px-5 pb-6">
          <p className="text-[14px] text-muted leading-relaxed">
            Kiritilgan ballar hali saqlanmagan. Orqaga qaytsangiz, ular yo'qoladi.
          </p>
          <div className="mt-5 flex gap-2.5">
            <AppButton label="Qolish" style="ghost" expand onClick={() => setLeaveOpen(false)} />
            <div className="flex-[2]">
              <AppButton
                label="Tashlab ketish"
                style="danger"
                expand
                onClick={() => {
                  setLeaveOpen(false)
                  onBack?.()
                }}
              />
            </div>
          </div>
        </div>
      </AppSheet>
    </div>
  )
}

// Content identity of the server rows: a new value remounts the body and throws
// the drafts away — exactly what must happen after a save reloads the list.
function EntryGrid(props) {
  const signature = useMemo(
    () => props.rows.map((r) => `${r.studentId}|${r.markId ?? ''}|${r.score ?? ''}|${r.comment ?? ''}`).join('\n'),
    [props.rows],
  )
  return <GridBody key={signature} {...props} />
}

function initialDrafts(rows) {
  const out = {}
  for (const r of rows) out[r.studentId] = { score: formatScore(r.score), comment: r.comment ?? '' }
  return out
}

// One row's validation and the change it would send (null = nothing to send).
function analyse(row, draft) {
  const parsed = parseScore(draft.score)
  const commentError = commentProblem(draft.comment)
  const scoreError = parsed.ok ? null : parsed.reason
  if (scoreError || commentError) {
    return { row, draft, scoreError, commentError, changed: true, willDelete: false, change: null }
  }
  const comment = normalizeComment(draft.comment)
  const original = normalizeComment(row.comment)
  const stored = row.score === null || row.score === undefined ? null : Number(row.score)
  const changed = parsed.value !== stored || comment !== original
  const willDelete = changed && row.markId != null && parsed.value === null && comment === null
  return {
    row,
    draft,
    scoreError: null,
    commentError: null,
    changed,
    willDelete,
    change: changed ? { studentId: row.studentId, score: parsed.value, comment } : null,
  }
}

function GridBody({ rows, busy, notice, onSave, onDirtyChange }) {
  const [drafts, setDrafts] = useState(() => initialDrafts(rows))
  // Comment fields start closed unless there is a stored comment — the list stays one line
  // per pupil. Once open, a field stays open, so clearing it does not pull it from under the cursor.
  const [openComments, setOpenComments] = useState(
    () => new Set(rows.filter((r) => normalizeComment(r.comment) !== null).map((r) => r.studentId)),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const scoreRefs = useRef([])

  const states = useMemo(
    () => rows.map((r) => analyse(r, drafts[r.studentId] || { score: '', comment: '' })),
    [rows, drafts],
  )
  const changes = states.flatMap((s) => (s.change ? [s.change] : []))
  const invalidCount = states.filter((s) => s.scoreError || s.commentError).length
  const dirty = states.some((s) => s.changed)
  const locked = saving || Boolean(busy)

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  // Leaving the grid (remount after save, unmount) never leaves the parent thinking it is dirty.
  const dirtyRef = useRef(onDirtyChange)
  useEffect(() => {
    dirtyRef.current = onDirtyChange
  })
  useEffect(() => () => dirtyRef.current?.(false), [])

  // Closing the tab or reloading with unsaved scores asks first.
  useEffect(() => {
    if (!dirty) return undefined
    const onBeforeUnload = (e) => {
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  const edit = (studentId, patch) =>
    setDrafts((prev) => ({ ...prev, [studentId]: { ...(prev[studentId] || { score: '', comment: '' }), ...patch } }))

  const openComment = (studentId) => setOpenComments((prev) => new Set(prev).add(studentId))

  // Enter / ↓ go to the next pupil's score, ↑ to the previous — fast typing down the list.
  const onScoreKey = (e, index) => {
    const next = e.key === 'Enter' || e.key === 'ArrowDown' ? index + 1 : e.key === 'ArrowUp' ? index - 1 : null
    if (next === null) return
    e.preventDefault()
    const target = scoreRefs.current[next]
    if (target) {
      target.focus()
      target.select()
    } else if (e.key === 'Enter') {
      e.currentTarget.blur()
    }
  }

  const reset = () => {
    setDrafts(initialDrafts(rows))
    setError(null)
  }

  const save = async () => {
    if (changes.length === 0 || invalidCount > 0 || locked) return
    setSaving(true)
    setError(null)
    try {
      await onSave(changes)
    } catch (err) {
      setError(seasonalErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  const canSave = changes.length > 0 && invalidCount === 0 && !locked

  return (
    <>
      <div className="px-4 pt-1 pb-1.5 flex items-center gap-3 text-[12px] font-bold text-muted">
        <span className="flex-1">O'quvchi</span>
        {busy && <Loader2 size={14} className="text-primary animate-spin" />}
        <span className="w-[84px] text-center">{SCORE_LABEL}</span>
      </div>

      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-4 space-y-2">
        {states.map((s, index) => {
          const showComment = openComments.has(s.row.studentId)
          const invalid = Boolean(s.scoreError || s.commentError)
          return (
            <div
              key={s.row.studentId}
              className={[
                'p-3 rounded-3xl bg-surface border',
                invalid ? 'border-danger' : s.changed ? 'border-primary' : 'border-border',
              ].join(' ')}
            >
              <div className="flex items-center gap-3">
                <span className="w-6 shrink-0 text-[11px] font-bold text-faint font-mono">{index + 1}.</span>
                <div className="flex-1 min-w-0">
                  <p className="text-[14px] font-semibold text-text leading-tight truncate">{s.row.fullName}</p>
                  <div className="mt-0.5 flex items-center gap-2">
                    <RowStatus state={s} />
                    {!showComment && (
                      <button
                        type="button"
                        disabled={locked}
                        onClick={() => openComment(s.row.studentId)}
                        className="flex items-center gap-0.5 text-[11px] font-semibold text-primary disabled:opacity-50"
                      >
                        <Plus size={12} /> Izoh
                      </button>
                    )}
                  </div>
                </div>
                <input
                  ref={(el) => {
                    scoreRefs.current[index] = el
                  }}
                  value={s.draft.score}
                  onChange={(e) => edit(s.row.studentId, { score: e.target.value })}
                  onKeyDown={(e) => onScoreKey(e, index)}
                  onFocus={(e) => e.currentTarget.select()}
                  disabled={locked}
                  inputMode="decimal"
                  enterKeyHint="next"
                  autoComplete="off"
                  placeholder="—"
                  aria-label={`${s.row.fullName}: ${SCORE_LABEL}`}
                  aria-invalid={Boolean(s.scoreError)}
                  className={[
                    'w-[84px] h-11 shrink-0 rounded-xl border text-center text-[17px] font-extrabold font-mono text-text outline-none placeholder:text-faint disabled:opacity-50',
                    s.scoreError ? 'border-danger bg-danger/10' : 'border-border bg-surface2 focus:border-primary',
                  ].join(' ')}
                />
              </div>

              {showComment && (
                <input
                  value={s.draft.comment}
                  onChange={(e) => edit(s.row.studentId, { comment: e.target.value })}
                  disabled={locked}
                  autoComplete="off"
                  placeholder="Izoh (ixtiyoriy)"
                  aria-label={`${s.row.fullName}: izoh`}
                  aria-invalid={Boolean(s.commentError)}
                  className={[
                    'mt-2 w-full h-10 px-3 rounded-xl border text-[13px] text-text outline-none placeholder:text-faint disabled:opacity-50',
                    s.commentError ? 'border-danger bg-danger/10' : 'border-border bg-surface2 focus:border-primary',
                  ].join(' ')}
                />
              )}

              {(s.scoreError || s.commentError) && (
                <p className="mt-1.5 text-[11px] font-semibold text-danger">{s.scoreError || s.commentError}</p>
              )}
            </div>
          )
        })}
      </div>

      <div className="shrink-0 px-4 pt-3 pb-4 bg-surface border-t border-border space-y-2.5">
        {error && (
          <div className="px-3 py-2.5 rounded-xl bg-danger/10 flex items-center gap-2 text-[13px] font-semibold text-danger">
            <AlertCircle size={16} className="shrink-0" /> {error}
          </div>
        )}
        <div className="text-[12px]">
          {invalidCount > 0 ? (
            <p className="font-semibold text-danger">
              {invalidCount} ta qatorda xato bor — tuzatmaguncha saqlab bo'lmaydi
            </p>
          ) : changes.length > 0 ? (
            <p className="font-semibold text-primary">{changes.length} ta o'zgarish saqlanmagan</p>
          ) : notice ? (
            <p className="flex items-center gap-1.5 font-semibold text-success">
              <CheckCircle2 size={14} /> {notice}
            </p>
          ) : (
            <p className="text-muted">Saqlanmagan o'zgarish yo'q</p>
          )}
          <p className="mt-0.5 text-[11px] text-faint">
            Ball ham, izoh ham bo'shatilsa — saqlashda o'quvchining shu davrdagi bahosi o'chiriladi.
          </p>
        </div>
        <div className="flex gap-2.5">
          <AppButton label="Bekor qilish" style="ghost" expand disabled={!dirty || locked} onClick={reset} />
          <div className="flex-[2]">
            <AppButton label="Saqlash" expand loading={saving} disabled={!canSave && !saving} onClick={save} />
          </div>
        </div>
      </div>
    </>
  )
}

function RowStatus({ state }) {
  if (state.changed) {
    if (state.scoreError || state.commentError) {
      return <span className="text-[11px] font-semibold text-danger">Xato</span>
    }
    if (state.willDelete) return <span className="text-[11px] font-semibold text-warning">O'chiriladi</span>
    return (
      <span className="text-[11px] font-semibold text-primary">
        {state.row.markId == null ? 'Yangi' : "O'zgardi"}
      </span>
    )
  }
  if (state.row.markId != null) {
    return (
      <span className="flex items-center gap-1 text-[11px] font-semibold text-success">
        <CheckCircle2 size={12} /> Saqlangan
      </span>
    )
  }
  return <span className="text-[11px] text-faint">Baholanmagan</span>
}
