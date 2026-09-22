import { useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { Loader2, Pencil } from 'lucide-react'
import { cn } from '@/lib/utils'
import { commentProblem, formatScore, normalizeComment, parseScore } from './periods'

/**
 * In-cell editors of the list (§3.3 screen 10): click the value, type, and
 * it is saved on Enter or when the field loses focus; Escape puts it back.
 *
 * `onCommit` resolves `true` when the server accepted the value. A value the
 * rules refuse (score outside 0–100, a 1–2 character comment) is reported
 * through `onInvalid` and never sent.
 */

const editorBase =
  'rounded-lg border border-brand-300 bg-white px-2 py-1 text-sm text-slate-800 outline-none ring-2 ring-brand-100 disabled:bg-slate-50'

interface InlineScoreCellProps {
  value: number | null
  editable: boolean
  onCommit: (next: number | null) => Promise<boolean>
  onInvalid: (message: string) => void
  label: string
}

export function InlineScoreCell({ value, editable, onCommit, onInvalid, label }: InlineScoreCellProps) {
  const [draft, setDraft] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /** Enter followed by the blur it causes must not save twice. */
  const settling = useRef(false)

  const shown = formatScore(value)

  if (!editable) {
    return shown ? (
      <span className="font-semibold tabular-nums text-slate-800">{shown}</span>
    ) : (
      <span className="text-slate-300">—</span>
    )
  }

  if (draft === null) {
    return (
      <button
        type="button"
        onClick={() => {
          settling.current = false
          setError(null)
          setDraft(shown)
        }}
        title="Ballni o'zgartirish"
        aria-label={`${label}: ballni o'zgartirish`}
        className="group inline-flex min-w-[3.5rem] items-center gap-1.5 rounded-md px-1.5 py-0.5 text-left transition-colors hover:bg-brand-50"
      >
        <span className={cn('tabular-nums', shown ? 'font-semibold text-slate-800' : 'text-slate-300')}>
          {shown || '—'}
        </span>
        <Pencil className="h-3 w-3 text-slate-300 opacity-0 transition-opacity group-hover:opacity-100" />
      </button>
    )
  }

  const finish = async (source: 'enter' | 'blur') => {
    if (settling.current) return
    const parsed = parseScore(draft)
    if (!parsed.ok) {
      if (source === 'enter') {
        setError(parsed.reason)
        return
      }
      settling.current = true
      onInvalid(`${parsed.reason} — o'zgarish saqlanmadi`)
      setDraft(null)
      return
    }
    settling.current = true
    if (parsed.value === value) {
      setDraft(null)
      return
    }
    setSaving(true)
    await onCommit(parsed.value)
    setSaving(false)
    setDraft(null)
  }

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault()
      void finish('enter')
    } else if (e.key === 'Escape') {
      e.preventDefault()
      settling.current = true
      setDraft(null)
    }
  }

  return (
    <div className="flex items-center gap-1.5">
      <input
        autoFocus
        value={draft}
        onChange={(e) => {
          setError(null)
          setDraft(e.target.value)
        }}
        onKeyDown={onKey}
        onBlur={() => void finish('blur')}
        onFocus={(e) => e.currentTarget.select()}
        disabled={saving}
        inputMode="decimal"
        autoComplete="off"
        aria-label={`${label}: ball (0–100)`}
        aria-invalid={Boolean(error)}
        title={error ?? '0 dan 100 gacha. Enter — saqlash, Esc — bekor qilish'}
        className={cn(editorBase, 'w-20 text-center tabular-nums', error && 'border-red-300 ring-red-100')}
      />
      {saving && <Loader2 className="h-3.5 w-3.5 animate-spin text-slate-400" />}
      {error && <span className="text-xs text-red-600">{error}</span>}
    </div>
  )
}

interface InlineCommentCellProps {
  value: string | null
  editable: boolean
  onCommit: (next: string | null) => Promise<boolean>
  onInvalid: (message: string) => void
  label: string
}

export function InlineCommentCell({ value, editable, onCommit, onInvalid, label }: InlineCommentCellProps) {
  const [draft, setDraft] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const settling = useRef(false)

  if (!editable) {
    return value ? (
      <span className="block max-w-[18rem] truncate text-slate-600" title={value}>
        {value}
      </span>
    ) : (
      <span className="text-slate-300">—</span>
    )
  }

  if (draft === null) {
    return (
      <button
        type="button"
        onClick={() => {
          settling.current = false
          setError(null)
          setDraft(value ?? '')
        }}
        title={value ?? "Izoh qo'shish"}
        aria-label={`${label}: izohni o'zgartirish`}
        className="group flex max-w-[18rem] items-center gap-1.5 rounded-md px-1.5 py-0.5 text-left transition-colors hover:bg-brand-50"
      >
        <span className={cn('truncate', value ? 'text-slate-600' : 'text-slate-300')}>
          {value || '—'}
        </span>
        <Pencil className="h-3 w-3 shrink-0 text-slate-300 opacity-0 transition-opacity group-hover:opacity-100" />
      </button>
    )
  }

  const finish = async (source: 'enter' | 'blur') => {
    if (settling.current) return
    const problem = commentProblem(draft)
    if (problem) {
      if (source === 'enter') {
        setError(problem)
        return
      }
      settling.current = true
      onInvalid(`${problem} — o'zgarish saqlanmadi`)
      setDraft(null)
      return
    }
    settling.current = true
    const next = normalizeComment(draft)
    if (next === normalizeComment(value)) {
      setDraft(null)
      return
    }
    setSaving(true)
    await onCommit(next)
    setSaving(false)
    setDraft(null)
  }

  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void finish('enter')
    } else if (e.key === 'Escape') {
      e.preventDefault()
      settling.current = true
      setDraft(null)
    }
  }

  return (
    <div className="min-w-[16rem]">
      <textarea
        autoFocus
        rows={3}
        value={draft}
        onChange={(e) => {
          setError(null)
          setDraft(e.target.value)
        }}
        onKeyDown={onKey}
        onBlur={() => void finish('blur')}
        disabled={saving}
        aria-label={`${label}: izoh`}
        aria-invalid={Boolean(error)}
        placeholder="Izoh"
        className={cn(editorBase, 'w-full resize-none', error && 'border-red-300 ring-red-100')}
      />
      <p className={cn('mt-0.5 text-xs', error ? 'text-red-600' : 'text-slate-400')}>
        {saving ? 'Saqlanmoqda...' : error ?? 'Enter — saqlash, Shift+Enter — yangi qator, Esc — bekor qilish'}
      </p>
    </div>
  )
}
