import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { CheckCircle2, Loader2, RotateCcw, Save } from 'lucide-react'
import {
  seasonalErrorMessage,
  type SeasonalBulkRow,
  type SeasonalEntryRowDto,
} from '@/api/services/seasonalMarks'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import {
  SCORE_COLUMN_LABEL,
  commentProblem,
  formatScore,
  normalizeComment,
  parseScore,
} from './periods'
import { InlineError } from './StateViews'

/**
 * THE BULK-ENTRY GRID — one row per pupil, one score (0–100) and one comment
 * each, one Saqlash for all of them (§3.3 screen 11, §6.6 `POST /bulk`).
 *
 * Reused by the teacher panel (unit C5) — it knows nothing about which
 * endpoint it saves to: the caller passes `onSave`.
 *
 * WHAT IS SENT. Only rows whose value differs from what the server gave us.
 * A row that had a stored mark and is now empty in both fields is sent with
 * `score: null, comment: null`, which the server treats as "delete this mark".
 *
 * RESET. The grid owns its drafts and re-initialises them whenever `rows`
 * changes in content (e.g. the caller reloads after a save), so after a
 * successful save the caller only has to fetch the list again.
 */
export interface SeasonalEntryGridProps {
  /** The pupils of one (class, subject, period), stored marks prefilled. */
  rows: SeasonalEntryRowDto[]
  /** false → read-only: values are shown, no input, no Saqlash (§3.7). */
  canEdit: boolean
  /**
   * Persist the changed rows. Resolve when saved (then reload `rows`); reject
   * with the request's error and the grid shows it in Uzbek, drafts intact.
   */
  onSave: (changes: SeasonalBulkRow[]) => Promise<void>
  /** Told whenever the grid gains or loses unsaved edits (for a leave guard). */
  onDirtyChange?: (dirty: boolean) => void
  /** Inputs locked from outside, e.g. while the caller reloads `rows`. */
  busy?: boolean
}

export function SeasonalEntryGrid(props: SeasonalEntryGridProps) {
  // Content identity of the server rows: a new value remounts the body and
  // throws the drafts away — exactly what must happen after a save or when the
  // selection changes.
  const signature = useMemo(
    () =>
      props.rows
        .map((r) => `${r.studentId}|${r.markId ?? ''}|${r.score ?? ''}|${r.comment ?? ''}`)
        .join('\n'),
    [props.rows],
  )
  return <GridBody key={signature} {...props} />
}

interface Draft {
  score: string
  comment: string
}

interface RowState {
  row: SeasonalEntryRowDto
  draft: Draft
  scoreError: string | null
  commentError: string | null
  changed: boolean
  willDelete: boolean
  /** Present only when the row is valid and changed. */
  change: SeasonalBulkRow | null
}

function initialDrafts(rows: SeasonalEntryRowDto[]): Record<string, Draft> {
  const out: Record<string, Draft> = {}
  for (const r of rows) out[r.studentId] = { score: formatScore(r.score), comment: r.comment ?? '' }
  return out
}

