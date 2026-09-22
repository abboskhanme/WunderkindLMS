/**
 * NATIJA KIRITISH — the manual result-entry grid
 * (`docs/modules/admission-and-testing.md` §3.2 screen 9, §6.3 `EntryTableDto`,
 * §8.4; unit C3).
 *
 * Rows are participants, columns are the exam's subjects, cells are points in
 * `[0, maxScore]`. Everything typed is held locally and written in ONE request
 * ("Saqlash" — §3.2); import goes through the Excel dialog.
 *
 * THE SERVER GRADES. Totals, maxima and percents are `EntryTableDto` fields.
 * A row that was edited shows its old total dimmed until the save returns and
 * the grid is re-read — the browser never adds points up (§8.3/§8.4). The
 * only arithmetic here is input hygiene: is it a number, is it in range.
 *
 * A CLEARED CELL IS AN ERROR, NOT A DELETE. The contract has no "no score"
 * value for a cell (`points` is a number); a score is removed by marking the
 * pupil "Kelmadi" (§8.4 `absent: true` clears the row). So emptying a cell
 * that had a score is flagged instead of being silently dropped.
 *
 * READ-ONLY WHEN: the user lacks `exams` (§3.7 hides every editable cell,
 * Saqlash and import), the exam is cancelled, or the exam is online — the
 * engine scores those (§8.3) and this grid only shows them.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  AlertTriangle,
  ArrowLeft,
  Info,
  Loader2,
  RefreshCw,
  RotateCcw,
  Save,
  Search,
  Trash2,
  Upload,
  Users,
} from 'lucide-react'
import {
  examsErrorMessage,
  getEntryTable,
  getExam,
  removeParticipant,
  saveEntryTable,
  type EntryColumn,
  type EntryRow,
  type EntrySaveRow,
  type EntryTable,
  type Exam,
} from '@/api/services/exams'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { ResultImportModal } from './ResultImportModal'
import {
  DASH,
  control,
  examStatusLabel,
  examStatusTone,
  formatDay,
  formatPercent,
  formatPoints,
  formatScore,
  hasPerm,
  kindLabel,
  participantStatusLabel,
  participantStatusTone,
} from './examLabels'
import { parseDecimal } from './examForm'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/**
 * What the user typed on one row, kept verbatim — never normalised while they
 * type, or "42.0" would snap to "42" under the cursor. Whether the row is a
 * CHANGE is derived (`dirtyIds`), not stored.
 */
interface RowDraft {
  scores: Record<string, string>
  absent: boolean
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; forId: string; message: string }
  | { status: 'ready'; forId: string; exam: Exam; table: EntryTable }

const UNSAVED_WARNING = "Saqlanmagan o'zgarishlar bor. Sahifadan chiqsangiz, ular yo'qoladi. Davom etasizmi?"

function toText(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : formatPoints(value)
}

/** A row the grid never edits, whatever the user's rights. */
function rowLocked(row: EntryRow): boolean {
  return row.status === 'cancelled' || row.status === 'in_progress'
}

/** Does the draft say exactly what the server already has? Then it is not a change. */
function sameAsServer(row: EntryRow, draft: RowDraft): boolean {
  if (draft.absent !== (row.status === 'absent')) return false
  for (const [sectionId, text] of Object.entries(draft.scores)) {
    const typed = parseDecimal(text)
    const stored = row.scores[sectionId] ?? null
    if (typed === null && stored === null) continue
    if (typed === null || stored === null || Number.isNaN(typed) || typed !== stored) return false
  }
  return true
}

/** Why a cell cannot be saved, in Uzbek — or `null` when it can. */
function cellProblem(row: EntryRow, column: EntryColumn, text: string): string | null {
  const value = parseDecimal(text)
  if (value === null) {
    const stored = row.scores[column.sectionId]
    return stored === null || stored === undefined
      ? null
      : "Ballni o'chirib bo'lmaydi — 0 kiriting yoki \"Kelmadi\" belgilang"
  }
  if (Number.isNaN(value)) return 'Son kiriting, masalan 42 yoki 42.5'
  if (value < 0 || value > column.maxScore) {
    return `Ball 0 dan ${formatPoints(column.maxScore)} gacha bo'lishi kerak`
  }
  return null
}

