import { useEffect, useMemo, useRef, useState } from 'react'
import { NotebookText, Check, CheckCircle2, Download, Upload, ChevronRight } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import type {
  AbsenceReason,
  JournalColumn,
  JournalEntry,
  JournalTopic,
  QuarterGradeRow,
  ScheduleTemplate,
  Student,
  Subject,
} from '@/types'
import { getSubjects } from '@/api/services/subjects'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getSettings } from '@/api/services/settings'
import {
  getJournalColumns,
  getJournalEntries,
  getJournalOwners,
  getJournalStudents,
  setJournalEntry,
  clearJournalEntry,
  getLessonNotes,
  setLessonNote,
  getQuarterGrades,
  getJournalFormerStudents,
  setQuarterGrade,
  downloadTopicsTemplate,
  importTopics,
  type JournalOwner,
  type TopicImportResult,
} from '@/api/services/journal'
import { quarters } from '@/config/constants'
import { getCurrentQuarterAndWeek } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { JournalCellModal } from './JournalCellModal'
import { QuarterGradeModal } from './QuarterGradeModal'

/** Admin jurnalidagi "Mavzu va uyga vazifa" paneli (hozircha yashirin — izoh render'da). */
const SHOW_TOPICS_PANEL = false

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
  /**
   * EduSchool oqimi (2026-09-23): Jurnal → sinf → fan. `/admin/journal/:classId/:subjectId`
   * dan ochilganda sinf va fan URL'dan olinadi va tanlovlar o'rnida yo'l ko'rsatkichi turadi.
   */
  const params = useParams<{ classId?: string; subjectId?: string }>()
  const locked = Boolean(params.classId && params.subjectId)
  /**
   * Jurnal EGALARI — sinflar va (cut-over o'chirgichi yoqilgan bo'lsa) o'quv
   * guruhlari (docs/modules/students-parity.md §2.1.4, G-12). Ikkalasi ham
   * bitta `classId` parametri orqali so'raladi.
   */
  const [owners, setOwners] = useState<JournalOwner[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  /** Tanlangan eganing ro'yxati — SERVERDAN (brauzerda sinf nomi bo'yicha filtr yo'q). */
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
  /** Jurnalda bahosi bor, lekin ro'yxatda endi yo'q o'quvchilar (arxiv / boshqa sinf). */
  const [formerStudents, setFormerStudents] = useState<Student[]>([])
  const [dataLoading, setDataLoading] = useState(false)

  const [editing, setEditing] = useState<{ student: Student; date: string; period: number } | null>(
    null,
  )
  const [editingQuarter, setEditingQuarter] = useState<Student | null>(null)

  // Mavzularni Excel'dan yuklash.
  const fileRef = useRef<HTMLInputElement>(null)
  const [importing, setImporting] = useState(false)
  const [importResult, setImportResult] = useState<TopicImportResult | null>(null)

  const onDownloadTemplate = async () => {
    if (!classId || !subjectId) return
    try {
      await downloadTopicsTemplate(classId, subjectId, quarter)
    } catch {
      alert("Shablonni yuklab bo'lmadi")
    }
  }
  const onImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0]
    e.target.value = ''
    if (!f || !classId || !subjectId) return
    setImporting(true)
    try {
      const res = await importTopics(f, classId, subjectId, quarter)
      setImportResult(res)
      setTopics(await getLessonNotes(classId, subjectId, quarter))
    } catch (err) {
      alert((err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Import xatosi')
    } finally {
      setImporting(false)
    }
  }

  useEffect(() => {
    Promise.all([getJournalOwners(), getSubjects(), getSettings()])
      .then(([ow, subs, settings]) => {
        setOwners(ow)
        setSubjects(subs)
        setReasons(settings.absenceReasons)
        setClassId(params.classId ?? ow[0]?.id ?? '')
        const { quarter: q } = getCurrentQuarterAndWeek(settings.quarters)
        setQuarter(q)
      })
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat birinchi ochilishda: URL'dagi sinf boshlang'ich tanlov
  }, [])

  useEffect(() => {
    if (!classId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- ega tanlanmagan: ro'yxatni tozalaymiz (maqsadli)
      setStudents([])
      return
    }
    const ownerSubjectId = owners.find((o) => o.id === classId)?.subjectId ?? ''
    getTemplates(classId).then((tpls) => {
      setTemplates(tpls)
      const ids = [...new Set(tpls.flatMap((t) => t.lessons.map((l) => l.subjectId)))]
      // Guruhda jadval hali bo'lmasa — guruhning o'z fani tanlanadi.
      setSubjectId(locked && params.subjectId ? params.subjectId : (ids[0] ?? ownerSubjectId))
    })
    // Ro'yxat serverdan: sinfda — sinf nomi bo'yicha (bugungi qoida), guruhda — faol a'zolar.
    getJournalStudents(classId).then(setStudents)
    // Ega o'zgarsa guruh filtrini "Butun sinf"ga qaytaramiz.
    setGroupFilter(0)
  }, [classId, owners, locked, params.subjectId])

  useEffect(() => {
    // Fan o'zgarsa ham guruh filtrini "Butun sinf"ga qaytaramiz.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- fan almashganda filtrni tiklash (maqsadli)
    setGroupFilter(0)
  }, [subjectId])

  useEffect(() => {
    if (!classId || !subjectId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlov bo'sh bo'lganda jadvalni tozalaymiz (maqsadli)
      setColumns([])
      setEntries([])
      setTopics([])
      setQuarterGrades([])
      setFormerStudents([])
      return
    }
    setDataLoading(true)
    Promise.all([
      getJournalColumns(classId, subjectId, quarter),
      getJournalEntries(classId, subjectId, quarter),
      getLessonNotes(classId, subjectId, quarter),
      getQuarterGrades(classId, subjectId, quarter),
      getJournalFormerStudents(classId, subjectId, quarter).catch(() => [] as Student[]),
    ])
      .then(([cols, ents, tps, qg, former]) => {
        setColumns(cols)
        setEntries(ents)
        setTopics(tps)
        setQuarterGrades(qg)
        setFormerStudents(former)
      })
      .finally(() => setDataLoading(false))
  }, [classId, subjectId, quarter])

  const selectedOwner = owners.find((o) => o.id === classId) ?? null
  const selectedOwnerLabel = selectedOwner
    ? selectedOwner.kind === 'group'
      ? `Guruh: ${selectedOwner.name}`
      : `${selectedOwner.name}-sinf`
    : ''
  const isGroupOwner = selectedOwner?.kind === 'group'
  // Ro'yxat serverdan keladi va allaqachon shu egaga tegishli.
  const allClassStudents = students

  // Sinfda guruh bo'linishi bormi (kamida bir o'quvchi G1/G2 da)? — guruh filtrini ko'rsatish-yashirish.
  // O'quv guruhida sinf ichidagi bo'linish YO'Q (§2.1.4), shuning uchun filtr ham ko'rinmaydi.
  const classIsGrouped = !isGroupOwner && allClassStudents.some((s) => (s.subGroup ?? 0) > 0)

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
    // O'quv guruhida fan BITTA va u guruhning o'zida yozilgan — jadval hali
    // tuzilmagan bo'lsa ham ro'yxat bo'sh qolmasin (§2.1.4).
    if (isGroupOwner && selectedOwner?.subjectId && !ids.includes(selectedOwner.subjectId))
      ids.unshift(selectedOwner.subjectId)
    return ids
      .map((id) => subjects.find((s) => s.id === id))
      .filter((s): s is Subject => Boolean(s))
  }, [templates, subjects, isGroupOwner, selectedOwner])

  const entryFor = (studentId: string, date: string, period: number) =>
    entries.find((e) => e.studentId === studentId && e.date === date && e.period === period) ?? null
  const reasonShort = (rid: string) => reasons.find((r) => r.id === rid)?.short ?? '?'
  const reasonName = (rid: string) => reasons.find((r) => r.id === rid)?.name ?? reasonShort(rid)
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
  const handleSaveCell = (
    grade: number | null,
    reasonId: string | null,
    homework: number,
    behavior: number,
    mastery: number | null,
  ) => {
    if (!editing) return
    const { student, date, period } = editing
    if (grade == null && reasonId == null && homework === 0 && behavior === 0 && mastery == null) {
      setEntries((prev) =>
        prev.filter((e) => !(e.studentId === student.id && e.date === date && e.period === period)),
      )
      clearJournalEntry(classId, subjectId, quarter, student.id, date, period)
    } else {
      upsertLocal(student.id, date, period, {
        grade: grade ?? undefined,
        reasonId: reasonId ?? undefined,
        homework,
        behavior,
        mastery,
      })
      // O'quvchining guruhi mos darsni "o'tildi" deb belgilash uchun ishlatiladi.
      markConductedLocal(date, period, student.subGroup ?? 0)
      setJournalEntry(classId, subjectId, quarter, student.id, date, period, {
        grade,
        reasonId,
        homework,
        behavior,
        mastery,
      })
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
      {locked ? (
        <div>
          <nav className="flex flex-wrap items-center gap-1 text-sm text-slate-400" aria-label="Yo'l">
            <Link to="/admin/journal" className="hover:text-slate-700">
              Jurnal
            </Link>
            <ChevronRight className="h-4 w-4" />
            <Link to={`/admin/journal/${classId}`} className="hover:text-slate-700">
              {selectedOwnerLabel}
            </Link>
          </nav>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">
            {subjects.find((x) => x.id === subjectId)?.name ?? 'Jurnal'}
          </h1>
        </div>
      ) : (
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Jurnal</h1>
          <p className="text-sm text-slate-400">Baholar, davomat va mavzular</p>
        </div>
      )}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Tanlovlar */}
          <div className="flex flex-wrap items-center gap-3">
            {!locked && (
            <>
            <select value={classId} onChange={(e) => setClassId(e.target.value)} className={control}>
              {owners.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.kind === 'group' ? `Guruh: ${o.name}` : `${o.name}-sinf`}
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
            </>
            )}
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
            {/* Chorak — ro'yxatdan tanlanadi (mijoz, 2026-09-23). */}
            <select
              value={quarter}
              onChange={(e) => setQuarter(Number(e.target.value))}
              className={control}
              aria-label="Chorak"
            >
              {quarters.map((q) => (
                <option key={q} value={q}>
                  {q}-chorak
                </option>
              ))}
            </select>
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

          {/* Mavzularni Excel'dan ommaviy yuklash — sinf+fan tanlangan bo'lsa */}
          {classSubjects.length > 0 && subjectId && (
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="secondary" onClick={onDownloadTemplate}>
                <Download className="h-4 w-4" /> Mavzular shabloni
              </Button>
              <Button onClick={() => fileRef.current?.click()} disabled={importing}>
                <Upload className="h-4 w-4" /> {importing ? 'Yuklanmoqda...' : "Excel'dan yuklash"}
              </Button>
              <input ref={fileRef} type="file" accept=".xlsx" className="hidden" onChange={onImportFile} />
              <span className="text-xs text-slate-400">
                Mavzu va uy vazifani to'ldiradi — darsni "o'tilgan" qilmaydi.
              </span>
            </div>
          )}

          {dataLoading ? (
            <Loader label="Yuklanmoqda..." />
          ) : classSubjects.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-slate-400">
                {isGroupOwner
                  ? "Bu guruhda fanlar yo'q — avval dars jadvali yarating"
                  : "Bu sinfda fanlar yo'q — avval dars jadvali yarating"}
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
            <div className="space-y-6">
              {/* Jurnal jadvali — EduSchool tartibida (mijoz, 2026-09-23): №, F.I.SH, sana + soat,
                  qutisiz qiymatlar (baho / ✓ keldi / sabab nomi), o'ngda qotirilgan O'rtacha.
                  Ko'rinish (ranglar, shrift) — bizniki. Katak bosilsa tahrirlash oynasi ochiladi. */}
              <Card className="min-w-0 overflow-hidden p-0">
                <div className="overflow-x-auto">
                  <table className="w-max min-w-full border-separate border-spacing-0 text-sm">
                    <thead>
                      <tr className="text-slate-500">
                        <th className="sticky left-0 z-20 w-12 min-w-12 border-b border-slate-200 bg-slate-50 px-2 py-2 text-center text-xs font-medium">
                          №
                        </th>
                        <th className="sticky left-12 z-20 min-w-[16rem] border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase tracking-wide">
                          F.I.SH
                        </th>
                        {visibleColumns.map((c) => {
                          const sg = c.subGroup ?? 0
                          const done = conductedFor(c.date, c.period, sg)
                          return (
                            <th
                              key={`${c.date}-${c.period}-${sg}`}
                              className="min-w-[5.5rem] border-b border-l border-slate-100 bg-slate-50 px-2 py-2 text-center font-normal"
                            >
                              <div className="text-xs font-semibold tabular-nums text-slate-700">{formatDate(c.date)}</div>
                              <div className="text-[10px] uppercase tracking-wide text-slate-400">
                                {weekdayShort(c.date)} · {c.period}-soat
                              </div>
                              <div className="mt-1 flex items-center justify-center gap-1">
                                {sg > 0 && (
                                  <span
                                    className={cn(
                                      'rounded px-1 text-[10px] font-semibold',
                                      sg === 1 ? 'bg-sky-100 text-sky-700' : 'bg-violet-100 text-violet-700',
                                    )}
                                    title={`${sg}-guruh darsi`}
                                  >
                                    G{sg}
                                  </span>
                                )}
                                <button
                                  type="button"
                                  onClick={() => handleToggleConducted(c.date, c.period, sg)}
                                  title={done ? "Dars o'tildi — bosib bekor qilish" : "Dars o'tilmadi — bosib belgilash"}
                                  className={cn(
                                    'flex h-5 w-5 items-center justify-center rounded-full transition-colors',
                                    done ? 'bg-emerald-100 text-emerald-600' : 'bg-slate-200/70 text-slate-400 hover:text-slate-500',
                                  )}
                                >
                                  <Check className="h-3 w-3" />
                                </button>
                              </div>
                            </th>
                          )
                        })}
                        <th className="min-w-[4.5rem] border-b border-l border-slate-200 bg-slate-50 px-2 py-2 text-center text-xs font-medium uppercase tracking-wide">
                          Chorak
                        </th>
                        <th className="sticky right-0 z-20 min-w-[5rem] border-b border-l border-slate-200 bg-slate-50 px-2 py-2 text-center text-xs font-medium uppercase tracking-wide">
                          O'rtacha
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {classStudents.map((s, idx) => (
                        <JournalRow
                          key={s.id}
                          index={idx + 1}
                          student={s}
                          columns={visibleColumns}
                          entryFor={entryFor}
                          conductedFor={conductedFor}
                          reasonName={reasonName}
                          reasonIsLate={reasonIsLate}
                          avg={quarterAvg(s.id, s.subGroup ?? 0)}
                          quarterGrade={quarterGradeFor(s.id)}
                          onCell={(date, period) => setEditing({ student: s, date, period })}
                          onQuarter={() => setEditingQuarter(s)}
                        />
                      ))}
                      {classStudents.length === 0 && (
                        <tr>
                          <td colSpan={visibleColumns.length + 4} className="px-4 py-10 text-center text-slate-400">
                            {isGroupOwner ? "Bu guruhda o'quvchilar yo'q" : "Bu sinfda o'quvchilar yo'q"}
                          </td>
                        </tr>
                      )}
                      {formerStudents.length > 0 && (
                        <>
                          <tr>
                            <td
                              colSpan={2}
                              className="sticky left-0 z-10 border-t-2 border-slate-300 bg-slate-50 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-500"
                            >
                              Arxivdagi va boshqa sinfga o'tgan o'quvchilar
                            </td>
                            <td colSpan={visibleColumns.length + 2} className="border-t-2 border-slate-300 bg-slate-50" />
                          </tr>
                          {formerStudents.map((s, idx) => (
                            <JournalRow
                              key={`former-${s.id}`}
                              index={idx + 1}
                              student={s}
                              columns={visibleColumns}
                              entryFor={entryFor}
                              conductedFor={() => false}
                              reasonName={reasonName}
                              reasonIsLate={reasonIsLate}
                              avg={quarterAvg(s.id, s.subGroup ?? 0)}
                              quarterGrade={null}
                              note={s.isArchived ? 'Arxivda' : s.className ? `Hozir: ${s.className}` : 'Sinfsiz'}
                              muted
                            />
                          ))}
                        </>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>

              {/* Mavzu va uyga vazifa — mijoz, 2026-09-23: "bu bo'lim hozircha kerakmas bu yerda".
                  O'chirilmadi, yashirildi: SHOW_TOPICS_PANEL = true qilinsa qaytadi. Mavzular
                  o'qituvchi jurnalida va Excel importida ishlashda davom etadi. */}
              {SHOW_TOPICS_PANEL && (
              <Card>
                <div className="mb-3 flex items-center gap-2">
                  <NotebookText className="h-4 w-4 text-brand-600" />
                  <h2 className="font-semibold text-slate-800">Mavzu va uyga vazifa</h2>
                </div>
                <div className="grid max-h-[40rem] grid-cols-1 gap-3 overflow-y-auto pr-1 md:grid-cols-2 xl:grid-cols-3">
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
              )}
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

      <Modal
        open={!!importResult}
        onClose={() => setImportResult(null)}
        title="Mavzular import natijasi"
        footer={<Button onClick={() => setImportResult(null)}>Yopish</Button>}
      >
        {importResult && (
          <div className="space-y-3 text-sm">
            <div className="flex flex-wrap gap-4">
              <span className="font-semibold text-emerald-600">{importResult.imported} ta to'ldirildi</span>
              <span className="text-slate-400">{importResult.skipped} ta bo'sh (o'tkazib yuborildi)</span>
              {importResult.errors > 0 && (
                <span className="font-semibold text-red-600">{importResult.errors} ta xato</span>
              )}
            </div>
            <p className="text-xs text-slate-400">
              Eslatma: import faqat mavzu va uy vazifani to'ldirdi — darslar "o'tilgan" deb belgilanmadi.
            </p>
            {importResult.rowErrors.length > 0 && (
              <div className="max-h-60 overflow-y-auto rounded-lg border border-slate-100">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 text-slate-400">
                    <tr>
                      <th className="px-3 py-1.5">Qator</th>
                      <th className="px-3 py-1.5">Sabab</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {importResult.rowErrors.map((er) => (
                      <tr key={er.row}>
                        <td className="px-3 py-1.5 text-slate-500">{er.row}</td>
                        <td className="px-3 py-1.5 text-red-600">{er.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  )
}

/* ==========================================================================
   Jurnal qatori — asosiy ro'yxat va sobiq o'quvchilar uchun bitta ko'rinish
   ========================================================================== */

interface JournalRowProps {
  index: number
  student: Student
  columns: JournalColumn[]
  entryFor: (studentId: string, date: string, period: number) => JournalEntry | null
  conductedFor: (date: string, period: number, subGroup: number) => boolean
  reasonName: (reasonId: string) => string
  reasonIsLate: (reasonId: string) => boolean
  avg: number | null
  quarterGrade: number | null
  /** Berilmasa — qator faqat o'qish uchun (sobiq o'quvchi). */
  onCell?: (date: string, period: number) => void
  onQuarter?: () => void
  /** Ism ostidagi izoh (masalan "Arxivda"). */
  note?: string
  muted?: boolean
}

function JournalRow({
  index,
  student: s,
  columns,
  entryFor,
  conductedFor,
  reasonName,
  reasonIsLate,
  avg,
  quarterGrade,
  onCell,
  onQuarter,
  note,
  muted = false,
}: JournalRowProps) {
  const ssg = s.subGroup ?? 0
  const cellBase = 'border-b border-l border-slate-100 p-0 text-center'
  return (
    <tr className={cn('group', muted && 'text-slate-400')}>
      <td className="sticky left-0 z-10 w-12 border-b border-slate-100 bg-white px-2 py-2 text-center tabular-nums text-slate-400 group-hover:bg-slate-50">
        {index}
      </td>
      <td className="sticky left-12 z-10 border-b border-r border-slate-200 bg-white px-3 py-2 group-hover:bg-slate-50">
        <div className="flex items-center gap-1.5 whitespace-nowrap">
          <span className={cn('font-medium', muted ? 'text-slate-500' : 'text-slate-800')}>{s.fullName}</span>
          {ssg > 0 && (
            <span
              className={cn(
                'rounded px-1 text-[10px] font-semibold',
                ssg === 1 ? 'bg-sky-100 text-sky-700' : 'bg-violet-100 text-violet-700',
              )}
              title={`${ssg}-guruh`}
            >
              G{ssg}
            </span>
          )}
        </div>
        {note && <div className="text-[11px] text-slate-400">{note}</div>}
      </td>
      {columns.map((c) => {
        const sg = c.subGroup ?? 0
        const entry = entryFor(s.id, c.date, c.period)
        const late = entry?.reasonId ? reasonIsLate(entry.reasonId) : false
        // Katakda bitta qiymat: baho → sabab nomi → (dars o'tilgan bo'lsa) ✓ keldi.
        const content =
          entry?.grade != null ? (
            <span className={cn('text-base font-semibold tabular-nums', muted ? '' : gradeColor(entry.grade))}>
              {entry.grade}
            </span>
          ) : entry?.reasonId ? (
            <span
              className={cn(
                'block truncate px-1 text-[11px] font-medium',
                muted ? '' : late ? 'text-amber-600' : 'text-red-600',
              )}
              title={reasonName(entry.reasonId)}
            >
              {reasonName(entry.reasonId)}
            </span>
          ) : conductedFor(c.date, c.period, sg) ? (
            <CheckCircle2 className="mx-auto h-5 w-5 text-emerald-500" aria-label="Keldi" />
          ) : null
        const marks = (
          <>
            {entry?.grade != null && late && (
              <span className="absolute right-1.5 top-1.5 h-1.5 w-1.5 rounded-full bg-amber-400" title="Kech keldi" />
            )}
            {entry?.homework ? (
              <span
                title={entry.homework === 1 ? 'Uy vazifa: qildi' : 'Uy vazifa: qilmadi'}
                className={cn(
                  'absolute bottom-1 left-1.5 h-1.5 w-1.5 rounded-sm',
                  entry.homework === 1 ? 'bg-emerald-500' : 'bg-red-500',
                )}
              />
            ) : null}
            {entry?.behavior ? (
              <span
                title={entry.behavior === 1 ? 'Xulq: yaxshi' : 'Xulq: yomon'}
                className={cn(
                  'absolute bottom-1 right-1.5 h-1.5 w-1.5 rounded-full',
                  entry.behavior === 1 ? 'bg-emerald-500' : 'bg-red-500',
                )}
              />
            ) : null}
            {entry?.mastery != null && (
              <span className="absolute inset-x-0 top-0.5 text-[8px] font-semibold leading-none text-brand-600">
                {entry.mastery}%
              </span>
            )}
          </>
        )
        return (
          <td key={`${c.date}-${c.period}-${sg}`} className={cellBase}>
            {onCell ? (
              <button
                type="button"
                onClick={() => onCell(c.date, c.period)}
                className="relative flex h-11 w-full items-center justify-center transition-colors hover:bg-brand-50/60"
              >
                {content}
                {marks}
              </button>
            ) : (
              <div className="relative flex h-11 w-full items-center justify-center">{content}</div>
            )}
          </td>
        )
      })}
      <td className={cn(cellBase, 'border-l-slate-200')}>
        {onQuarter ? (
          <button
            type="button"
            onClick={onQuarter}
            title="Chorak bahosini belgilash"
            className="flex h-11 w-full items-center justify-center transition-colors hover:bg-brand-50/60"
          >
            {quarterGrade != null ? (
              <span className={cn('text-base font-bold', gradeColor(quarterGrade))}>{quarterGrade}</span>
            ) : (
              <span className="text-slate-300">—</span>
            )}
          </button>
        ) : (
          <span className="text-slate-300">—</span>
        )}
      </td>
      <td className="sticky right-0 z-10 border-b border-l border-slate-200 bg-white px-2 text-center group-hover:bg-slate-50">
        {avg != null ? (
          <span className={cn('text-sm font-semibold tabular-nums', muted ? '' : avgColor(avg))}>{avg.toFixed(1)}</span>
        ) : (
          <span className="text-slate-300">—</span>
        )}
      </td>
    </tr>
  )
}
