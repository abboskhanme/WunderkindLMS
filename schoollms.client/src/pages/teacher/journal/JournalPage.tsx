import { useEffect, useMemo, useState } from 'react'
import { NotebookText, Check } from 'lucide-react'
import type {
  AbsenceReason,
  JournalColumn,
  JournalEntry,
  JournalTopic,
  QuarterGradeRow,
  QuarterPeriod,
  Student,
  TeacherClass,
} from '@/types'
import {
  getMyClasses,
  getTeacherMeta,
  getTeacherStudents,
  getTeacherLessons,
  getTeacherEntries,
  setTeacherEntry,
  clearTeacherEntry,
  getTeacherTopics,
  setTeacherNote,
  getTeacherQuarterGrades,
  setTeacherQuarterGrade,
} from '@/api/services/teacher'
import { quarters } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { JournalCellModal } from '@/pages/admin/journal/JournalCellModal'
import { QuarterGradeModal } from '@/pages/admin/journal/QuarterGradeModal'

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

export function TeacherJournalPage() {
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [metaQuarters, setMetaQuarters] = useState<QuarterPeriod[]>([])
  const [students, setStudents] = useState<Student[]>([])
  const [loading, setLoading] = useState(true)

  const [classId, setClassId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [quarter, setQuarter] = useState(1)

  const [columns, setColumns] = useState<JournalColumn[]>([])
  const [entries, setEntries] = useState<JournalEntry[]>([])
  const [topics, setTopics] = useState<JournalTopic[]>([])
  const [quarterGrades, setQuarterGrades] = useState<QuarterGradeRow[]>([])
  const [dataLoading, setDataLoading] = useState(false)

  const [editing, setEditing] = useState<{ student: Student; date: string; period: number } | null>(null)
  const [editingQuarter, setEditingQuarter] = useState<Student | null>(null)

  useEffect(() => {
    Promise.all([getMyClasses(), getTeacherMeta()])
      .then(([cl, meta]) => {
        setClasses(cl)
        setReasons(meta?.absenceReasons ?? [])
        setMetaQuarters(meta?.quarters ?? [])
        setClassId(cl[0]?.classId ?? '')
        if (meta?.currentQuarter) setQuarter(meta.currentQuarter)
      })
      .finally(() => setLoading(false))
  }, [])

  const selectedClass = classes.find((c) => c.classId === classId) ?? null
  const classSubjects = useMemo(() => selectedClass?.subjects ?? [], [selectedClass])

  // Tanlangan chorak uchun chorak bahosini kiritish admin tomonidan ochilganmi.
  const quarterOpen = metaQuarters.find((q) => q.quarter === quarter)?.gradesOpen ?? false

  // Sinf o'zgarsa — fanni moslab, o'quvchilarni yuklaymiz.
  useEffect(() => {
    const ids = classSubjects.map((s) => s.id)
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf almashganda fanni moslash (maqsadli)
    setSubjectId((prev) => (ids.includes(prev) ? prev : (ids[0] ?? '')))
    if (!classId) {
      setStudents([])
      return
    }
    getTeacherStudents(classId).then(setStudents)
  }, [classId, classSubjects])

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
      getTeacherLessons(classId, subjectId, quarter),
      getTeacherEntries(classId, subjectId, quarter),
      getTeacherTopics(classId, subjectId, quarter),
      getTeacherQuarterGrades(classId, subjectId, quarter),
    ])
      .then(([cols, ents, tps, qg]) => {
        setColumns(cols)
        setEntries(ents)
        setTopics(tps)
        setQuarterGrades(qg)
      })
      .finally(() => setDataLoading(false))
  }, [classId, subjectId, quarter])

  const entryFor = (studentId: string, date: string, period: number) =>
    entries.find((e) => e.studentId === studentId && e.date === date && e.period === period) ?? null
  const reasonShort = (rid: string) => reasons.find((r) => r.id === rid)?.short ?? '?'
  const reasonIsLate = (rid: string) => reasons.find((r) => r.id === rid)?.isLate ?? false
  const topicFor = (date: string, period: number) =>
    topics.find((t) => t.date === date && t.period === period)?.topic ?? ''
  const homeworkFor = (date: string, period: number) =>
    topics.find((t) => t.date === date && t.period === period)?.homework ?? ''
  const conductedFor = (date: string, period: number) =>
    topics.find((t) => t.date === date && t.period === period)?.conducted ?? false

  const quarterAvg = (studentId: string): number | null => {
    const vals = columns
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

  const markConductedLocal = (date: string, period: number) =>
    setTopics((prev) => {
      const existing = prev.find((t) => t.date === date && t.period === period)
      if (existing?.conducted) return prev
      const merged = {
        date,
        period,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: true,
      }
      return [...prev.filter((t) => !(t.date === date && t.period === period)), merged]
    })

  const handleSaveCell = (grade: number | null, reasonId: string | null) => {
    if (!editing) return
    const { student, date, period } = editing
    if (grade == null && reasonId == null) {
      setEntries((prev) =>
        prev.filter((e) => !(e.studentId === student.id && e.date === date && e.period === period)),
      )
      clearTeacherEntry(classId, subjectId, quarter, student.id, date, period)
    } else {
      upsertLocal(student.id, date, period, { grade: grade ?? undefined, reasonId: reasonId ?? undefined })
      markConductedLocal(date, period)
      setTeacherEntry(classId, subjectId, quarter, student.id, date, period, { grade, reasonId })
    }
    setEditing(null)
  }

  const handleClearCell = () => {
    if (!editing) return
    const { student, date, period } = editing
    setEntries((prev) =>
      prev.filter((e) => !(e.studentId === student.id && e.date === date && e.period === period)),
    )
    clearTeacherEntry(classId, subjectId, quarter, student.id, date, period)
    setEditing(null)
  }

  const handleSetQuarterGrade = (grade: number) => {
    if (!editingQuarter) return
    const s = editingQuarter
    setQuarterGrades((prev) => {
      const rec = prev.find((q) => q.studentId === s.id)?.recommended
      return [...prev.filter((q) => q.studentId !== s.id), { studentId: s.id, grade, recommended: rec }]
    })
    setTeacherQuarterGrade(classId, subjectId, quarter, s.id, grade)
    setEditingQuarter(null)
  }

  const handleClearQuarterGrade = () => {
    if (!editingQuarter) return
    const s = editingQuarter
    setQuarterGrades((prev) => prev.map((q) => (q.studentId === s.id ? { ...q, grade: undefined } : q)))
    setTeacherQuarterGrade(classId, subjectId, quarter, s.id, null)
    setEditingQuarter(null)
  }

  const handleNoteChange = (
    date: string,
    period: number,
    field: 'topic' | 'homework',
    value: string,
  ) =>
    setTopics((prev) => {
      const existing = prev.find((t) => t.date === date && t.period === period)
      const merged = {
        date,
        period,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: existing?.conducted ?? false,
        [field]: value,
      }
      return [...prev.filter((t) => !(t.date === date && t.period === period)), merged]
    })

  const handleNoteBlur = (date: string, period: number) =>
    setTeacherNote(
      classId,
      subjectId,
      quarter,
      date,
      period,
      topicFor(date, period),
      homeworkFor(date, period),
      conductedFor(date, period),
    )

  const handleToggleConducted = (date: string, period: number) => {
    const next = !conductedFor(date, period)
    setTopics((prev) => {
      const existing = prev.find((t) => t.date === date && t.period === period)
      const merged = {
        date,
        period,
        topic: existing?.topic ?? '',
        homework: existing?.homework ?? '',
        conducted: next,
      }
      return [...prev.filter((t) => !(t.date === date && t.period === period)), merged]
    })
    setTeacherNote(
      classId,
      subjectId,
      quarter,
      date,
      period,
      topicFor(date, period),
      homeworkFor(date, period),
      next,
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
      ) : classes.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-slate-400">Sizga biriktirilgan sinf/fan yo'q</p>
        </Card>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <select value={classId} onChange={(e) => setClassId(e.target.value)} className={control}>
              {classes.map((c) => (
                <option key={c.classId} value={c.classId}>
                  {c.className}
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
            <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
              {quarters.map((q) => (
                <button
                  key={q}
                  type="button"
                  onClick={() => setQuarter(q)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    q === quarter ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
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
              <p className="py-8 text-center text-slate-400">Bu sinfda dars beradigan faningiz yo'q</p>
            </Card>
          ) : columns.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                Bu fan uchun chorakda dars sanalari yo'q
              </p>
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-6 xl:grid-cols-3 xl:items-start">
              {/* Baholar jadvali */}
              <Card className="min-w-0 p-0 xl:col-span-2">
                <div className="overflow-x-auto">
                  <table className="w-full border-separate border-spacing-0 text-sm">
                    <thead>
                      <tr className="bg-slate-50 text-slate-400">
                        <th className="sticky left-0 z-10 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase">
                          F.I.SH
                        </th>
                        {columns.map((c) => (
                          <th key={`${c.date}-${c.period}`} className="px-1 py-2 text-center">
                            <div className="text-[10px] font-normal text-slate-400">{weekdayShort(c.date)}</div>
                            <div className="text-xs font-medium text-slate-500">
                              {formatDate(c.date).slice(0, 5)}
                            </div>
                            <div className="text-[10px] text-slate-400">{c.period}-dars</div>
                            <button
                              type="button"
                              onClick={() => handleToggleConducted(c.date, c.period)}
                              title={conductedFor(c.date, c.period) ? "Dars o'tildi" : "Dars o'tilmadi"}
                              className={cn(
                                'mx-auto mt-1 flex h-5 w-5 items-center justify-center rounded transition-colors',
                                conductedFor(c.date, c.period)
                                  ? 'bg-emerald-100 text-emerald-600'
                                  : 'bg-slate-100 text-slate-300 hover:text-slate-400',
                              )}
                            >
                              <Check className="h-3.5 w-3.5" />
                            </button>
                          </th>
                        ))}
                        <th className="px-3 py-2 text-center text-xs font-medium uppercase">
                          Chorak
                          {!quarterOpen && (
                            <span className="mt-0.5 block text-[9px] font-normal normal-case text-amber-600">
                              yopiq
                            </span>
                          )}
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {students.map((s) => {
                        const avg = quarterAvg(s.id)
                        const qGrade = quarterGradeFor(s.id)
                        return (
                          <tr key={s.id} className="border-t border-slate-100 hover:bg-slate-50/40">
                            <td className="sticky left-0 z-10 bg-white px-3 py-1.5 font-medium text-slate-800">
                              {s.fullName}
                            </td>
                            {columns.map((c) => {
                              const entry = entryFor(s.id, c.date, c.period)
                              const late = entry?.reasonId ? reasonIsLate(entry.reasonId) : false
                              return (
                                <td key={`${c.date}-${c.period}`} className="px-1 py-1 text-center">
                                  <button
                                    type="button"
                                    onClick={() => setEditing({ student: s, date: c.date, period: c.period })}
                                    title={entry?.reasonId ? reasonShort(entry.reasonId) : undefined}
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
                                    {entry?.grade != null && late && (
                                      <span className="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full bg-amber-400" />
                                    )}
                                  </button>
                                </td>
                              )
                            })}
                            <td className="px-3 py-1.5 text-center">
                              <button
                                type="button"
                                onClick={() => setEditingQuarter(s)}
                                disabled={!quarterOpen}
                                title={
                                  quarterOpen
                                    ? 'Chorak bahosini belgilash'
                                    : 'Chorak bahosini kiritish yopiq — administrator ochishi kerak'
                                }
                                className={cn(
                                  'mx-auto flex h-9 min-w-[2.75rem] flex-col items-center justify-center rounded-md border border-slate-200 px-2 transition-colors',
                                  quarterOpen
                                    ? 'hover:border-brand-300 hover:bg-brand-50'
                                    : 'cursor-not-allowed opacity-60',
                                )}
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
                      {students.length === 0 && (
                        <tr>
                          <td colSpan={columns.length + 2} className="px-4 py-10 text-center text-slate-400">
                            Bu sinfda o'quvchilar yo'q
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>

              {/* Mavzu va uyga vazifa */}
              <Card className="xl:col-span-1">
                <div className="mb-3 flex items-center gap-2">
                  <NotebookText className="h-4 w-4 text-brand-600" />
                  <h2 className="font-semibold text-slate-800">Mavzu va uyga vazifa</h2>
                </div>
                <div className="max-h-[34rem] space-y-3 overflow-y-auto pr-1">
                  {columns.map((c) => (
                    <div key={`${c.date}-${c.period}`} className="rounded-xl border border-slate-100 p-2.5">
                      <div className="mb-1.5 flex items-center justify-between gap-2">
                        <span className="text-xs font-semibold text-slate-500">
                          {weekdayShort(c.date)}, {formatDate(c.date).slice(0, 5)} · {c.period}-dars
                        </span>
                        <label
                          className={cn(
                            'flex cursor-pointer items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium',
                            conductedFor(c.date, c.period) ? 'text-emerald-600' : 'text-slate-400',
                          )}
                        >
                          <input
                            type="checkbox"
                            checked={conductedFor(c.date, c.period)}
                            onChange={() => handleToggleConducted(c.date, c.period)}
                          />
                          O'tildi
                        </label>
                      </div>
                      <input
                        value={topicFor(c.date, c.period)}
                        onChange={(e) => handleNoteChange(c.date, c.period, 'topic', e.target.value)}
                        onBlur={() => handleNoteBlur(c.date, c.period)}
                        placeholder="Mavzu"
                        className="mb-1.5 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm outline-none focus:border-brand-400"
                      />
                      <input
                        value={homeworkFor(c.date, c.period)}
                        onChange={(e) => handleNoteChange(c.date, c.period, 'homework', e.target.value)}
                        onBlur={() => handleNoteBlur(c.date, c.period)}
                        placeholder="Uyga vazifa"
                        className="w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm outline-none focus:border-brand-400"
                      />
                    </div>
                  ))}
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
        recommended={editingQuarter ? quarterAvg(editingQuarter.id) : null}
        onClose={() => setEditingQuarter(null)}
        onSetGrade={handleSetQuarterGrade}
        onClear={handleClearQuarterGrade}
      />
    </div>
  )
}
