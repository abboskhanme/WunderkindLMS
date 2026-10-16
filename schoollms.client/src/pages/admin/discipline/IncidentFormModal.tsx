import { useEffect, useMemo, useState } from 'react'
import { Search, Send } from 'lucide-react'
import type { DisciplineReason, DisciplineType } from '@/types'
import {
  addDisciplinePoint,
  disciplineTypeLabel,
  disciplineTypeOf,
  getDisciplineReasons,
  updateDisciplinePoint,
} from '@/api/services/discipline'
import { searchStudents, type StudentListRow } from '@/api/services/studentSearch'
import { cn } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'

/** The pupil the incident is for — fixed (profile) or picked in the form (Harakatlar). */
export interface IncidentStudent {
  id: string
  fullName: string
  className?: string
}

/** A manual entry being edited. */
export interface IncidentEditing {
  id: string
  reasonId?: string | null
  reasonName: string
  points: number
  note: string
  studentName?: string
}

interface Props {
  open: boolean
  /** Fixed pupil — no search. Omitted (and not editing) — the form searches for one. */
  student?: IncidentStudent | null
  editing?: IncidentEditing | null
  onClose: () => void
  /** `notified` — parents told by Telegram (new entries only). */
  onSaved: (notified: number) => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/**
 * "Harakat qo'shish / tahrirlash" — EduSchool's incident form (docs/modules/behaviour.md): the type
 * first (Ijobiy / Salbiy), then the reasons of that type with their points, an optional note. Points
 * always come from the reason; editing to another reason takes that reason's points.
 */
export function IncidentFormModal({ open, student, editing, onClose, onSaved }: Props) {
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [type, setType] = useState<DisciplineType>('negative')
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [picked, setPicked] = useState<IncidentStudent | null>(null)
  const [query, setQuery] = useState('')
  const [found, setFound] = useState<StudentListRow[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the form is reset each time it opens (existing pattern)
    setType(editing ? disciplineTypeOf(editing.points) : 'negative')
    setReasonId(editing?.reasonId ?? '')
    setNote(editing?.note ?? '')
    setPicked(null)
    setQuery('')
    setFound([])
    setError(null)
    getDisciplineReasons()
      .then((all) => setReasons(all.filter((r) => r.kind === 'other')))
      .catch(() => setReasons([]))
  }, [open, editing])

  // Pupil search (Harakatlar) — server side, debounced.
  const searching = open && !student && !editing && !picked
  useEffect(() => {
    if (!searching) return
    const q = query.trim()
    if (q.length < 2) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- clearing the list for a short query
      setFound([])
      return
    }
    let cancelled = false
    const timer = setTimeout(() => {
      searchStudents({ state: 'active', search: q, pageSize: 12 })
        .then((page) => {
          if (!cancelled) setFound(page.items)
        })
        .catch(() => {
          if (!cancelled) setFound([])
        })
    }, 300)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [query, searching])

  /** Active reasons of the chosen type; the edited entry's reason stays listed even if retired. */
  const options = useMemo(() => {
    const list = reasons.filter((r) => disciplineTypeOf(r.points) === type && (r.isActive || r.id === editing?.reasonId))
    return [...list].sort((a, b) => a.name.localeCompare(b.name))
  }, [reasons, type, editing])
  const selected = reasons.find((r) => r.id === reasonId) ?? null
  // The edited entry's reason may be an attendance reason or a deleted one — keep it choosable.
  const keepsUnlisted = !!editing?.reasonId && editing.reasonId === reasonId && !selected
  const target = editing ? null : (student ?? picked)

  const pickType = (next: DisciplineType) => {
    setType(next)
    if (selected && disciplineTypeOf(selected.points) !== next) setReasonId('')
  }

