import { useEffect, useMemo, useState } from 'react'
import { Plus, History, Trash2, Search } from 'lucide-react'
import type { DisciplineReason, DisciplineScoreRow, DisciplinePoint } from '@/types'
import {
  getDisciplineScores,
  getDisciplineReasons,
  addDisciplinePoint,
  getStudentDisciplinePoints,
  deleteDisciplinePoint,
} from '@/api/services/discipline'
import { cn, formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type Sort = 'class' | 'remaining_desc' | 'remaining_asc' | 'plus_desc' | 'minus_desc'

const SORTS: { value: Sort; label: string }[] = [
  { value: 'class', label: 'Sinf bo\'yicha' },
  { value: 'remaining_desc', label: 'Eng yuqori qoldi' },
  { value: 'remaining_asc', label: 'Eng kam qoldi' },
  { value: 'plus_desc', label: 'Eng ko\'p rag\'bat (+)' },
  { value: 'minus_desc', label: 'Eng ko\'p jazo (−)' },
]

export function BallarNazoratiPage() {
  const [scores, setScores] = useState<DisciplineScoreRow[]>([])
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [loading, setLoading] = useState(true)

  const [classFilter, setClassFilter] = useState('all')
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<Sort>('class')

  // Ball kiritish modali
  const [entryFor, setEntryFor] = useState<DisciplineScoreRow | null>(null)
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)

  // Tarix modali
  const [historyOf, setHistoryOf] = useState<DisciplineScoreRow | null>(null)
  const [history, setHistory] = useState<DisciplinePoint[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)

  useEffect(() => {
    Promise.all([getDisciplineScores(), getDisciplineReasons()])
      .then(([s, r]) => {
        setScores(s)
        setReasons(r)
      })
      .finally(() => setLoading(false))
  }, [])

  const classNames = useMemo(
    () => [...new Set(scores.map((s) => s.className).filter(Boolean))].sort(),
    [scores],
  )

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    let list = scores.filter(
      (s) =>
        (classFilter === 'all' || s.className === classFilter) &&
        (!q || s.fullName.toLowerCase().includes(q)),
    )
    const by = {
      class: (a: DisciplineScoreRow, b: DisciplineScoreRow) =>
        a.className.localeCompare(b.className) || a.fullName.localeCompare(b.fullName),
      remaining_desc: (a: DisciplineScoreRow, b: DisciplineScoreRow) => b.remaining - a.remaining,
      remaining_asc: (a: DisciplineScoreRow, b: DisciplineScoreRow) => a.remaining - b.remaining,
      plus_desc: (a: DisciplineScoreRow, b: DisciplineScoreRow) => b.plus - a.plus,
      minus_desc: (a: DisciplineScoreRow, b: DisciplineScoreRow) => b.minus - a.minus,
    }[sort]
    return [...list].sort(by)
  }, [scores, classFilter, search, sort])

  const applyDelta = (studentId: string, pts: number) =>
    setScores((prev) =>
      prev.map((r) =>
        r.studentId === studentId
          ? {
              ...r,
              plus: r.plus + (pts > 0 ? pts : 0),
              minus: r.minus + (pts < 0 ? -pts : 0),
              remaining: r.remaining + pts,
            }
          : r,
      ),
    )

  const openEntry = (row: DisciplineScoreRow) => {
    setEntryFor(row)
    setReasonId(reasons[0]?.id ?? '')
    setNote('')
  }

  const submitEntry = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!entryFor || !reasonId || saving) return
    setSaving(true)
    try {
      const p = await addDisciplinePoint(entryFor.studentId, reasonId, note.trim() || undefined)
      applyDelta(entryFor.studentId, p.points)
      setEntryFor(null)
    } finally {
      setSaving(false)
    }
  }

  const openHistory = (row: DisciplineScoreRow) => {
    setHistoryOf(row)
    setHistory([])
    setHistoryLoading(true)
    getStudentDisciplinePoints(row.studentId)
      .then(setHistory)
      .finally(() => setHistoryLoading(false))
  }

  const removePoint = async (p: DisciplinePoint) => {
    await deleteDisciplinePoint(p.id)
    setHistory((prev) => prev.filter((x) => x.id !== p.id))
    applyDelta(p.studentId, -p.points)
  }

  const reasonLabel = (r: DisciplineReason) =>
    `${r.name} (${r.points > 0 ? '+' : ''}${r.points})`
  const attendanceReasons = useMemo(() => reasons.filter((r) => r.kind === 'attendance'), [reasons])
  const otherReasons = useMemo(() => reasons.filter((r) => r.kind === 'other'), [reasons])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Ballar nazorati</h1>
        <p className="text-sm text-slate-400">
          Har o'quvchi 100 balldan boshlaydi. Sabab bo'yicha ball kiriting — qoldi avtomatik hisoblanadi.
        </p>
      </div>

      <Card className="p-0">
        {/* Filtrlar */}
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="F.I.SH bo'yicha qidirish..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          <select value={classFilter} onChange={(e) => setClassFilter(e.target.value)} className={control}>
            <option value="all">Barcha sinflar</option>
            {classNames.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
          <select value={sort} onChange={(e) => setSort(e.target.value as Sort)} className={control}>
            {SORTS.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3 text-center">Rag'bat (+)</th>
                  <th className="px-4 py-3 text-center">Jazo (−)</th>
                  <th className="px-4 py-3 text-center">Qoldi</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r, i) => (
                  <tr key={r.studentId} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                    <td className="px-4 py-3">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {r.className}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center font-medium text-emerald-600">
                      {r.plus > 0 ? `+${r.plus}` : '—'}
                    </td>
                    <td className="px-4 py-3 text-center font-medium text-red-600">
                      {r.minus > 0 ? `−${r.minus}` : '—'}
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span
                        className={cn(
                          'inline-block min-w-[44px] rounded-md px-2 py-0.5 text-sm font-bold',
                          r.remaining >= 80
                            ? 'bg-emerald-50 text-emerald-700'
                            : r.remaining >= 50
                              ? 'bg-amber-50 text-amber-700'
                              : 'bg-red-50 text-red-700',
                        )}
                      >
                        {r.remaining}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <button
                          type="button"
                          title="Ball kiritish"
                          onClick={() => openEntry(r)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-700"
                        >
                          <Plus className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title="Tarix"
                          onClick={() => openHistory(r)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <History className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-12 text-center text-slate-400">
                      O'quvchi topilmadi
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Ball kiritish */}
      <Modal
        open={!!entryFor}
        onClose={() => setEntryFor(null)}
        title={entryFor ? `${entryFor.fullName} — ball kiritish` : 'Ball kiritish'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setEntryFor(null)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="entry-form" disabled={!reasonId || saving}>
              {saving ? 'Saqlanmoqda...' : 'Kiritish'}
            </Button>
          </>
        }
      >
        <form id="entry-form" onSubmit={submitEntry} className="space-y-4">
          {reasons.length === 0 ? (
            <p className="text-sm text-amber-600">
              Avval "Ball sabablar" bo'limida sabab qo'shing.
            </p>
          ) : (
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">Sabab</label>
              <select
                value={reasonId}
                onChange={(e) => setReasonId(e.target.value)}
                className={cn(control, 'w-full')}
              >
                {otherReasons.length > 0 && (
                  <optgroup label="Boshqa sabablar">
                    {otherReasons.map((r) => (
                      <option key={r.id} value={r.id}>
                        {reasonLabel(r)}
                      </option>
                    ))}
                  </optgroup>
                )}
                {attendanceReasons.length > 0 && (
                  <optgroup label="Davomat sabablari">
                    {attendanceReasons.map((r) => (
                      <option key={r.id} value={r.id}>
                        {reasonLabel(r)}
                      </option>
                    ))}
                  </optgroup>
                )}
              </select>
            </div>
          )}
          <Input
            label="Izoh (ixtiyoriy)"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="masalan: 3-darsda"
          />
        </form>
      </Modal>

      {/* Tarix */}
      <Modal
        open={!!historyOf}
        onClose={() => setHistoryOf(null)}
        title={historyOf ? `${historyOf.fullName} — ball tarixi` : 'Tarix'}
        footer={
          <Button variant="secondary" onClick={() => setHistoryOf(null)}>
            Yopish
          </Button>
        }
      >
        {historyLoading ? (
          <Loader label="Yuklanmoqda..." />
        ) : history.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Hali ball kiritilmagan</p>
        ) : (
          <div className="space-y-2">
            {history.map((p) => (
              <div
                key={p.id}
                className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2"
              >
                <span
                  className={cn(
                    'shrink-0 rounded-md px-2 py-0.5 text-sm font-bold',
                    p.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                  )}
                >
                  {p.points > 0 ? `+${p.points}` : p.points}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-slate-700">{p.reasonName}</p>
                  <p className="text-xs text-slate-400">
                    {formatDate(p.createdAt)}
                    {p.note ? ` · ${p.note}` : ''}
                    {p.createdBy ? ` · ${p.createdBy}` : ''}
                  </p>
                </div>
                {p.source === 'attendance' ? (
                  <span className="shrink-0 rounded-md bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-500">
                    jurnal
                  </span>
                ) : (
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={() => removePoint(p)}
                    className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </Modal>
    </div>
  )
}
