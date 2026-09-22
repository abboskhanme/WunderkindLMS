/**
 * NATIJALAR — the cross-exam result register
 * (`docs/modules/admission-and-testing.md` §3.2 screen 8, §6.3; unit C3).
 *
 * One row per participant of one exam, paper and online alike — both write
 * the same `exam_participants` summary and `exam_section_scores` (§2.1), so
 * this is one implementation for both. Paged and filtered on the server;
 * the default filter is `status=finished` (§3.2: EduSchool's menu prefetches
 * completed results).
 *
 * READ-ONLY. Screen 8 shows data and the export button (§3.7). Scores are
 * typed on the entry grid (screen 9), reached from the exam register. The
 * drawer shows one result in full; its two online-attempt actions follow
 * their own keys (§3.7 row "Natijani qayta hisoblash / attempt reset").
 *
 * THE EXPORT IS SERVER-SIDE. `.xlsx` from `ExcelExport` covering the whole
 * filter, not the 50 rows on screen.
 *
 * DEEP LINK. `?examId=` preselects one exam — the exam register links online
 * exams here, since they have no entry grid.
 */
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  Eye,
  FileSpreadsheet,
  Inbox,
  Loader2,
  RefreshCw,
  RotateCcw,
  Search,
} from 'lucide-react'
import {
  PAGE_LIMIT_MAX,
  examsErrorMessage,
  exportResults,
  listExams,
  listResults,
  type ExamRow,
  type ParticipantStatus,
  type ResultListQuery,
  type ResultRow,
} from '@/api/services/exams'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import type { SchoolClass, Subject } from '@/types'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { ResultReviewDrawer } from './ResultReviewDrawer'
import { ScoreBar } from './ScoreBar'
import {
  DASH,
  ONLINE_EXAMS_ENABLED,
  PAGE_SIZE,
  SEARCH_DEBOUNCE_MS,
  control,
  formatDay,
  formatPercent,
  formatPoints,
  formatScore,
  hasPerm,
  participantStatusLabel,
  participantStatusLabels,
  participantStatusTone,
} from './examLabels'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

interface FilterState {
  examId: string
  classId: string
  subjectId: string
  status: '' | ParticipantStatus
}

/** §3.2: the register opens on finished results. */
const DEFAULT_STATUS: ParticipantStatus = 'finished'

const STATUSES: ParticipantStatus[] = ['finished', 'absent', 'assigned', 'in_progress', 'cancelled']

/** A dropdown fed by a lookup that may fail on its own without taking the page down. */
interface Lookup<T> {
  items: T[]
  failed: boolean
}