function analyse(row: SeasonalEntryRowDto, draft: Draft): RowState {
  const parsed = parseScore(draft.score)
  const commentError = commentProblem(draft.comment)
  const scoreError = parsed.ok ? null : parsed.reason
  const comment = normalizeComment(draft.comment)
  const original = normalizeComment(row.comment)

  if (!parsed.ok || commentError) {
    return { row, draft, scoreError, commentError, changed: true, willDelete: false, change: null }
  }

  const changed = parsed.value !== row.score || comment !== original
  const willDelete = changed && row.markId !== null && parsed.value === null && comment === null
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

const cellInput =
  'rounded-lg border px-2.5 py-1.5 text-sm text-slate-800 outline-none transition-colors focus:ring-2 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400'

function GridBody({ rows, canEdit, onSave, onDirtyChange, busy }: SeasonalEntryGridProps) {
  const [drafts, setDrafts] = useState<Record<string, Draft>>(() => initialDrafts(rows))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const scoreRefs = useRef<(HTMLInputElement | null)[]>([])

  const states = useMemo(
    () => rows.map((r) => analyse(r, drafts[r.studentId] ?? { score: '', comment: '' })),
    [rows, drafts],
  )
  const changes = states.flatMap((s) => (s.change ? [s.change] : []))
  const invalidCount = states.filter((s) => s.scoreError || s.commentError).length
  const dirty = states.some((s) => s.changed)
  const locked = saving || Boolean(busy)

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  // Leaving the grid (remount, unmount) never leaves the caller thinking it is dirty.
  const dirtyCallback = useRef(onDirtyChange)
  useEffect(() => {
    dirtyCallback.current = onDirtyChange
  })
  useEffect(() => () => dirtyCallback.current?.(false), [])

  // A tab close or reload with unsaved scores asks first.
  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  const edit = (studentId: string, patch: Partial<Draft>) =>
    setDrafts((prev) => {
      const current = prev[studentId] ?? { score: '', comment: '' }
      return { ...prev, [studentId]: { ...current, ...patch } }
    })

  /** Enter / ↓ go to the next pupil's score, ↑ to the previous — fast typing down a column. */
  const onScoreKey = (e: KeyboardEvent<HTMLInputElement>, index: number) => {
    const next =
      e.key === 'Enter' || e.key === 'ArrowDown' ? index + 1 : e.key === 'ArrowUp' ? index - 1 : null
    if (next === null) return
    e.preventDefault()
    const target = scoreRefs.current[next]
    if (target) {
      target.focus()
      target.select()
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
    } catch (err: unknown) {
      setError(seasonalErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[40rem] text-left text-sm">
          <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="w-12 px-4 py-3 text-center">№</th>
              <th className="px-4 py-3">O'quvchi</th>
              <th className="px-4 py-3 normal-case">{SCORE_COLUMN_LABEL}</th>
              <th className="px-4 py-3">Izoh</th>
              <th className="w-32 px-4 py-3">Holat</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {states.map((s, index) => (
              <tr key={s.row.studentId} className={cn(s.changed && canEdit && 'bg-brand-50/40')}>
                <td className="px-4 py-2 text-center text-xs text-slate-400 tabular-nums">
                  {index + 1}
                </td>
                <td className="px-4 py-2 font-medium text-slate-800">
                  <span className="block max-w-[16rem] truncate" title={s.row.fullName}>
                    {s.row.fullName}
                  </span>
                </td>
                <td className="px-4 py-2 align-top">
                  {canEdit ? (
                    <div>
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
                        autoComplete="off"
                        placeholder="—"
                        aria-label={`${s.row.fullName}: ball (0–100)`}
                        aria-invalid={Boolean(s.scoreError)}
                        className={cn(
                          cellInput,
                          'w-24 text-center tabular-nums',
                          s.scoreError
                            ? 'border-red-300 bg-red-50 focus:border-red-400 focus:ring-red-100'
                            : 'border-slate-200 bg-white focus:border-brand-400 focus:ring-brand-100',
                        )}
                      />
                      {s.scoreError && (
                        <p className="mt-1 text-xs text-red-600">{s.scoreError}</p>
                      )}
                    </div>
                  ) : (
                    <span className="font-semibold tabular-nums text-slate-800">
                      {formatScore(s.row.score) || <span className="font-normal text-slate-300">—</span>}
                    </span>
                  )}
                </td>
                <td className="px-4 py-2 align-top">
                  {canEdit ? (
                    <div>
                      <input
                        value={s.draft.comment}
                        onChange={(e) => edit(s.row.studentId, { comment: e.target.value })}
                        disabled={locked}
                        autoComplete="off"
                        placeholder="Izoh (ixtiyoriy)"
                        aria-label={`${s.row.fullName}: izoh`}
                        aria-invalid={Boolean(s.commentError)}
                        className={cn(
                          cellInput,
                          'w-full min-w-[16rem]',
                          s.commentError
                            ? 'border-red-300 bg-red-50 focus:border-red-400 focus:ring-red-100'
                            : 'border-slate-200 bg-white focus:border-brand-400 focus:ring-brand-100',
                        )}
                      />
                      {s.commentError && (
                        <p className="mt-1 text-xs text-red-600">{s.commentError}</p>
                      )}
                    </div>
                  ) : (
                    <span className="block max-w-[24rem] truncate text-slate-600" title={s.row.comment ?? undefined}>
                      {s.row.comment ?? <span className="text-slate-300">—</span>}
                    </span>
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-2 align-top">
                  <RowStatus state={s} canEdit={canEdit} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {canEdit && (
        <div className="sticky bottom-0 z-10 space-y-2 rounded-b-2xl border-t border-slate-100 bg-white/95 px-4 py-3 backdrop-blur">
          {error && <InlineError message={error} />}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs text-slate-500">
              {invalidCount > 0 ? (
                <span className="font-medium text-red-600">
                  {invalidCount} ta qatorda xato bor — tuzatmaguncha saqlab bo'lmaydi
                </span>
              ) : changes.length > 0 ? (
                <span className="font-medium text-brand-700">
                  {changes.length} ta o'zgarish saqlanmagan
                </span>
              ) : (
                "Saqlanmagan o'zgarish yo'q"
              )}
              <span className="mt-0.5 block text-slate-400">
                Ball ham, izoh ham bo'shatilsa — saqlashda o'quvchining shu davrdagi bahosi
                o'chiriladi.
              </span>
            </p>
            <div className="flex items-center gap-2">
              <Button variant="ghost" onClick={reset} disabled={!dirty || locked}>
                <RotateCcw className="h-4 w-4" /> Bekor qilish
              </Button>
              <Button onClick={save} disabled={changes.length === 0 || invalidCount > 0 || locked}>
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                {saving ? 'Saqlanmoqda...' : 'Saqlash'}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function RowStatus({ state, canEdit }: { state: RowState; canEdit: boolean }) {
  if (canEdit && state.changed) {
    if (state.scoreError || state.commentError) {
      return <span className="text-xs font-medium text-red-600">Xato</span>
    }
    if (state.willDelete) {
      return (
        <span className="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
          O'chiriladi
        </span>
      )
    }
    return (
      <span className="rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
        {state.row.markId === null ? 'Yangi' : "O'zgardi"}
      </span>
    )
  }
  if (state.row.markId !== null) {
    return (
      <span className="inline-flex items-center gap-1 text-xs text-emerald-600">
        <CheckCircle2 className="h-3.5 w-3.5" /> Saqlangan
      </span>
    )
  }
  return <span className="text-xs text-slate-300">Baholanmagan</span>
}
