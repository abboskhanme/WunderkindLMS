import { useEffect, useMemo, useState } from 'react'
import { NotebookText, Check } from 'lucide-react'
import type {
  AbsenceReason,
  JournalColumn,
  JournalEntry,
  JournalTopic,
  QuarterGradeRow,
  SchoolClass,
  ScheduleTemplate,
  Student,
  Subject,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getStudents } from '@/api/services/students'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getSettings } from '@/api/services/settings'
import {
  getJournalColumns,
  getJournalEntries,
  setJournalEntry,
  clearJournalEntry,
  getLessonNotes,
  setLessonNote,
  getQuarterGrades,
  setQuarterGrade,
} from '@/api/services/journal'
import { quarters } from '@/config/constants'
import { getCurrentQuarterAndWeek } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { JournalCellModal } from './JournalCellModal'
import { QuarterGradeModal } from './QuarterGradeModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const weekdayShort = (iso: string) =>
  ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh'][new Date(iso).getDay()] ?? ''

function gradeColor(g: number): string {
  if (g >= 5) return 'text-emerald-600'
  if (g === 4) return 'text-brand-600'
  if (g === 3) return 'text-amber-600'
  return 'text-red-600'
}

function avgColor(g: number): string {
  if (g >= 4.5) return 'text-emerald-600'
  if (g >= 3.5) return 'text-brand-600'
  if (g >= 2.5) return 'text-amber-600'
  return 'text-red-600'
}