export function ResultEntryPage() {
  const { examId = '' } = useParams<{ examId: string }>()
  const { user } = useAuth()
  const canWrite = hasPerm(user?.permissions, 'exams')

  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  const [drafts, setDrafts] = useState<Record<string, RowDraft>>({})
  const [search, setSearch] = useState('')
  const [className, setClassName] = useState('')

  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [showProblems, setShowProblems] = useState(false)
  const [refreshing, setRefreshing] = useState(false)
  const [removingId, setRemovingId] = useState<string | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)

  const cells = useRef(new Map<string, HTMLInputElement>())

  useEffect(() => {
    let cancelled = false
    Promise.all([getExam(examId), getEntryTable(examId)])
      .then(([exam, table]) => {
        if (cancelled) return
        setState({ status: 'ready', forId: examId, exam, table })
        setDrafts({})
        setSaveError(null)
        setShowProblems(false)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setState({ status: 'error', forId: examId, message: examsErrorMessage(err, 'entry.load') })
      })
    return () => {
      cancelled = true
    }
  }, [examId, attempt])

  const retry = () => {
    setState({ status: 'loading' })
    setAttempt((n) => n + 1)
  }

  const ready = state.status === 'ready' && state.forId === examId ? state : null
  const exam = ready?.exam ?? null
  const table = ready?.table ?? null

  const readOnlyReason = !exam
    ? null
    : !canWrite
      ? "Sizda \"Imtihonlar\" ruxsati yo'q — jadval faqat ko'rish uchun ochiq."
      : exam.status === 'cancelled'
        ? "Imtihon bekor qilingan — natijalarni o'zgartirib bo'lmaydi."
        : exam.delivery === 'online'
          ? "Onlayn imtihon — ballarni tizim o'zi hisoblaydi. Bu yerda natijalar faqat ko'rsatiladi."
          : null
  const editable = exam !== null && readOnlyReason === null

  /** Rows whose typed state differs from the server's — the ones "Saqlash" sends. */
  const dirtyIds = useMemo(() => {
    const out = new Set<string>()
    if (!table) return out
    for (const row of table.rows) {
      const draft = drafts[row.participantId]
      if (draft && !sameAsServer(row, draft)) out.add(row.participantId)
    }
    return out
  }, [table, drafts])

  const dirtyCount = dirtyIds.size

  // Leaving the tab with unsaved scores asks first.
  useEffect(() => {
    if (dirtyCount === 0) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirtyCount])

  const classNames = useMemo(() => {
    if (!table) return []
    const names = new Set<string>()
    for (const r of table.rows) if (r.className) names.add(r.className)
    return [...names].sort((a, b) => a.localeCompare(b, 'uz', { numeric: true }))
  }, [table])

  const visibleRows = useMemo(() => {
    if (!table) return []
    const needle = search.trim().toLowerCase()
    return table.rows.filter(
      (r) =>
        (!className || r.className === className) &&
        (!needle || r.fullName.toLowerCase().includes(needle)),
    )
  }, [table, search, className])

  const filtersActive = Boolean(search.trim() || className)

  const counts = useMemo(() => {
    const out = { total: 0, finished: 0, absent: 0 }
    if (!table) return out
    for (const r of table.rows) {
      out.total += 1
      if (r.status === 'finished') out.finished += 1
      if (r.status === 'absent') out.absent += 1
    }
    return out
  }, [table])

  const cellText = useCallback(
    (row: EntryRow, sectionId: string): string =>
      drafts[row.participantId]?.scores[sectionId] ?? toText(row.scores[sectionId]),
    [drafts],
  )

  const isAbsent = (row: EntryRow): boolean =>
    drafts[row.participantId]?.absent ?? row.status === 'absent'

  /** Every cell that cannot be saved, across the dirty rows (not only the visible ones). */
  const problems = useMemo(() => {
    const out = new Map<string, string>()
    if (!table) return out
    for (const row of table.rows) {
      const draft = drafts[row.participantId]
      if (!draft || draft.absent || !dirtyIds.has(row.participantId)) continue
      for (const column of table.columns) {
        const problem = cellProblem(row, column, cellText(row, column.sectionId))
        if (problem) out.set(`${row.participantId}:${column.sectionId}`, problem)
      }
    }
    return out
  }, [table, drafts, dirtyIds, cellText])

  const updateDraft = (row: EntryRow, patch: (draft: RowDraft) => RowDraft) => {
    setSaveError(null)
    setDrafts((prev) => {
      const base = prev[row.participantId] ?? { scores: {}, absent: row.status === 'absent' }
      return { ...prev, [row.participantId]: patch(base) }
    })
  }

  const setCell = (row: EntryRow, sectionId: string, text: string) =>
    updateDraft(row, (d) => ({ ...d, scores: { ...d.scores, [sectionId]: text } }))

  const setAbsent = (row: EntryRow, absent: boolean) => updateDraft(row, (d) => ({ ...d, absent }))

  /** Enter / ↓ go down a column, ↑ goes up — the way a teacher types a paper stack. */
  const onCellKey = (e: KeyboardEvent<HTMLInputElement>, rowIndex: number, colIndex: number) => {
    const step = e.key === 'Enter' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0
    if (step === 0) return
    e.preventDefault()
    for (let r = rowIndex + step; r >= 0 && r < visibleRows.length; r += step) {
      const target = cells.current.get(`${r}:${colIndex}`)
      if (target && !target.disabled) {
        target.focus()
        target.select()
        return
      }
    }
  }

  const refreshTable = async (): Promise<boolean> => {
    if (!exam) return false
    setRefreshing(true)
    try {
      const fresh = await getEntryTable(exam.id)
      setState({ status: 'ready', forId: exam.id, exam, table: fresh })
      setDrafts({})
      return true
    } catch (err) {
      setSaveError(`Jadvalni yangilab bo'lmadi: ${examsErrorMessage(err, 'entry.load')}`)
      return false
    } finally {
      setRefreshing(false)
    }
  }

  const save = async () => {
    if (!table || !exam || dirtyCount === 0 || saving) return
    if (problems.size > 0) {
      setShowProblems(true)
      setSaveError(`${problems.size} ta katakda xato bor — qizil kataklarni tuzating.`)
      return
    }
    const rows: EntrySaveRow[] = table.rows
      .filter((row) => dirtyIds.has(row.participantId))
      .map((row) => {
        const draft = drafts[row.participantId]
        if (draft.absent) return { participantId: row.participantId, scores: [], absent: true }
        const scores = table.columns.flatMap((column) => {
          const value = parseDecimal(cellText(row, column.sectionId))
          return value === null || Number.isNaN(value)
            ? []
            : [{ sectionId: column.sectionId, points: value }]
        })
        return { participantId: row.participantId, scores, absent: false }
      })

    setSaving(true)
    setSaveError(null)
    try {
      const result = await saveEntryTable(exam.id, rows)
      setShowProblems(false)
      setNotice({ message: `${result.saved} ta o'quvchi natijasi saqlandi`, tone: 'success' })
      await refreshTable()
    } catch (err) {
      setSaveError(examsErrorMessage(err, 'entry.save'))
    } finally {
      setSaving(false)
    }
  }

  const discard = () => {
    if (!confirm("Saqlanmagan o'zgarishlar bekor qilinsinmi?")) return
    setDrafts({})
    setSaveError(null)
    setShowProblems(false)
  }

  /** 409 once an attempt exists — never for a paper exam, but the server decides. */
  const remove = async (row: EntryRow) => {
    if (!exam || !table) return
    if (!confirm(`${row.fullName} imtihon ro'yxatidan chiqarilsinmi? Uning natijasi ham o'chadi.`)) return
    setRemovingId(row.participantId)
    try {
      await removeParticipant(exam.id, row.participantId)
      setState({
        status: 'ready',
        forId: exam.id,
        exam,
        table: { ...table, rows: table.rows.filter((r) => r.participantId !== row.participantId) },
      })
      setDrafts((prev) => {
        const out = { ...prev }
        delete out[row.participantId]
        return out
      })
      setNotice({ message: `${row.fullName} ro'yxatdan chiqarildi`, tone: 'success' })
    } catch (err) {
      setNotice({ message: examsErrorMessage(err, 'participants.remove'), tone: 'error' })
    } finally {
      setRemovingId(null)
    }
  }

  const confirmLeave = (e: { preventDefault: () => void }) => {
    if (dirtyCount > 0 && !confirm(UNSAVED_WARNING)) e.preventDefault()
  }

  const backLink = (
    <Link
      to="/admin/exams/list"
      onClick={confirmLeave}
      className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition-colors hover:text-slate-800"
    >
      <ArrowLeft className="h-4 w-4" /> Imtihonlar
    </Link>
  )

  // ---- loading / error ----

  if (!ready) {
    const failed = state.status === 'error' && state.forId === examId ? state : null
    return (
      <div className="space-y-6">
        {backLink}
        {failed ? (
          <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <p className="font-medium text-slate-800">Natijalar jadvalini ochib bo'lmadi</p>
            <p className="max-w-md text-sm text-slate-500">{failed.message}</p>
            <Button variant="secondary" onClick={retry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </Card>
        ) : (
          <Loader label="Yuklanmoqda..." />
        )}
      </div>
    )
  }

  const { exam: current, table: grid } = ready
  const showActions = editable && grid.columns.length > 0 && grid.rows.length > 0

  return (
    <div className="space-y-6">
      {backLink}

      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold text-slate-800" title={current.title}>
            {current.title}
          </h1>
          <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-slate-400">
            <span
              className={cn(
                'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                examStatusTone(current.status),
              )}
            >
              {examStatusLabel(current.status)}
            </span>
            <span>{kindLabel(current.kind)}</span>
            {current.examTypeName && <span>· {current.examTypeName}</span>}
            <span>· {formatDay(grid.examDate ?? current.examDate)}</span>
            <span>
              · {counts.total} ta ishtirokchi, {counts.finished} tasi baholangan
              {counts.absent > 0 && `, ${counts.absent} tasi kelmadi`}
            </span>
          </p>
        </div>

        {showActions && (
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              onClick={() => setImportOpen(true)}
              disabled={dirtyCount > 0 || saving}
              title={dirtyCount > 0 ? "Avval o'zgarishlarni saqlang yoki bekor qiling" : undefined}
            >
              <Upload className="h-4 w-4" /> Excel'dan yuklash
            </Button>
            {dirtyCount > 0 && (
              <Button variant="ghost" onClick={discard} disabled={saving}>
                <RotateCcw className="h-4 w-4" /> Bekor qilish
              </Button>
            )}
            <Button onClick={() => void save()} disabled={dirtyCount === 0 || saving || refreshing}>
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              {dirtyCount > 0 ? `Saqlash (${dirtyCount})` : 'Saqlash'}
            </Button>
          </div>
        )}
      </div>

      {readOnlyReason && (
        <p className="flex items-center gap-2 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-500">
          <Info className="h-4 w-4 shrink-0" /> {readOnlyReason}
        </p>
      )}

      {editable && current.status === 'draft' && grid.rows.length > 0 && (
        <p className="flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
          <Info className="h-4 w-4 shrink-0" />
          Imtihon hali e'lon qilinmagan — fanlar o'zgarsa, jadval ustunlari ham o'zgaradi.
        </p>
      )}

      {saveError && (
        <p className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {saveError}
        </p>
      )}

      {grid.columns.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <p className="text-sm font-medium text-slate-600">Imtihonda hali fan yo'q</p>
          <p className="max-w-md text-sm text-slate-400">
            Natija kiritish uchun avval imtihonni tahrirlab, fanlar va ularning maksimal ballini
            qo'shing.
          </p>
          <Link to="/admin/exams/list" className="text-sm font-medium text-brand-600 hover:text-brand-700">
            Imtihonlar ro'yxatiga o'tish
          </Link>
        </Card>
      ) : grid.rows.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <Users className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Imtihonga hali o'quvchi biriktirilmagan</p>
          <p className="max-w-md text-sm text-slate-400">
            Imtihonlar ro'yxatida uni tahrirlab, qatnashadigan sinflarni qo'shing — sinfdagi barcha
            faol o'quvchilar shu jadvalga tushadi.
          </p>
          <Link to="/admin/exams/list" className="text-sm font-medium text-brand-600 hover:text-brand-700">
            Imtihonlar ro'yxatiga o'tish
          </Link>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
            <div className="relative min-w-[200px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="O'quvchi ismi..."
                aria-label="Qidiruv"
                className={cn(control, 'w-full pl-9')}
              />
            </div>
            {classNames.length > 1 && (
              <select
                value={className}
                onChange={(e) => setClassName(e.target.value)}
                aria-label="Sinf"
                className={control}
              >
                <option value="">Barcha sinflar</option>
                {classNames.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </select>
            )}
            {editable && (
              <span className="ml-auto text-xs text-slate-400">
                Enter yoki ↓ — keyingi o'quvchi. Hammasi bitta "Saqlash" bilan yoziladi.
              </span>
            )}
          </div>

          {visibleRows.length === 0 ? (
            <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
              <Search className="h-8 w-8 text-slate-300" />
              <p className="font-medium text-slate-600">
                {filtersActive ? "Bu shartlar bo'yicha o'quvchi topilmadi" : "O'quvchi yo'q"}
              </p>
              {filtersActive && (
                <button
                  type="button"
                  onClick={() => {
                    setSearch('')
                    setClassName('')
                  }}
                  className="mt-1 inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  <RotateCcw className="h-3.5 w-3.5" /> Filtrlarni tozalash
                </button>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full border-separate border-spacing-0 text-sm">
                <thead>
                  <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <th className="sticky left-0 z-10 min-w-[14rem] border-b border-slate-100 bg-slate-50 px-4 py-2 text-left font-medium">
                      F.I.SH
                    </th>
                    {grid.columns.map((column) => (
                      <th
                        key={column.sectionId}
                        className="border-b border-slate-100 px-2 py-2 text-center font-medium"
                      >
                        <span className="block max-w-[8rem] truncate normal-case text-slate-600" title={column.name}>
                          {column.name}
                        </span>
                        <span className="block text-[10px] font-normal normal-case text-slate-400">
                          maks. {formatPoints(column.maxScore)}
                        </span>
                      </th>
                    ))}
                    <th className="border-b border-slate-100 px-3 py-2 text-center font-medium">Kelmadi</th>
                    <th className="border-b border-slate-100 px-3 py-2 text-right font-medium">Jami</th>
                    <th className="border-b border-slate-100 px-3 py-2 text-right font-medium">Foiz</th>
                    <th className="border-b border-slate-100 px-3 py-2 text-left font-medium">Holati</th>
                    {editable && <th className="border-b border-slate-100 px-2 py-2" />}
                  </tr>
                </thead>
                <tbody>
                  {visibleRows.map((row, rowIndex) => {
                    const locked = !editable || rowLocked(row)
                    const absent = isAbsent(row)
                    const dirty = dirtyIds.has(row.participantId)
                    return (
                      <tr key={row.participantId} className={cn('group', dirty && 'bg-brand-50/30')}>
                        <td
                          className={cn(
                            'sticky left-0 z-10 border-b border-slate-100 px-4 py-2',
                            dirty ? 'bg-brand-50' : 'bg-white group-hover:bg-slate-50',
                          )}
                        >
                          <span className="block max-w-[16rem] truncate font-medium text-slate-800" title={row.fullName}>
                            {row.fullName}
                          </span>
                          <span className="block text-xs text-slate-400">{row.className ?? DASH}</span>
                        </td>
                        {grid.columns.map((column, colIndex) => {
                          const key = `${row.participantId}:${column.sectionId}`
                          const text = cellText(row, column.sectionId)
                          const problem = problems.get(key) ?? null
                          const typedHere =
                            dirty && drafts[row.participantId]?.scores[column.sectionId] !== undefined
                          const showBad = problem !== null && (showProblems || typedHere)
                          return (
                            <td key={column.sectionId} className="border-b border-slate-100 px-2 py-1.5 text-center">
                              {locked ? (
                                <span className={cn('text-slate-700', absent && 'text-slate-300')}>
                                  {absent ? DASH : text || DASH}
                                </span>
                              ) : (
                                <input
                                  ref={(el) => {
                                    const refKey = `${rowIndex}:${colIndex}`
                                    if (el) cells.current.set(refKey, el)
                                    else cells.current.delete(refKey)
                                  }}
                                  value={absent ? '' : text}
                                  onChange={(e) => setCell(row, column.sectionId, e.target.value)}
                                  onKeyDown={(e) => onCellKey(e, rowIndex, colIndex)}
                                  onFocus={(e) => e.target.select()}
                                  disabled={absent || saving}
                                  inputMode="decimal"
                                  autoComplete="off"
                                  aria-label={`${row.fullName}, ${column.name}`}
                                  aria-invalid={showBad}
                                  title={showBad && problem ? problem : undefined}
                                  className={cn(
                                    'w-16 rounded-md border px-1.5 py-1 text-center text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100',
                                    showBad
                                      ? 'border-red-300 bg-red-50 text-red-700'
                                      : typedHere
                                        ? 'border-brand-200 bg-white text-slate-800'
                                        : 'border-slate-200 bg-white text-slate-800',
                                    absent && 'cursor-not-allowed border-slate-100 bg-slate-50',
                                  )}
                                />
                              )}
                            </td>
                          )
                        })}
                        <td className="border-b border-slate-100 px-3 py-1.5 text-center">
                          <input
                            type="checkbox"
                            checked={absent}
                            onChange={(e) => setAbsent(row, e.target.checked)}
                            disabled={locked || saving}
                            aria-label={`${row.fullName}: kelmadi`}
                            className="h-4 w-4 rounded border-slate-300 accent-brand-600 disabled:cursor-not-allowed disabled:opacity-50"
                          />
                        </td>
                        <td
                          className={cn(
                            'whitespace-nowrap border-b border-slate-100 px-3 py-1.5 text-right',
                            dirty ? 'text-slate-300' : 'font-medium text-slate-700',
                          )}
                          title={dirty ? 'Saqlangandan keyin qayta hisoblanadi' : undefined}
                        >
                          {formatScore(row.totalPoints, row.maxPoints)}
                        </td>
                        <td
                          className={cn(
                            'whitespace-nowrap border-b border-slate-100 px-3 py-1.5 text-right',
                            dirty ? 'text-slate-300' : 'text-slate-600',
                          )}
                        >
                          {formatPercent(row.percent)}
                        </td>
                        <td className="whitespace-nowrap border-b border-slate-100 px-3 py-1.5">
                          <span
                            className={cn(
                              'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                              participantStatusTone(row.status),
                            )}
                          >
                            {participantStatusLabel(row.status)}
                          </span>
                        </td>
                        {editable && (
                          <td className="border-b border-slate-100 px-2 py-1.5 text-right">
                            <button
                              type="button"
                              onClick={() => void remove(row)}
                              disabled={removingId === row.participantId || saving || rowLocked(row)}
                              title="Imtihon ro'yxatidan chiqarish"
                              aria-label={`${row.fullName}ni imtihon ro'yxatidan chiqarish`}
                              className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
                            >
                              {removingId === row.participantId ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <Trash2 className="h-4 w-4" />
                              )}
                            </button>
                          </td>
                        )}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}

          <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
            <span>
              {visibleRows.length === grid.rows.length
                ? `${grid.rows.length} ta o'quvchi`
                : `${visibleRows.length} / ${grid.rows.length} ta o'quvchi`}
            </span>
            {refreshing && (
              <span className="inline-flex items-center gap-1.5">
                <Loader2 className="h-3.5 w-3.5 animate-spin" /> Jadval yangilanmoqda…
              </span>
            )}
            {dirtyCount > 0 && !refreshing && (
              <span className="font-medium text-brand-600">
                {dirtyCount} ta o'quvchida saqlanmagan o'zgarish
              </span>
            )}
          </div>
        </Card>
      )}

      {importOpen && editable && (
        <ResultImportModal
          examId={current.id}
          onClose={() => setImportOpen(false)}
          onImported={(imported) => {
            setImportOpen(false)
            setNotice({ message: `${imported} ta qator yozildi`, tone: 'success' })
            void refreshTable()
          }}
        />
      )}

      <Toast
        message={notice?.message ?? null}
        tone={notice?.tone ?? 'success'}
        onClose={() => setNotice(null)}
      />
    </div>
  )
}