  const submit = async () => {
    if (!reasonId || busy || (!editing && !target)) return
    setBusy(true)
    setError(null)
    try {
      if (editing) {
        await updateDisciplinePoint(editing.id, reasonId, note.trim())
        onSaved(0)
      } else if (target) {
        const saved = await addDisciplinePoint(target.id, reasonId, note.trim() || undefined)
        onSaved(saved.notifiedParents ?? 0)
      }
    } catch (e) {
      setError(errorText(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const points = (p: number) => (p > 0 ? `+${p}` : String(p))

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Harakatni tahrirlash' : "Harakat qo'shish"}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy || !reasonId || (!editing && !target)}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* ---------- O'quvchi ---------- */}
        {editing ? (
          editing.studentName && (
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
              O'quvchi: <b className="text-slate-800">{editing.studentName}</b>
            </p>
          )
        ) : target ? (
          <div className="flex items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2 text-sm">
            <span className="text-slate-600">
              O'quvchi: <b className="text-slate-800">{target.fullName}</b>
              {target.className && <span className="text-slate-400"> · {target.className}</span>}
            </span>
            {!student && (
              <button
                type="button"
                onClick={() => setPicked(null)}
                className="text-xs font-medium text-brand-600 hover:underline"
              >
                Boshqasini tanlash
              </button>
            )}
          </div>
        ) : (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-700">O'quvchi</span>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                autoFocus
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="F.I.SH bo'yicha qidirish (kamida 2 harf)"
                className="w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
            </div>
            {found.length > 0 && (
              <div className="mt-2 max-h-48 overflow-y-auto rounded-lg border border-slate-100">
                {found.map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => setPicked({ id: s.id, fullName: s.fullName, className: s.className })}
                    className="flex w-full items-center justify-between gap-3 border-t border-slate-100 px-3 py-2 text-left text-sm first:border-t-0 hover:bg-slate-50"
                  >
                    <span className="truncate text-slate-800">{s.fullName}</span>
                    <span className="shrink-0 text-xs text-slate-400">{s.className || '—'}</span>
                  </button>
                ))}
              </div>
            )}
            {query.trim().length >= 2 && found.length === 0 && (
              <p className="mt-2 text-xs text-slate-400">Faol o'quvchi topilmadi</p>
            )}
          </div>
        )}

        {/* ---------- Turi, keyin sabab ---------- */}
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-700">Turi</span>
          <div className="grid grid-cols-2 gap-2">
            {(['positive', 'negative'] as const).map((t) => (
              <button
                key={t}
                type="button"
                onClick={() => pickType(t)}
                className={cn(
                  'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                  type === t
                    ? t === 'positive'
                      ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                      : 'border-red-300 bg-red-50 text-red-600'
                    : 'border-slate-200 bg-white text-slate-500 hover:bg-slate-50',
                )}
              >
                {disciplineTypeLabel[t]}
              </button>
            ))}
          </div>
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-700">Sabab</span>
          {options.length === 0 && !keepsUnlisted ? (
            <p className="text-sm text-amber-600">
              Bu turdagi faol sabab yo'q — "Ball sabablar" bo'limida qo'shing.
            </p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {keepsUnlisted && editing && (
                <span className="rounded-lg border border-brand-300 bg-brand-50 px-3 py-1.5 text-sm font-medium text-brand-700">
                  {editing.reasonName} ({points(editing.points)})
                </span>
              )}
              {options.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  title={r.description ?? undefined}
                  onClick={() => setReasonId(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-1.5 text-sm transition-colors',
                    r.id === reasonId
                      ? 'border-brand-400 bg-brand-50 font-semibold text-brand-700'
                      : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {r.name}{' '}
                  <span className={r.points < 0 ? 'text-red-600' : 'text-emerald-600'}>({points(r.points)})</span>
                </button>
              ))}
            </div>
          )}
          {selected?.notifyParent && !editing && (
            <p className="mt-2 inline-flex items-center gap-1 text-xs text-sky-600">
              <Send className="h-3 w-3" /> Bu sabab bilan ota-onaga Telegram xabari ketadi.
            </p>
          )}
          {editing && selected && selected.id !== editing.reasonId && (
            <p className="mt-2 text-xs text-slate-400">
              Ball yangi sababdan olinadi: {points(selected.points)}.
            </p>
          )}
        </div>

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          placeholder="Masalan: tanaffusda sinfdoshi bilan urushdi"
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