export function JournalPage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [students, setStudents] = useState<Student[]>([])
  const [templates, setTemplates] = useState<ScheduleTemplate[]>([])
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)

  const [classId, setClassId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [quarter, setQuarter] = useState(1)
  /**
   * Guruh filtri: 0 = Butun sinf (SubGroup=0 darslari, hamma o'quvchi), 1/2 = mos guruh
   * (faqat shu guruh darslari va shu guruh o'quvchilari). Sinf/fan o'zgarsa 0 ga qaytadi.
   */
  const [groupFilter, setGroupFilter] = useState<0 | 1 | 2>(0)

  const [columns, setColumns] = useState<JournalColumn[]>([])
  const [entries, setEntries] = useState<JournalEntry[]>([])
  const [topics, setTopics] = useState<JournalTopic[]>([])
  const [quarterGrades, setQuarterGrades] = useState<QuarterGradeRow[]>([])
  const [dataLoading, setDataLoading] = useState(false)

  const [editing, setEditing] = useState<{ student: Student; date: string; period: number } | null>(
    null,
  )
  const [editingQuarter, setEditingQuarter] = useState<Student | null>(null)

  useEffect(() => {
    Promise.all([getClasses(), getSubjects(), getStudents(), getSettings()])
      .then(([cl, subs, st, settings]) => {
        setClasses(cl)
        setSubjects(subs)
        setStudents(st)
        setReasons(settings.absenceReasons)
        setClassId(cl[0]?.id ?? '')
        const { quarter: q } = getCurrentQuarterAndWeek(settings.quarters)
        setQuarter(q)
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!classId) return
    getTemplates(classId).then((tpls) => {
      setTemplates(tpls)
      const ids = [...new Set(tpls.flatMap((t) => t.lessons.map((l) => l.subjectId)))]
      setSubjectId(ids[0] ?? '')
    })
    // Sinf o'zgarsa guruh filtrini "Butun sinf"ga qaytaramiz.
    setGroupFilter(0)
  }, [classId])

  useEffect(() => {
    // Fan o'zgarsa ham guruh filtrini "Butun sinf"ga qaytaramiz.
    setGroupFilter(0)
  }, [subjectId])

  useEffect(() => {
    if (!classId || !subjectId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlov bo'sh bo'lganda jadvalni tozalaymiz (maqsadli)
      setColumns([])
      setEntries([])
      setTopics([])
      setQuarterGrades([])
      return
    }
    setDataLoading(true)
    Promise.all([
      getJournalColumns(classId, subjectId, quarter),
      getJournalEntries(classId, subjectId, quarter),
      getLessonNotes(classId, subjectId, quarter),
      getQuarterGrades(classId, subjectId, quarter),
    ])
      .then(([cols, ents, tps, qg]) => {
        setColumns(cols)
        setEntries(ents)
        setTopics(tps)
        setQuarterGrades(qg)
      })
      .finally(() => setDataLoading(false))
  }, [classId, subjectId, quarter])

  const selectedClass = classes.find((c) => c.id === classId) ?? null
  const allClassStudents = selectedClass
    ? students.filter((s) => s.className === selectedClass.name)
    : []

  // Sinfda guruh bo'linishi bormi (kamida bir o'quvchi G1/G2 da)? — guruh filtrini ko'rsatish-yashirish.
  const classIsGrouped = allClassStudents.some((s) => (s.subGroup ?? 0) > 0)

  // Joriy guruh filtri bo'yicha tanlangan o'quvchilar.
  // Butun sinf (0) — hamma; 1/2 — faqat shu guruh.
  const classStudents =
    groupFilter === 0
      ? allClassStudents
      : allClassStudents.filter((s) => (s.subGroup ?? 0) === groupFilter)

  // Joriy guruh filtri bo'yicha ustunlar (jurnal ustunlari ham guruh asosida ajralgan).
  const visibleColumns = columns.filter((c) => (c.subGroup ?? 0) === groupFilter)

  const classSubjects = useMemo(() => {
    const ids = [...new Set(templates.flatMap((t) => t.lessons.map((l) => l.subjectId)))]
    return ids
      .map((id) => subjects.find((s) => s.id === id))
      .filter((s): s is Subject => Boolean(s))
  }, [templates, subjects])

  const entryFor = (studentId: string, date: string, period: number) =>
    entries.find((e) => e.studentId === studentId && e.date === date && e.period === period) ?? null
  const reasonShort = (rid: string) => reasons.find((r) => r.id === rid)?.short ?? '?'
  const reasonIsLate = (rid: string) => reasons.find((r) => r.id === rid)?.isLate ?? false
  const topicFor = (date: string, period: number, subGroup: number) =>
    topics.find((t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)?.topic ?? ''
  const homeworkFor = (date: string, period: number, subGroup: number) =>
    topics.find((t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)?.homework ?? ''
  const conductedFor = (date: string, period: number, subGroup: number) =>
    topics.find((t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)?.conducted ?? false

  const quarterAvg = (studentId: string, studentSubGroup: number): number | null => {
    // O'quvchining guruhiga taalluqli ustunlar bo'yicha o'rtacha (SubGroup=0 hammaga).
    // Joriy filter bo'yicha emas — chorak bahosi o'quvchining JAMI baholaridan hisoblanadi.
    const vals = columns
      .filter((c) => (c.subGroup ?? 0) === 0 || (c.subGroup ?? 0) === studentSubGroup)
      .map((c) => entryFor(studentId, c.date, c.period)?.grade)
      .filter((g): g is number => g != null)
    if (!vals.length) return null
    return Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10
  }

  const quarterGradeFor = (studentId: string): number | null =>
    quarterGrades.find((q) => q.studentId === studentId)?.grade ?? null

  const upsertLocal = (
    studentId: string,
    date: string,
    period: number,
    payload: Partial<JournalEntry>,
  ) =>
    setEntries((prev) => [
      ...prev.filter((e) => !(e.studentId === studentId && e.date === date && e.period === period)),
      { studentId, date, period, ...payload },
    ])

  // Baho/davomat kiritilganda shu darsni "o'tildi" deb mahalliy belgilaymiz (backend ham auto-belgilaydi).
  const markConductedLocal = (date: string, period: number, subGroup: number) =>
    setTopics((prev) => {
      const existing = prev.find(
        (t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup,
      )
      if (existing?.conducted) return prev
      const merged: JournalTopic = {
        date,
        period,
        subGroup,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: true,
      }
      return [
        ...prev.filter((t) => !(t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)),
        merged,
      ]
    })

  // Baho va davomat sababini birga saqlaymiz (ikkalasi ham bo'sh bo'lsa — katakni tozalaymiz).
  const handleSaveCell = (grade: number | null, reasonId: string | null) => {
    if (!editing) return
    const { student, date, period } = editing
    if (grade == null && reasonId == null) {
      setEntries((prev) =>
        prev.filter((e) => !(e.studentId === student.id && e.date === date && e.period === period)),
      )
      clearJournalEntry(classId, subjectId, quarter, student.id, date, period)
    } else {
      upsertLocal(student.id, date, period, {
        grade: grade ?? undefined,
        reasonId: reasonId ?? undefined,
      })
      // O'quvchining guruhi mos darsni "o'tildi" deb belgilash uchun ishlatiladi.
      markConductedLocal(date, period, student.subGroup ?? 0)
      setJournalEntry(classId, subjectId, quarter, student.id, date, period, { grade, reasonId })
    }
    setEditing(null)
  }

  const handleClearCell = () => {
    if (!editing) return
    const { student, date, period } = editing
    setEntries((prev) =>
      prev.filter((e) => !(e.studentId === student.id && e.date === date && e.period === period)),
    )
    clearJournalEntry(classId, subjectId, quarter, student.id, date, period)
    setEditing(null)
  }

  const handleSetQuarterGrade = (grade: number) => {
    if (!editingQuarter) return
    const s = editingQuarter
    setQuarterGrades((prev) => {
      const rec = prev.find((q) => q.studentId === s.id)?.recommended
      return [...prev.filter((q) => q.studentId !== s.id), { studentId: s.id, grade, recommended: rec }]
    })
    setQuarterGrade(classId, subjectId, quarter, s.id, grade)
    setEditingQuarter(null)
  }

  const handleClearQuarterGrade = () => {
    if (!editingQuarter) return
    const s = editingQuarter
    setQuarterGrades((prev) => prev.map((q) => (q.studentId === s.id ? { ...q, grade: undefined } : q)))
    setQuarterGrade(classId, subjectId, quarter, s.id, null)
    setEditingQuarter(null)
  }

  const handleNoteChange = (
    date: string,
    period: number,
    subGroup: number,
    field: 'topic' | 'homework',
    value: string,
  ) =>
    setTopics((prev) => {
      const existing = prev.find(
        (t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup,
      )
      const merged: JournalTopic = {
        date,
        period,
        subGroup,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: existing?.conducted ?? false,
        [field]: value,
      }
      return [
        ...prev.filter((t) => !(t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)),
        merged,
      ]
    })

  const handleNoteBlur = (date: string, period: number, subGroup: number) =>
    setLessonNote(
      classId,
      subjectId,
      quarter,
      date,
      period,
      topicFor(date, period, subGroup),
      homeworkFor(date, period, subGroup),
      conductedFor(date, period, subGroup),
      subGroup,
    )

  // "Dars o'tildi" ptichkasini almashtirish (mavzu/uyga vazifa saqlanadi)
  const handleToggleConducted = (date: string, period: number, subGroup: number) => {
    const next = !conductedFor(date, period, subGroup)
    setTopics((prev) => {
      const existing = prev.find(
        (t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup,
      )
      const merged: JournalTopic = {
        date,
        period,
        subGroup,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: next,
      }
      return [
        ...prev.filter((t) => !(t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup)),
        merged,
      ]
    })
    setLessonNote(
      classId,
      subjectId,
      quarter,
      date,
      period,
      topicFor(date, period, subGroup),
      homeworkFor(date, period, subGroup),
      next,
      subGroup,
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Jurnal</h1>
        <p className="text-sm text-slate-400">Baholar, davomat va mavzular</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Tanlovlar */}
          <div className="flex flex-wrap items-center gap-3">
            <select value={classId} onChange={(e) => setClassId(e.target.value)} className={control}>
              {classes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}-sinf
                </option>
              ))}
            </select>
            <select
              value={subjectId}
              onChange={(e) => setSubjectId(e.target.value)}
              className={control}
              disabled={classSubjects.length === 0}
            >
              {classSubjects.length === 0 ? (
                <option value="">Fan yo'q</option>
              ) : (
                classSubjects.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))
              )}
            </select>
            {/* Guruh filtri — sinf bo'lingan bo'lsa ko'rinadi (dropdown tanlovi) */}
            {classIsGrouped && (
              <select
                value={groupFilter}
                onChange={(e) => setGroupFilter(Number(e.target.value) as 0 | 1 | 2)}
                className={cn(
                  control,
                  groupFilter === 1
                    ? 'border-sky-300 bg-sky-50 text-sky-800'
                    : groupFilter === 2
                      ? 'border-violet-300 bg-violet-50 text-violet-800'
                      : '',
                )}
                title="Guruh tanlash"
              >
                <option value={0}>Butun sinf</option>
                <option value={1}>1-guruh</option>
                <option value={2}>2-guruh</option>
              </select>
            )}
            <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
              {quarters.map((q) => (
                <button
                  key={q}
                  type="button"
                  onClick={() => setQuarter(q)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    q === quarter
                      ? 'bg-white text-brand-700 shadow-sm'
                      : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {q}-chorak
                </button>
              ))}
            </div>
            {reasons.length > 0 && (
              <div className="ml-auto flex flex-wrap items-center gap-2 text-xs text-slate-400">
                {reasons.map((r) => (
                  <span key={r.id}>
                    <b className="text-slate-600">{r.short}</b> — {r.name}
                  </span>
                ))}
              </div>
            )}
          </div>

          {dataLoading ? (
            <Loader label="Yuklanmoqda..." />
          ) : classSubjects.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                Bu sinfda fanlar yo'q — avval dars jadvali yarating
              </p>
            </Card>
          ) : columns.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                Bu fan uchun chorakda dars sanalari yo'q — haftalarga jadval biriktiring
              </p>
            </Card>
          ) : visibleColumns.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                {groupFilter === 0
                  ? "Bu fan butun sinf darslarini topmadi (faqat guruhlarga bo'lingan)"
                  : `Bu fanda ${groupFilter}-guruh uchun dars yo'q`}
              </p>
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-6 xl:grid-cols-3 xl:items-start">
              {/* Baholar jadvali (2/3) */}
              <Card className="min-w-0 p-0 xl:col-span-2">
                <div className="overflow-x-auto">
                  <table className="w-full border-separate border-spacing-0 text-sm">
                    <thead>
                      <tr className="bg-slate-50 text-slate-400">
                        <th className="sticky left-0 z-10 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase">
                          F.I.SH
                        </th>
                        {visibleColumns.map((c) => {
                          const sg = c.subGroup ?? 0
                          return (
                          <th key={`${c.date}-${c.period}-${sg}`} className="px-1 py-2 text-center">
                            <div className="text-[10px] font-normal text-slate-400">
                              {weekdayShort(c.date)}
                            </div>
                            <div className="text-xs font-medium text-slate-500">
                              {formatDate(c.date).slice(0, 5)}
                            </div>
                            <div className="text-[10px] text-slate-400">{c.period}-dars</div>
                            {sg > 0 && (
                              <div
                                className={cn(
                                  'mx-auto mt-0.5 inline-block rounded px-1 text-[10px] font-semibold',
                                  sg === 1
                                    ? 'bg-sky-100 text-sky-700'
                                    : 'bg-violet-100 text-violet-700',
                                )}
                                title={`${sg}-guruh darsi`}
                              >
                                G{sg}
                              </div>
                            )}
                            <button
                              type="button"
                              onClick={() => handleToggleConducted(c.date, c.period, sg)}
                              title={conductedFor(c.date, c.period, sg) ? "Dars o'tildi" : "Dars o'tilmadi"}
                              className={cn(
                                'mx-auto mt-1 flex h-5 w-5 items-center justify-center rounded transition-colors',
                                conductedFor(c.date, c.period, sg)
                                  ? 'bg-emerald-100 text-emerald-600'
                                  : 'bg-slate-100 text-slate-300 hover:text-slate-400',
                              )}
                            >
                              <Check className="h-3.5 w-3.5" />
                            </button>
                          </th>
                        )})}
                        <th className="px-3 py-2 text-center text-xs font-medium uppercase">
                          Chorak
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {classStudents.map((s) => {
                        const ssg = s.subGroup ?? 0
                        const avg = quarterAvg(s.id, ssg)
                        const qGrade = quarterGradeFor(s.id)
                        return (
                          <tr key={s.id} className="border-t border-slate-100 hover:bg-slate-50/40">
                            <td className="sticky left-0 z-10 bg-white px-3 py-1.5 font-medium text-slate-800">
                              <div className="flex items-center gap-1.5">
                                <span>{s.fullName}</span>
                                {ssg > 0 && (
                                  <span
                                    className={cn(
                                      'rounded px-1 text-[10px] font-semibold',
                                      ssg === 1
                                        ? 'bg-sky-100 text-sky-700'
                                        : 'bg-violet-100 text-violet-700',
                                    )}
                                    title={`${ssg}-guruh`}
                                  >
                                    G{ssg}
                                  </span>
                                )}
                              </div>
                            </td>
                            {visibleColumns.map((c) => {
                              const entry = entryFor(s.id, c.date, c.period)
                              const colSg = c.subGroup ?? 0
                              return (
                                <td key={`${c.date}-${c.period}-${colSg}`} className="px-1 py-1 text-center">
                                  {(() => {
                                    const late = entry?.reasonId ? reasonIsLate(entry.reasonId) : false
                                    return (
                                      <button
                                        type="button"
                                        onClick={() => setEditing({ student: s, date: c.date, period: c.period })}
                                        className={cn(
                                          'relative flex h-9 w-11 items-center justify-center rounded-md border text-sm font-semibold transition-colors',
                                          entry?.grade != null
                                            ? late
                                              ? 'border-amber-300 bg-amber-50'
                                              : 'border-slate-200 bg-white'
                                            : entry?.reasonId
                                              ? late
                                                ? 'border-amber-200 bg-amber-50'
                                                : 'border-red-200 bg-red-50'
                                              : 'border-slate-200 bg-slate-50 hover:border-brand-300 hover:bg-brand-50',
                                        )}
                                        title={entry?.reasonId ? reasonShort(entry.reasonId) : undefined}
                                      >
                                        {entry?.grade != null ? (
                                          <span className={gradeColor(entry.grade)}>{entry.grade}</span>
                                        ) : entry?.reasonId ? (
                                          <span
                                            className={cn(
                                              'text-xs font-medium',
                                              late ? 'text-amber-600' : 'text-red-600',
                                            )}
                                          >
                                            {reasonShort(entry.reasonId)}
                                          </span>
                                        ) : null}
                                        {/* Baho + kech kelgan bo'lsa — kichik sariq belgi */}
                                        {entry?.grade != null && late && (
                                          <span className="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full bg-amber-400" />
                                        )}
                                      </button>
                                    )
                                  })()}
                                </td>
                              )
                            })}
                            <td className="px-3 py-1.5 text-center">
                              <button
                                type="button"
                                onClick={() => setEditingQuarter(s)}
                                title="Chorak bahosini belgilash"
                                className="mx-auto flex h-9 min-w-[2.75rem] flex-col items-center justify-center rounded-md border border-slate-200 px-2 transition-colors hover:border-brand-300 hover:bg-brand-50"
                              >
                                {qGrade != null ? (
                                  <>
                                    <span className={cn('text-sm font-bold leading-none', gradeColor(qGrade))}>
                                      {qGrade}
                                    </span>
                                    {avg != null && (
                                      <span className="text-[10px] leading-tight text-slate-400">
                                        ≈{avg.toFixed(1)}
                                      </span>
                                    )}
                                  </>
                                ) : avg != null ? (
                                  <span className={cn('text-sm font-semibold', avgColor(avg))}>
                                    {avg.toFixed(1)}
                                  </span>
                                ) : (
                                  <span className="text-slate-300">—</span>
                                )}
                              </button>
                            </td>
                          </tr>
                        )
                      })}
                      {classStudents.length === 0 && (
                        <tr>
                          <td
                            colSpan={visibleColumns.length + 2}
                            className="px-4 py-10 text-center text-slate-400"
                          >
                            Bu sinfda o'quvchilar yo'q
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>

              {/* Mavzu va uyga vazifa (1/3) */}
              <Card className="xl:col-span-1">
                <div className="mb-3 flex items-center gap-2">
                  <NotebookText className="h-4 w-4 text-brand-600" />
                  <h2 className="font-semibold text-slate-800">Mavzu va uyga vazifa</h2>
                </div>
                <div className="max-h-[34rem] space-y-3 overflow-y-auto pr-1">
                  {visibleColumns.map((c) => {
                    const sg = c.subGroup ?? 0
                    return (
                    <div key={`${c.date}-${c.period}-${sg}`} className="rounded-xl border border-slate-100 p-2.5">
                      <div className="mb-1.5 flex items-center justify-between gap-2">
                        <span className="text-xs font-semibold text-slate-500">
                          {weekdayShort(c.date)}, {formatDate(c.date).slice(0, 5)} · {c.period}-dars
                          {sg > 0 && (
                            <span
                              className={cn(
                                'ml-1.5 rounded px-1 text-[10px] font-semibold',
                                sg === 1
                                  ? 'bg-sky-100 text-sky-700'
                                  : 'bg-violet-100 text-violet-700',
                              )}
                            >
                              G{sg}
                            </span>
                          )}
                        </span>
                        <label
                          className={cn(
                            'flex cursor-pointer items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium',
                            conductedFor(c.date, c.period, sg) ? 'text-emerald-600' : 'text-slate-400',
                          )}
                        >
                          <input
                            type="checkbox"
                            checked={conductedFor(c.date, c.period, sg)}
                            onChange={() => handleToggleConducted(c.date, c.period, sg)}
                            className="h-3.5 w-3.5 accent-emerald-600"
                          />
                          {conductedFor(c.date, c.period, sg) ? "Dars o'tildi" : "Dars o'tilmadi"}
                        </label>
                      </div>
                      <input
                        value={topicFor(c.date, c.period, sg)}
                        onChange={(e) => handleNoteChange(c.date, c.period, sg, 'topic', e.target.value)}
                        onBlur={() => handleNoteBlur(c.date, c.period, sg)}
                        placeholder="Mavzu..."
                        className="w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm outline-none focus:border-brand-400"
                      />
                      <input
                        value={homeworkFor(c.date, c.period, sg)}
                        onChange={(e) => handleNoteChange(c.date, c.period, sg, 'homework', e.target.value)}
                        onBlur={() => handleNoteBlur(c.date, c.period, sg)}
                        placeholder="Uyga vazifa..."
                        className="mt-1.5 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm outline-none focus:border-brand-400"
                      />
                    </div>
                  )})}
                </div>
              </Card>
            </div>
          )}
        </>
      )}

      <JournalCellModal
        open={!!editing}
        studentName={editing?.student.fullName ?? ''}
        dateLabel={editing ? `${formatDate(editing.date)} · ${editing.period}-dars` : ''}
        entry={editing ? entryFor(editing.student.id, editing.date, editing.period) : null}
        reasons={reasons}
        onClose={() => setEditing(null)}
        onSave={handleSaveCell}
        onClear={handleClearCell}
      />

      <QuarterGradeModal
        open={!!editingQuarter}
        studentName={editingQuarter?.fullName ?? ''}
        grade={editingQuarter ? quarterGradeFor(editingQuarter.id) : null}
        recommended={editingQuarter ? quarterAvg(editingQuarter.id, editingQuarter.subGroup ?? 0) : null}
        onClose={() => setEditingQuarter(null)}
        onSetGrade={handleSetQuarterGrade}
        onClear={handleClearQuarterGrade}
      />
    </div>
  )
}