export function ResultsPage() {
  const { user } = useAuth()
  const canForceFinish = hasPerm(user?.permissions, 'exams')
  const canReset = canForceFinish && hasPerm(user?.permissions, 'admission')

  const [searchParams] = useSearchParams()
  const linkedExamId = searchParams.get('examId') ?? ''

  const initial: FilterState = {
    examId: linkedExamId,
    classId: '',
    subjectId: '',
    status: DEFAULT_STATUS,
  }

  const [rows, setRows] = useState<ResultRow[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [filters, setFilters] = useState<FilterState>(initial)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  const [exams, setExams] = useState<Lookup<ExamRow>>({ items: [], failed: false })
  const [classes, setClasses] = useState<Lookup<SchoolClass>>({ items: [], failed: false })
  const [subjects, setSubjects] = useState<Lookup<Subject>>({ items: [], failed: false })

  const [exporting, setExporting] = useState(false)
  const [opened, setOpened] = useState<ResultRow | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  // The three filter dropdowns. Each fails on its own — a missing class list
  // must not hide the results.
  useEffect(() => {
    let cancelled = false
    listExams({ page: 1, limit: PAGE_LIMIT_MAX })
      .then((data) => !cancelled && setExams({ items: data.items, failed: false }))
      .catch(() => !cancelled && setExams({ items: [], failed: true }))
    getClasses()
      .then((data) => !cancelled && setClasses({ items: data, failed: false }))
      .catch(() => !cancelled && setClasses({ items: [], failed: true }))
    getSubjects()
      .then((data) => !cancelled && setSubjects({ items: data, failed: false }))
      .catch(() => !cancelled && setSubjects({ items: [], failed: true }))
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search)
      setPage(1)
    }, SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const query = useMemo<ResultListQuery>(
    () => ({
      examId: filters.examId || undefined,
      classId: filters.classId || undefined,
      subjectId: filters.subjectId || undefined,
      status: filters.status || undefined,
      search: debouncedSearch.trim() || undefined,
    }),
    [filters, debouncedSearch],
  )

  /** Narrowed beyond the default? Decides which empty state is honest. */
  const filtersActive =
    Boolean(query.examId) ||
    Boolean(query.classId) ||
    Boolean(query.subjectId) ||
    Boolean(query.search) ||
    query.status !== DEFAULT_STATUS

  useEffect(() => {
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin "yuklanmoqda" holatini belgilaymiz (loyihadagi mavjud naqsh)
    setLoading(true)
    listResults({ ...query, page, limit: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return
        setRows(result.items)
        setTotal(result.total)
        setError(null)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setRows([])
        setTotal(0)
        setError(examsErrorMessage(err, 'results.load'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [query, page, reloadToken])

  const set = (patch: Partial<FilterState>) => {
    setPage(1)
    setFilters((prev) => ({ ...prev, ...patch }))
  }

  const reset = () => {
    setPage(1)
    setSearch('')
    setDebouncedSearch('')
    setFilters({ examId: '', classId: '', subjectId: '', status: DEFAULT_STATUS })
  }

  const handleExport = async () => {
    setExporting(true)
    try {
      await exportResults(query)
    } catch (err) {
      setNotice({ message: examsErrorMessage(err, 'results.export'), tone: 'error' })
    } finally {
      setExporting(false)
    }
  }

  const classOptions = classes.items
    .filter((c) => !c.isArchived)
    .sort((a, b) => a.grade - b.grade || a.name.localeCompare(b.name, 'uz'))
  const subjectOptions = [...subjects.items].sort((a, b) => a.name.localeCompare(b.name, 'uz'))
  /** A deep-linked exam outside the first 200 still needs an option to show as selected. */
  const examMissing = Boolean(filters.examId) && !exams.items.some((e) => e.id === filters.examId)

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const first = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const last = Math.min(page * PAGE_SIZE, total)

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Natijalar</h1>
          <p className="text-sm text-slate-400">
            Barcha imtihonlar natijalari — o'quvchi, sinf, fan va imtihon bo'yicha.
          </p>
        </div>
        <Button
          variant="secondary"
          onClick={() => void handleExport()}
          disabled={exporting || loading || total === 0}
          title="Butun filtr bo'yicha Excel fayl (ko'rinib turgan sahifa emas)"
        >
          {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileSpreadsheet className="h-4 w-4" />}
          {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </div>

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

          <select
            value={filters.examId}
            onChange={(e) => set({ examId: e.target.value })}
            disabled={exams.failed && !filters.examId}
            aria-label="Imtihon"
            className={cn(control, 'max-w-[16rem]', exams.failed && 'text-slate-400')}
          >
            <option value="">{exams.failed ? 'Imtihonlar yuklanmadi' : 'Barcha imtihonlar'}</option>
            {examMissing && <option value={filters.examId}>Tanlangan imtihon</option>}
            {exams.items.map((e) => (
              <option key={e.id} value={e.id}>
                {e.examDate ? `${e.title} · ${formatDay(e.examDate)}` : e.title}
              </option>
            ))}
          </select>

          <select
            value={filters.classId}
            onChange={(e) => set({ classId: e.target.value })}
            disabled={classes.failed}
            aria-label="Sinf"
            className={cn(control, classes.failed && 'text-slate-400')}
          >
            <option value="">{classes.failed ? 'Sinflar yuklanmadi' : 'Barcha sinflar'}</option>
            {classOptions.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>

          <select
            value={filters.subjectId}
            onChange={(e) => set({ subjectId: e.target.value })}
            disabled={subjects.failed}
            aria-label="Fan"
            className={cn(control, subjects.failed && 'text-slate-400')}
          >
            <option value="">{subjects.failed ? 'Fanlar yuklanmadi' : 'Barcha fanlar'}</option>
            {subjectOptions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>

          <select
            value={filters.status}
            onChange={(e) => set({ status: e.target.value as '' | ParticipantStatus })}
            aria-label="Holati"
            className={control}
          >
            <option value="">Barcha holatlar</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {participantStatusLabels[s]}
              </option>
            ))}
          </select>

          <button
            type="button"
            onClick={reset}
            title="Filtrlarni tozalash"
            className="ml-auto inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100"
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Tozalash
          </button>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : error ? (
          <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <div>
              <p className="font-medium text-slate-800">Natijalarni yuklab bo'lmadi</p>
              <p className="mt-1 max-w-md text-sm text-slate-500">{error}</p>
            </div>
            <Button variant="secondary" onClick={() => setReloadToken((t) => t + 1)}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        ) : rows.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
            <Inbox className="h-8 w-8 text-slate-300" />
            {filtersActive ? (
              <>
                <p className="font-medium text-slate-600">Bu shartlar bo'yicha natija topilmadi</p>
                <p className="max-w-md text-sm text-slate-400">
                  Boshqa imtihon yoki holatni tanlang, yoki filtrlarni tozalang.
                </p>
                <button
                  type="button"
                  onClick={reset}
                  className="mt-1 inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  <RotateCcw className="h-3.5 w-3.5" /> Filtrlarni tozalash
                </button>
              </>
            ) : (
              <>
                <p className="font-medium text-slate-600">Hali baholangan natija yo'q</p>
                <p className="max-w-md text-sm text-slate-400">
                  Blok test natijalari "Imtihonlar" ro'yxatidagi "Natija kiritish" orqali kiritiladi;
                  qabul imtihoni natijalari nomzod testni yakunlashi bilan shu yerda paydo bo'ladi.
                </p>
              </>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[64rem] text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Imtihon</th>
                  <th className="px-4 py-3">Fanlar bo'yicha</th>
                  <th className="px-4 py-3 text-right">Ball</th>
                  <th className="px-4 py-3">Foiz</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr
                    key={row.participantId}
                    onClick={ONLINE_EXAMS_ENABLED ? () => setOpened(row) : undefined}
                    className={cn(ONLINE_EXAMS_ENABLED && 'cursor-pointer hover:bg-slate-50/60')}
                  >
                    <td className="px-4 py-3">
                      <span className="block max-w-[14rem] truncate font-medium text-slate-800" title={row.fullName}>
                        {row.fullName}
                      </span>
                      {row.participantKind === 'lead' && (
                        <span className="text-xs text-brand-600">Nomzod</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">{row.className ?? DASH}</td>
                    <td className="px-4 py-3">
                      <span className="block max-w-[14rem] truncate text-slate-700" title={row.examTitle}>
                        {row.examTitle}
                      </span>
                      <span className="text-xs text-slate-400">{formatDay(row.examDate)}</span>
                    </td>
                    <td className="px-4 py-3">
                      {row.scores && row.scores.length > 0 ? (
                        <div className="flex max-w-[22rem] flex-wrap gap-1">
                          {row.scores.map((s) => (
                            <span
                              key={s.sectionId}
                              title={s.name}
                              className={cn(
                                'inline-flex items-center gap-1 whitespace-nowrap rounded-md px-1.5 py-0.5 text-xs',
                                filters.subjectId === s.subjectId
                                  ? 'bg-brand-50 text-brand-700'
                                  : 'bg-slate-100 text-slate-600',
                              )}
                            >
                              <span className="max-w-[7rem] truncate">{s.name}</span>
                              <b className="font-semibold">
                                {s.points === null ? DASH : formatPoints(s.points)}
                              </b>
                              <span className="text-slate-400">/{formatPoints(s.maxPoints)}</span>
                            </span>
                          ))}
                        </div>
                      ) : (
                        <span className="text-slate-300">{DASH}</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-700">
                      {formatScore(row.totalPoints, row.maxPoints)}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex min-w-[7rem] items-center gap-2">
                        <span className="w-12 whitespace-nowrap text-right text-slate-600">
                          {formatPercent(row.percent)}
                        </span>
                        <ScoreBar value={row.percent} max={100} className="w-16" label="Foiz" />
                      </div>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3">
                      <span
                        className={cn(
                          'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                          participantStatusTone(row.status),
                        )}
                      >
                        {participantStatusLabel(row.status)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {ONLINE_EXAMS_ENABLED && (
                      <button
                        type="button"
                        title="Batafsil"
                        aria-label="Batafsil"
                        onClick={(e) => {
                          e.stopPropagation()
                          setOpened(row)
                        }}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                      >
                        <Eye className="h-4 w-4" />
                      </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
          <p className="text-xs text-slate-400">
            {total === 0 ? "Yozuv yo'q" : `${first}–${last} / ${total} ta`}
          </p>
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={page <= 1 || loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              aria-label="Oldingi sahifa"
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="min-w-[70px] text-center text-xs text-slate-500">
              {page} / {lastPage}
            </span>
            <button
              type="button"
              disabled={page >= lastPage || loading}
              onClick={() => setPage((p) => p + 1)}
              aria-label="Keyingi sahifa"
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </Card>

      {opened && (
        <ResultReviewDrawer
          key={opened.participantId}
          row={opened}
          canForceFinish={canForceFinish}
          canReset={canReset}
          onClose={() => setOpened(null)}
          onChanged={(message) => {
            setNotice({ message, tone: 'success' })
            setReloadToken((t) => t + 1)
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
