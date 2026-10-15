import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { CheckCheck, Download, NotebookText } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { getJournalOwners, getJournalSubjects } from '@/api/services/journal'
import { getMyClasses } from '@/api/services/teacher'
import {
  downloadHomeworkSheet,
  getHomeworkSheet,
  saveHomeworkMarks,
  type HomeworkLesson,
  type HomeworkScope,
  type HomeworkSheet,
} from '@/api/services/homeworkCompletion'
import {
  HOMEWORK_RATE_HINT,
  HOMEWORK_STATUSES,
  HW_DONE,
  HW_NONE,
  homeworkFromKey,
  homeworkMeta,
  homeworkTally,
  percentColor,
  tallyCount,
  type HomeworkTally,
} from '@/lib/homeworkStatus'
import { cn, formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { DatePicker } from '@/components/ui/DatePicker'

/* ==========================================================================
   "Uy vazifasi bajarilishi" — client, 2026-10-15: "uyga vazifalar bajarilish darajasini
   aniqlash va jadval holatida qilish (+ / - / *)". Pupils × lessons of one class/group +
   subject over a date range; one tap or one key per cell. docs/modules/homework-completion.md.

   One component for both surfaces: the admin (`/admin/journal-homework`, journal key) and the
   teacher (`/teacher/uy-vazifasi`, own class + subject). Only the pickers' source and the API
   prefix differ — the server applies each side's gate.

   A mark is saved the moment it is chosen (like the journal cell), the totals are recounted at
   once with the server's formula and the sheet is then re-read quietly, so a derived "Kelmadi"
   or a lesson that just became "o'tildi" comes back exactly as the server sees it.
   ========================================================================== */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const weekdayShort = (iso: string) => ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh'][new Date(iso).getDay()] ?? ''

/** "YYYY-MM-DD" in local time (the school's day, not UTC). */
function isoDay(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
function daysAgo(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return isoDay(d)
}
function mondayOfThisWeek(): string {
  const d = new Date()
  const dow = (d.getDay() + 6) % 7 // Monday = 0
  d.setDate(d.getDate() - dow)
  return isoDay(d)
}
function firstOfThisMonth(): string {
  const d = new Date()
  return isoDay(new Date(d.getFullYear(), d.getMonth(), 1))
}

/** Same cap as the server (HomeworkCompletionService.MaxRangeDays). */
const MAX_RANGE_DAYS = 120

interface OwnerOption {
  id: string
  label: string
}
interface SubjectOption {
  id: string
  name: string
}

/** Error text of an axios failure (the server's Uzbek message when there is one). */
function errorText(err: unknown, fallback: string): string {
  const e = err as { response?: { status?: number; data?: { message?: string } } }
  if (e?.response?.data?.message) return e.response.data.message
  if (e?.response?.status === 403) return "Bu sinf yoki fanga ruxsatingiz yo'q."
  return fallback
}

const fmtPct = (p: number | null) => (p == null ? '—' : `${Number.isInteger(p) ? p : p.toFixed(1)}%`)

export function HomeworkCompletionPage({ scope }: { scope: HomeworkScope }) {
  const [params] = useSearchParams()
  const [owners, setOwners] = useState<OwnerOption[]>([])
  /** Teacher: the subjects of each owner come with the owner list. */
  const [teacherSubjects, setTeacherSubjects] = useState<Record<string, SubjectOption[]>>({})
  const [subjects, setSubjects] = useState<SubjectOption[]>([])
  /** The owner `subjects` belong to — the sheet waits until the list of a newly chosen owner is in. */
  const [subjectsFor, setSubjectsFor] = useState('')
  const [ownerId, setOwnerId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [from, setFrom] = useState(() => daysAgo(13))
  const [to, setTo] = useState(() => isoDay(new Date()))
  /** 0 = every lesson and pupil; 1/2 = one half of a split class. */
  const [half, setHalf] = useState<0 | 1 | 2>(0)

  const [loading, setLoading] = useState(true)
  const [sheet, setSheet] = useState<HomeworkSheet | null>(null)
  const [sheetLoading, setSheetLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  const [picker, setPicker] = useState<{ ri: number; ci: number; rect: DOMRect } | null>(null)
  const [info, setInfo] = useState<HomeworkLesson | null>(null)
  const cellRefs = useRef(new Map<string, HTMLButtonElement>())
  /** Bumped on every sheet request; an older answer arriving late is dropped. */
  const requestNo = useRef(0)

  /* ---------- pickers ---------- */

  useEffect(() => {
    const wanted = params.get('ownerId') ?? ''
    const load =
      scope === 'admin'
        ? getJournalOwners().then((list) => {
            setOwners(
              list.map((o) => ({
                id: o.id,
                label: o.kind === 'group' ? `${o.isTrack ? "Yo'nalish" : 'Guruh'}: ${o.name}` : `${o.name}-sinf`,
              })),
            )
            return list.map((o) => o.id)
          })
        : getMyClasses().then((list) => {
            const taught = list.filter((c) => c.subjects.length > 0)
            setOwners(taught.map((c) => ({ id: c.classId, label: c.className })))
            setTeacherSubjects(
              Object.fromEntries(taught.map((c) => [c.classId, c.subjects.map((s) => ({ id: s.id, name: s.name }))])),
            )
            return taught.map((c) => c.classId)
          })
    load
      .then((ids) => setOwnerId(ids.includes(wanted) ? wanted : (ids[0] ?? '')))
      .catch((err) => setError(errorText(err, "Sinflar ro'yxatini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once: the URL only seeds the first choice
  }, [scope])

  useEffect(() => {
    if (!ownerId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- no owner chosen: clear the subject list (intended)
      setSubjects([])
      return
    }
    const wanted = params.get('subjectId') ?? ''
    let cancelled = false
    const pick = (list: SubjectOption[]) => {
      if (cancelled) return
      setSubjects(list)
      // The same subject stays chosen when the new class has it too.
      setSubjectId((prev) =>
        list.some((s) => s.id === prev) ? prev : list.some((s) => s.id === wanted) ? wanted : (list[0]?.id ?? ''),
      )
      setSubjectsFor(ownerId)
    }
    if (scope === 'teacher') pick(teacherSubjects[ownerId] ?? [])
    else
      getJournalSubjects(ownerId)
        .then((list) => pick(list.map((s) => ({ id: s.subjectId, name: s.subjectName }))))
        .catch(() => pick([]))
    setHalf(0)
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the URL subject only seeds the first choice
  }, [ownerId, scope, teacherSubjects])

  const rangeError = useMemo(() => {
    if (!from || !to) return 'Sanalarni tanlang.'
    if (from > to) return 'Boshlanish sanasi tugash sanasidan keyin bo\'lmasin.'
    const days = Math.round((new Date(to).getTime() - new Date(from).getTime()) / 86_400_000) + 1
    if (days > MAX_RANGE_DAYS) return `Oraliq ${MAX_RANGE_DAYS} kundan oshmasin.`
    return null
  }, [from, to])

  /* ---------- the sheet ---------- */

  const loadSheet = useCallback(
    (quiet = false) => {
      if (!ownerId || !subjectId || subjectsFor !== ownerId || rangeError) {
        setSheet(null)
        return
      }
      const no = ++requestNo.current
      if (!quiet) setSheetLoading(true)
      getHomeworkSheet(scope, { ownerId, subjectId, from, to })
        .then((s) => {
          if (no !== requestNo.current) return
          setSheet(s)
          setError(null)
        })
        .catch((err) => {
          if (no !== requestNo.current) return
          setError(errorText(err, "Jadvalni yuklab bo'lmadi"))
          if (!quiet) setSheet(null)
        })
        .finally(() => {
          if (no === requestNo.current) setSheetLoading(false)
        })
    },
    [scope, ownerId, subjectId, subjectsFor, from, to, rangeError],
  )
  /** The latest `loadSheet` — a save that finishes after the filters changed re-reads the NEW sheet. */
  const loadSheetRef = useRef(loadSheet)

  useEffect(() => {
    loadSheetRef.current = loadSheet
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the filters changed: read the sheet again (intended)
    loadSheet()
  }, [loadSheet])

  const hasHalves = (sheet?.lessons ?? []).some((l) => l.subGroup > 0)
  const visibleCols = useMemo(
    () =>
      (sheet?.lessons ?? [])
        .map((l, ci) => ({ l, ci }))
        .filter(({ l }) => half === 0 || l.subGroup === 0 || l.subGroup === half)
        .map(({ ci }) => ci),
    [sheet, half],
  )
  const visibleRows = useMemo(
    () =>
      (sheet?.rows ?? [])
        .map((r, ri) => ({ r, ri }))
        .filter(({ r }) => half === 0 || r.subGroup === half)
        .map(({ ri }) => ri),
    [sheet, half],
  )

  /** Applies statuses locally (totals recounted) and saves them; the sheet is re-read afterwards. */
  const applyMarks = async (changes: { ri: number; ci: number; status: number }[]) => {
    if (!sheet || changes.length === 0) return
    setSaveError(null)
    const rows = sheet.rows.map((r) => ({ ...r, cells: [...r.cells] }))
    for (const { ri, ci, status } of changes)
      rows[ri].cells[ci] = { ...rows[ri].cells[ci], status, derived: false }
    const colStatuses = (ci: number) => rows.filter((r) => r.cells[ci].inLesson).map((r) => r.cells[ci].status)
    setSheet({
      ...sheet,
      rows: rows.map((r) => ({ ...r, summary: homeworkTally(r.cells.filter((c) => c.inLesson).map((c) => c.status)) })),
      lessons: sheet.lessons.map((l, ci) => ({ ...l, summary: homeworkTally(colStatuses(ci)) })),
      total: homeworkTally(sheet.lessons.flatMap((_, ci) => colStatuses(ci))),
    })
    try {
      await saveHomeworkMarks(
        scope,
        sheet.ownerId,
        sheet.subjectId,
        changes.map(({ ri, ci, status }) => ({
          studentId: sheet.rows[ri].studentId,
          date: sheet.lessons[ci].date,
          period: sheet.lessons[ci].period,
          subGroup: sheet.lessons[ci].subGroup,
          status,
        })),
      )
    } catch (err) {
      setSaveError(errorText(err, 'Saqlashda xatolik'))
    }
    loadSheetRef.current(true)
  }

  const fillColumn = (ci: number) => {
    if (!sheet) return
    const empty = visibleRows.filter((ri) => {
      const c = sheet.rows[ri].cells[ci]
      return c.inLesson && c.status === HW_NONE && !c.derived
    })
    if (empty.length === 0) return
    const l = sheet.lessons[ci]
    if (!confirm(`${formatDate(l.date)} · ${l.period}-dars: ${empty.length} ta bo'sh katakka «+ Bajardi» qo'yilsinmi?`))
      return
    void applyMarks(empty.map((ri) => ({ ri, ci, status: HW_DONE })))
  }

  /* ---------- keyboard ---------- */

  const focusCell = (ri: number, ci: number) => cellRefs.current.get(`${ri}:${ci}`)?.focus()

  /** Next focusable cell from (ri, ci) in a direction — cells outside the lesson have no button. */
  const move = (ri: number, ci: number, dr: number, dc: number) => {
    let r = visibleRows.indexOf(ri)
    let c = visibleCols.indexOf(ci)
    for (;;) {
      r += dr
      c += dc
      if (r < 0 || r >= visibleRows.length || c < 0 || c >= visibleCols.length) return
      const el = cellRefs.current.get(`${visibleRows[r]}:${visibleCols[c]}`)
      if (el) {
        el.focus()
        return
      }
    }
  }

  const onCellKey = (e: React.KeyboardEvent, ri: number, ci: number) => {
    const arrows: Record<string, [number, number]> = {
      ArrowUp: [-1, 0],
      ArrowDown: [1, 0],
      ArrowLeft: [0, -1],
      ArrowRight: [0, 1],
    }
    if (arrows[e.key]) {
      e.preventDefault()
      move(ri, ci, ...arrows[e.key])
      return
    }
    const status = homeworkFromKey(e.key)
    if (status == null || e.ctrlKey || e.metaKey || e.altKey) return
    e.preventDefault()
    const cell = sheet?.rows[ri].cells[ci]
    if (cell && (cell.status !== status || cell.derived)) void applyMarks([{ ri, ci, status }])
  }

  const choose = (status: number) => {
    if (!picker) return
    const { ri, ci } = picker
    setPicker(null)
    focusCell(ri, ci)
    const cell = sheet?.rows[ri].cells[ci]
    if (cell && (cell.status !== status || cell.derived)) void applyMarks([{ ri, ci, status }])
  }

  const onExport = async () => {
    if (!ownerId || !subjectId || rangeError) return
    setExporting(true)
    try {
      await downloadHomeworkSheet(scope, { ownerId, subjectId, from, to })
    } catch (err) {
      setSaveError(errorText(err, "Excel faylni yuklab bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  const presets = [
    { label: 'Shu hafta', from: mondayOfThisWeek() },
    { label: '2 hafta', from: daysAgo(13) },
    { label: 'Shu oy', from: firstOfThisMonth() },
  ]
  const today = isoDay(new Date())

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Uy vazifasi bajarilishi</h1>
          <p className="text-sm text-slate-400">Har bir o'quvchi uy vazifasini qanday bajargani — dars bo'yicha</p>
        </div>
        <Button
          variant="secondary"
          onClick={() => void onExport()}
          disabled={!sheet || sheet.rows.length === 0 || sheet.lessons.length === 0 || exporting}
        >
          <Download className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
        </Button>
      </header>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : owners.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-slate-400">
            {error ?? (scope === 'teacher' ? "Sizga biriktirilgan sinf/fan yo'q" : "Sinflar yo'q")}
          </p>
        </Card>
      ) : (
        <>
          {/* Filters */}
          <div className="flex flex-wrap items-center gap-3">
            <select value={ownerId} onChange={(e) => setOwnerId(e.target.value)} className={control} aria-label="Sinf yoki guruh">
              {owners.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.label}
                </option>
              ))}
            </select>
            <select
              value={subjectId}
              onChange={(e) => setSubjectId(e.target.value)}
              className={control}
              disabled={subjects.length === 0}
              aria-label="Fan"
            >
              {subjects.length === 0 ? (
                <option value="">Fan yo'q</option>
              ) : (
                subjects.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))
              )}
            </select>
            {hasHalves && (
              <select
                value={half}
                onChange={(e) => setHalf(Number(e.target.value) as 0 | 1 | 2)}
                className={cn(
                  control,
                  half === 1 ? 'border-sky-300 bg-sky-50 text-sky-800' : half === 2 ? 'border-violet-300 bg-violet-50 text-violet-800' : '',
                )}
                aria-label="Guruh"
              >
                <option value={0}>Hammasi</option>
                <option value={1}>1-guruh</option>
                <option value={2}>2-guruh</option>
              </select>
            )}
            <div className="flex items-center gap-2">
              <DatePicker value={from} onChange={setFrom} max={to || undefined} ariaLabel="Davr boshi" className="w-40" />
              <span className="text-slate-400">—</span>
              <DatePicker value={to} onChange={setTo} min={from || undefined} ariaLabel="Davr oxiri" className="w-40" />
            </div>
            <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
              {presets.map((p) => {
                const on = from === p.from && to === today
                return (
                  <button
                    key={p.label}
                    type="button"
                    onClick={() => {
                      setFrom(p.from)
                      setTo(today)
                    }}
                    className={cn(
                      'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                      on ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                    )}
                  >
                    {p.label}
                  </button>
                )
              })}
            </div>
          </div>

          <Legend />

          {rangeError && <p className="text-sm text-red-600">{rangeError}</p>}
          {saveError && <p className="text-sm font-medium text-red-600">{saveError}</p>}

          {sheetLoading || subjectsFor !== ownerId ? (
            <Loader label="Yuklanmoqda..." />
          ) : error ? (
            <Card>
              <p className="py-8 text-center text-red-600">{error}</p>
            </Card>
          ) : subjects.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">Bu sinfda fanlar yo'q — avval dars jadvali yarating</p>
            </Card>
          ) : !sheet ? null : sheet.lessons.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                Bu oraliqda dars yo'q — sanalarni o'zgartiring yoki haftalarga jadval biriktirilganini tekshiring
              </p>
            </Card>
          ) : sheet.rows.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                {sheet.ownerKind === 'group' ? "Bu guruhda o'quvchilar yo'q" : "Bu sinfda o'quvchilar yo'q"}
              </p>
            </Card>
          ) : (
            <Card className="min-w-0 overflow-hidden p-0">
              <div className="overflow-x-auto">
                <table className="w-max min-w-full border-separate border-spacing-0 text-sm">
                  <thead>
                    <tr className="text-slate-500">
                      <th className="sticky left-0 z-20 w-12 min-w-12 border-b border-slate-200 bg-slate-50 px-2 py-2 text-center text-xs font-medium">
                        №
                      </th>
                      <th className="sticky left-12 z-20 min-w-[15rem] border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase tracking-wide">
                        F.I.SH
                      </th>
                      {visibleCols.map((ci) => (
                        <LessonHeader
                          key={ci}
                          lesson={sheet.lessons[ci]}
                          emptyCount={
                            visibleRows.filter((ri) => {
                              const c = sheet.rows[ri].cells[ci]
                              return c.inLesson && c.status === HW_NONE && !c.derived
                            }).length
                          }
                          onInfo={() => setInfo(sheet.lessons[ci])}
                          onFill={() => fillColumn(ci)}
                        />
                      ))}
                      {HOMEWORK_STATUSES.map((s, i) => (
                        <th
                          key={s.value}
                          title={s.label}
                          className={cn(
                            'min-w-[2.75rem] border-b border-slate-200 bg-slate-50 px-1 py-2 text-center text-base font-bold',
                            i === 0 && 'border-l border-l-slate-200',
                            s.text,
                          )}
                        >
                          {s.symbol}
                        </th>
                      ))}
                      <th
                        title={HOMEWORK_RATE_HINT}
                        className="sticky right-0 z-20 min-w-[4.5rem] border-b border-l border-slate-200 bg-slate-50 px-2 py-2 text-center text-xs font-medium uppercase tracking-wide"
                      >
                        Bajarilish
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleRows.map((ri, idx) => {
                      const r = sheet.rows[ri]
                      return (
                        <tr key={r.studentId} className="group">
                          <td className="sticky left-0 z-10 w-12 border-b border-slate-100 bg-white px-2 py-2 text-center tabular-nums text-slate-400 group-hover:bg-slate-50">
                            {idx + 1}
                          </td>
                          <td className="sticky left-12 z-10 border-b border-r border-slate-200 bg-white px-3 py-2 group-hover:bg-slate-50">
                            <div className="flex items-center gap-1.5 whitespace-nowrap">
                              <span className="font-medium text-slate-800">{r.fullName}</span>
                              {r.subGroup > 0 && hasHalves && (
                                <span
                                  className={cn(
                                    'rounded px-1 text-[10px] font-semibold',
                                    r.subGroup === 1 ? 'bg-sky-100 text-sky-700' : 'bg-violet-100 text-violet-700',
                                  )}
                                >
                                  G{r.subGroup}
                                </span>
                              )}
                            </div>
                          </td>
                          {visibleCols.map((ci) => {
                            const cell = r.cells[ci]
                            if (!cell.inLesson)
                              return (
                                <td
                                  key={ci}
                                  className="border-b border-l border-slate-100 bg-slate-50/60 text-center text-slate-300"
                                  title="Bu guruh darsiga kirmaydi"
                                >
                                  —
                                </td>
                              )
                            const meta = homeworkMeta(cell.status)
                            return (
                              <td key={ci} className={cn('border-b border-l border-slate-100 p-0 text-center', meta && !cell.derived && meta.soft)}>
                                <button
                                  type="button"
                                  ref={(el) => {
                                    const k = `${ri}:${ci}`
                                    if (el) cellRefs.current.set(k, el)
                                    else cellRefs.current.delete(k)
                                  }}
                                  onClick={(e) => setPicker({ ri, ci, rect: e.currentTarget.getBoundingClientRect() })}
                                  onKeyDown={(e) => onCellKey(e, ri, ci)}
                                  title={
                                    cell.derived
                                      ? 'Kelmadi — davomatdan (darsda yo\'q edi). Belgilash uchun bosing'
                                      : meta
                                        ? `Uy vazifasi: ${meta.label}`
                                        : 'Belgilash: + − * K Y'
                                  }
                                  className="flex h-10 w-full items-center justify-center outline-none transition-colors hover:bg-brand-50/60 focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-400"
                                >
                                  {meta && (
                                    <span
                                      className={cn(
                                        'text-base font-bold leading-none',
                                        cell.derived ? 'font-semibold italic text-slate-300' : meta.text,
                                      )}
                                    >
                                      {meta.symbol}
                                    </span>
                                  )}
                                </button>
                              </td>
                            )
                          })}
                          <SummaryCells tally={r.summary} />
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot>
                    <tr className="text-slate-500">
                      <td className="sticky left-0 z-10 border-t border-slate-200 bg-slate-50" />
                      <td className="sticky left-12 z-10 border-r border-t border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium uppercase tracking-wide">
                        Bajarilish (dars)
                      </td>
                      {visibleCols.map((ci) => {
                        const p = sheet.lessons[ci].summary.percent
                        return (
                          <td key={ci} className="border-l border-t border-slate-200 bg-slate-50 px-1 py-2 text-center">
                            <span className={cn('text-xs font-semibold tabular-nums', percentColor(p))}>{fmtPct(p)}</span>
                          </td>
                        )
                      })}
                      <SummaryCells tally={sheet.total} footer />
                    </tr>
                  </tfoot>
                </table>
              </div>
            </Card>
          )}
        </>
      )}

      {picker && sheet && (
        <StatusPicker
          rect={picker.rect}
          current={sheet.rows[picker.ri].cells[picker.ci]}
          title={`${sheet.rows[picker.ri].fullName} · ${formatDate(sheet.lessons[picker.ci].date).slice(0, 5)}, ${sheet.lessons[picker.ci].period}-dars`}
          onChoose={choose}
          onClose={() => {
            const { ri, ci } = picker
            setPicker(null)
            focusCell(ri, ci)
          }}
        />
      )}

      <Modal
        open={!!info}
        onClose={() => setInfo(null)}
        size="sm"
        title={info ? `${formatDate(info.date)} · ${info.period}-dars` : ''}
        footer={<Button onClick={() => setInfo(null)}>Yopish</Button>}
      >
        {info && (
          <div className="space-y-3 text-sm">
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">Mavzu</p>
              <p className="mt-0.5 text-slate-700">{info.topic?.trim() || '—'}</p>
            </div>
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">Uyga vazifa</p>
              <p className="mt-0.5 whitespace-pre-wrap text-slate-700">{info.homework?.trim() || '—'}</p>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}

/* ==========================================================================
   Small parts
   ========================================================================== */

/** The words behind every symbol — always on screen, so a mark never has to be guessed. */
function Legend() {
  return (
    <div className="space-y-1.5">
      <div className="flex flex-wrap items-center gap-2 text-xs">
        {HOMEWORK_STATUSES.map((s) => (
          <span key={s.value} className="inline-flex items-center gap-1.5 rounded-full bg-white px-2.5 py-1 ring-1 ring-slate-200">
            <b className={cn('text-sm leading-none', s.text)}>{s.symbol}</b>
            <span className="text-slate-600">{s.label}</span>
          </span>
        ))}
        <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-50 px-2.5 py-1 text-slate-500">
          <b className="text-sm italic leading-none text-slate-300">Y</b> davomatdan (darsda yo'q edi)
        </span>
      </div>
      <p className="text-xs text-slate-400">
        {HOMEWORK_RATE_HINT} Klaviatura: katakni tanlab <b>+ − * K Y</b> bosing, <b>0</b> — tozalash, strelkalar — katakdan
        katakka.
      </p>
    </div>
  )
}

function LessonHeader({
  lesson: l,
  emptyCount,
  onInfo,
  onFill,
}: {
  lesson: HomeworkLesson
  emptyCount: number
  onInfo: () => void
  onFill: () => void
}) {
  const homeworkTip = l.hasHomework ? `Uyga vazifa: ${l.homework}` : "Bu darsda uyga vazifa yozilmagan"
  return (
    <th className="min-w-[4.75rem] border-b border-l border-slate-100 bg-slate-50 px-1.5 py-2 text-center font-normal">
      <button type="button" onClick={onInfo} title={`${homeworkTip} — batafsil`} className="w-full rounded-md hover:bg-slate-100">
        <div className="text-xs font-semibold tabular-nums text-slate-700">{formatDate(l.date).slice(0, 5)}</div>
        <div className="text-[10px] uppercase tracking-wide text-slate-400">
          {weekdayShort(l.date)} · {l.period}-dars
        </div>
      </button>
      <div className="mt-1 flex items-center justify-center gap-1">
        {l.subGroup > 0 && (
          <span
            className={cn(
              'rounded px-1 text-[10px] font-semibold',
              l.subGroup === 1 ? 'bg-sky-100 text-sky-700' : 'bg-violet-100 text-violet-700',
            )}
            title={`${l.subGroup}-guruh darsi`}
          >
            G{l.subGroup}
          </span>
        )}
        {l.hasHomework && (
          <button type="button" onClick={onInfo} title={homeworkTip} className="flex h-5 w-5 items-center justify-center rounded-full bg-brand-50 text-brand-600">
            <NotebookText className="h-3 w-3" />
          </button>
        )}
        <button
          type="button"
          onClick={onFill}
          disabled={emptyCount === 0}
          title={emptyCount === 0 ? "Bo'sh katak yo'q" : `Hammasi bajardi — ${emptyCount} ta bo'sh katakka +`}
          className="flex h-5 w-5 items-center justify-center rounded-full bg-emerald-50 text-emerald-600 transition-colors hover:bg-emerald-100 disabled:bg-slate-100 disabled:text-slate-300"
        >
          <CheckCheck className="h-3 w-3" />
        </button>
      </div>
    </th>
  )
}

/** Right-hand totals: one count per status and the completion rate. */
function SummaryCells({ tally, footer = false }: { tally: HomeworkTally; footer?: boolean }) {
  const base = footer ? 'border-t border-slate-200 bg-slate-50' : 'border-b border-slate-100 bg-white group-hover:bg-slate-50'
  return (
    <>
      {HOMEWORK_STATUSES.map((s, i) => {
        const n = tallyCount(tally, s.value)
        return (
          <td key={s.value} className={cn(base, 'px-1 py-2 text-center tabular-nums', i === 0 && 'border-l border-l-slate-200')}>
            <span className={n ? cn('text-sm font-semibold', footer ? 'text-slate-700' : 'text-slate-600') : 'text-slate-300'}>{n}</span>
          </td>
        )
      })}
      <td className={cn(base, 'sticky right-0 z-10 border-l border-l-slate-200 px-2 py-2 text-center')}>
        <span className={cn('text-sm font-semibold tabular-nums', percentColor(tally.percent))}>{fmtPct(tally.percent)}</span>
      </td>
    </>
  )
}

/**
 * The five statuses and "Tozalash" next to the clicked cell. Fixed to the window (the table scrolls
 * sideways, an absolute panel would be clipped); closes on Escape, an outside click or a scroll.
 */
function StatusPicker({
  rect,
  current,
  title,
  onChoose,
  onClose,
}: {
  rect: DOMRect
  current: { status: number; derived: boolean }
  title: string
  onChoose: (status: number) => void
  onClose: () => void
}) {
  const panelRef = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const panel = panelRef.current
    if (!panel) return
    const margin = 8
    const below = rect.bottom + 4
    const top = below + panel.offsetHeight + margin > window.innerHeight ? rect.top - panel.offsetHeight - 4 : below
    const left = Math.min(Math.max(margin, rect.left + rect.width / 2 - panel.offsetWidth / 2), window.innerWidth - panel.offsetWidth - margin)
    panel.style.top = `${Math.max(margin, top)}px`
    panel.style.left = `${left}px`
    panel.querySelector<HTMLButtonElement>('[data-current="true"]')?.focus()
    if (!panel.contains(document.activeElement)) panel.querySelector<HTMLButtonElement>('button')?.focus()
  }, [rect])

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (!panelRef.current?.contains(e.target as Node)) onClose()
    }
    const onScroll = (e: Event) => {
      if (!panelRef.current?.contains(e.target as Node)) onClose()
    }
    document.addEventListener('mousedown', onDown)
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onClose)
    return () => {
      document.removeEventListener('mousedown', onDown)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onClose)
    }
  }, [onClose])

  const stored = current.derived ? HW_NONE : current.status

  return (
    <div
      ref={panelRef}
      role="dialog"
      aria-label={title}
      style={{ top: rect.bottom + 4, left: rect.left }}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.preventDefault()
          onClose()
          return
        }
        const status = homeworkFromKey(e.key)
        if (status != null && e.key !== 'Backspace' && e.key !== 'Delete') {
          e.preventDefault()
          onChoose(status)
        }
      }}
      className="fixed z-50 w-72 rounded-2xl border border-slate-200 bg-white p-2 shadow-xl"
    >
      <p className="truncate px-1.5 pb-1.5 pt-0.5 text-xs font-medium text-slate-400">{title}</p>
      <div className="grid grid-cols-5 gap-1">
        {HOMEWORK_STATUSES.map((s) => {
          const on = stored === s.value
          return (
            <button
              key={s.value}
              type="button"
              data-current={on}
              onClick={() => onChoose(s.value)}
              title={s.label}
              className={cn(
                'flex h-14 flex-col items-center justify-center gap-0.5 rounded-xl ring-1 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400',
                on ? s.solid : 'bg-white ring-slate-200 hover:bg-slate-50',
              )}
            >
              <span className={cn('text-lg font-bold leading-none', on ? '' : s.text)}>{s.symbol}</span>
              <span className={cn('px-0.5 text-center text-[9px] font-medium leading-tight', on ? 'text-white/90' : 'text-slate-500')}>
                {s.label}
              </span>
            </button>
          )
        })}
      </div>
      {stored !== HW_NONE && (
        <button
          type="button"
          onClick={() => onChoose(HW_NONE)}
          className="mt-1 w-full rounded-lg px-2 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50"
        >
          Tozalash
        </button>
      )}
    </div>
  )
}
