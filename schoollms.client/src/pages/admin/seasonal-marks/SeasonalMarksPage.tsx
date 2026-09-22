/**
 * MAVSUMIY BAHOLASH — `/admin/seasonal-marks`, screen 10 of
 * `docs/modules/admission-and-testing.md` §3.3.
 *
 * Every stored seasonal mark, paged and filtered on the server
 * (`GET /api/admin/seasonal-marks`, §6.6) — a year of marks for the whole
 * school is tens of thousands of rows. Ball and izoh are edited in the cell
 * (`PUT /{id}`); a row can be deleted and its history opened. The Excel file
 * is the server's (`/export`) and covers the whole filter, not this page.
 *
 * PERMISSIONS (§3.7). The route is wrapped in `RequirePerm perm="seasonalMarks"`.
 * Inside, the editing controls, delete and "Baholash" follow the same key;
 * "Tarix" follows the audit endpoint's own gate (admin, superadmin).
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  ClipboardList,
  FileSpreadsheet,
  History,
  PencilLine,
  RotateCcw,
  Search,
  Trash2,
  X,
} from 'lucide-react'
import {
  deleteSeasonalMark,
  downloadSeasonalMarks,
  getSeasonalScope,
  listSeasonalMarks,
  seasonalError,
  seasonalErrorMessage,
  updateSeasonalMark,
  type PeriodKind,
  type SeasonalListFilters,
  type SeasonalMarkRowDto,
  type SeasonalMarkUpdate,
} from '@/api/services/seasonalMarks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { MonthPicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { InlineCommentCell, InlineScoreCell } from './InlineCells'
import { MarkHistoryModal } from './MarkHistoryModal'
import { EmptyState, ErrorState, InlineError, Pager } from './StateViews'
import { useSeasonalAccess } from './access'
import {
  PERIOD_KINDS,
  QUARTERS,
  SCORE_COLUMN_LABEL,
  controlClass,
  periodKindLabels,
  readPeriodParams,
  yearOptions,
} from './periods'
import { classOptions, subjectOptions } from './scopeOptions'
import { useRemote } from './useRemote'

/** §6: server default 50, cap 200. */
const PAGE_SIZE = 50
const SEARCH_DEBOUNCE_MS = 350

interface FilterState {
  kind: '' | PeriodKind
  /** '' = any year. */
  year: number | ''
  /** Only with `monthly`. */
  month: number | ''
  /** Only with `quarterly`. */
  quarter: number | ''
  classId: string
  subjectId: string
  /** Set only by a link from another screen (e.g. the coverage drill-down). */
  studentId: string
}

const EMPTY: FilterState = {
  kind: '',
  year: '',
  month: '',
  quarter: '',
  classId: '',
  subjectId: '',
  studentId: '',
}

function filtersFromUrl(params: URLSearchParams): FilterState {
  const p = readPeriodParams(params)
  return {
    kind: p.kind ?? '',
    year: p.year ?? '',
    month: p.kind === 'monthly' ? (p.month ?? '') : '',
    quarter: p.kind === 'quarterly' ? (p.quarter ?? '') : '',
    classId: params.get('classId') ?? '',
    subjectId: params.get('subjectId') ?? '',
    studentId: params.get('studentId') ?? '',
  }
}

interface ToastState {
  message: string
  tone: 'success' | 'error'
}

export function SeasonalMarksPage() {
  const [params] = useSearchParams()
  const { canWrite, canSeeHistory } = useSeasonalAccess()

  const [filters, setFilters] = useState<FilterState>(() => filtersFromUrl(params))
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [toast, setToast] = useState<ToastState | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const [historyOf, setHistoryOf] = useState<SeasonalMarkRowDto | null>(null)
  const [deleting, setDeleting] = useState<SeasonalMarkRowDto | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search)
      setPage(1)
    }, SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  // Filter options. A failure here does not block the list — the two selects
  // just say so.
  const scope = useRemote('scope', () => getSeasonalScope())
  const classes = useMemo(() => (scope.data ? classOptions(scope.data) : []), [scope.data])
  const subjects = useMemo(
    () => (scope.data ? subjectOptions(scope.data, filters.classId ? [filters.classId] : null) : []),
    [scope.data, filters.classId],
  )

  const query = useMemo<SeasonalListFilters>(
    () => ({
      search: debouncedSearch.trim() || undefined,
      periodKind: filters.kind || undefined,
      year: filters.year === '' ? undefined : filters.year,
      month: filters.kind === 'monthly' && filters.month !== '' ? filters.month : undefined,
      quarter: filters.kind === 'quarterly' && filters.quarter !== '' ? filters.quarter : undefined,
      classId: filters.classId || undefined,
      subjectId: filters.subjectId || undefined,
      studentId: filters.studentId || undefined,
    }),
    [filters, debouncedSearch],
  )

  const filtersActive = Object.values(query).some((v) => v !== undefined)

  const list = useRemote(JSON.stringify({ ...query, page }), () =>
    listSeasonalMarks({ ...query, page, limit: PAGE_SIZE }),
  )
  const rows = list.data?.items ?? []
  const total = list.data?.total ?? 0

  const set = (patch: Partial<FilterState>) => {
    setPage(1)
    setFilters((prev) => ({ ...prev, ...patch }))
  }

  const reset = () => {
    setPage(1)
    setSearch('')
    setDebouncedSearch('')
    setFilters(EMPTY)
  }

  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await downloadSeasonalMarks(query)
    } catch (err: unknown) {
      setExportError(seasonalErrorMessage(err))
    } finally {
      setExporting(false)
    }
  }

  /** One inline save. Both fields travel, so the other one is never cleared by accident. */
  const saveRow = async (row: SeasonalMarkRowDto, next: SeasonalMarkUpdate): Promise<boolean> => {
    if (next.score === null && next.comment === null) {
      setToast({
        tone: 'error',
        message:
          "Ball ham, izoh ham bo'sh bo'lsa baho qolmaydi. Bahoni olib tashlash uchun o'chirish tugmasidan foydalaning.",
      })
      return false
    }
    try {
      const updated = await updateSeasonalMark(row.id, next)
      list.mutate((d) => ({ ...d, items: d.items.map((i) => (i.id === row.id ? updated : i)) }))
      setToast({ tone: 'success', message: 'Saqlandi' })
      return true
    } catch (err: unknown) {
      const info = seasonalError(err)
      setToast({ tone: 'error', message: info.message })
      if (info.kind === 'not_found' || info.kind === 'conflict') list.reload()
      return false
    }
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    setDeleteError(null)
    try {
      await deleteSeasonalMark(deleting.id)
      setDeleting(null)
      setToast({ tone: 'success', message: "Baho o'chirildi" })
      // The last row of a later page: step back instead of showing an empty page.
      if (rows.length === 1 && page > 1) setPage(page - 1)
      else list.reload()
    } catch (err: unknown) {
      const info = seasonalError(err)
      if (info.kind === 'not_found') {
        // Already gone — the goal is reached; refresh what is on screen.
        setDeleting(null)
        list.reload()
      } else {
        setDeleteError(info.message)
      }
    } finally {
      setDeleteBusy(false)
    }
  }

  /** The list's own filters travel to the entry screen as a preselection. */
  const entryLink = useMemo(() => {
    const out = new URLSearchParams()
    if (filters.kind) out.set('periodKind', filters.kind)
    if (filters.year !== '') out.set('year', String(filters.year))
    if (filters.kind === 'monthly' && filters.month !== '') out.set('month', String(filters.month))
    if (filters.kind === 'quarterly' && filters.quarter !== '') out.set('quarter', String(filters.quarter))
    if (filters.classId) out.set('classId', filters.classId)
    if (filters.subjectId) out.set('subjectId', filters.subjectId)
    const qs = out.toString()
    return `/admin/seasonal-marks/entry${qs ? `?${qs}` : ''}`
  }, [filters])

  const monthValue =
    filters.year !== '' && filters.month !== ''
      ? `${filters.year}-${String(filters.month).padStart(2, '0')}`
      : ''
  const studentName = filters.studentId
    ? rows.find((r) => r.student.id === filters.studentId)?.student.fullName
    : undefined
  const showActions = canWrite || canSeeHistory

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Mavsumiy baholash</h1>
          <p className="text-sm text-slate-400">
            Oylik, choraklik va yillik baholar. Ball 0 dan 100 gacha — jurnaldagi 1–5 baho emas.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="secondary"
            onClick={handleExport}
            disabled={exporting || list.loading || total === 0}
            title="Butun filtr bo'yicha Excel fayl (ko'rinib turgan sahifa emas)"
          >
            <FileSpreadsheet className="h-4 w-4" />
            {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
          </Button>
          {canWrite && (
            <Link
              to={entryLink}
              className="inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
            >
              <PencilLine className="h-4 w-4" /> Baholash
            </Link>
          )}
        </div>
      </div>

      {exportError && <InlineError message={exportError} />}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="O'quvchi ismi..."
              aria-label="O'quvchi bo'yicha qidiruv"
              className={cn(controlClass, 'w-full pl-9 font-normal')}
            />
          </div>

          <select
            value={filters.kind}
            onChange={(e) =>
              set({ kind: e.target.value as '' | PeriodKind, month: '', quarter: '' })
            }
            aria-label="Tur"
            className={controlClass}
          >
            <option value="">Barcha turlar</option>
            {PERIOD_KINDS.map((k) => (
              <option key={k} value={k}>
                {periodKindLabels[k]}
              </option>
            ))}
          </select>

          <select
            value={filters.year}
            onChange={(e) => {
              const v = e.target.value
              // A month means nothing without its year.
              set(v === '' ? { year: '', month: '' } : { year: Number(v) })
            }}
            aria-label="Yil"
            className={controlClass}
          >
            <option value="">Barcha yillar</option>
            {yearOptions(filters.year === '' ? undefined : filters.year).map((y) => (
              <option key={y} value={y}>
                {y}-yil
              </option>
            ))}
          </select>

          {filters.kind === 'monthly' && (
            <MonthPicker
              value={monthValue}
              onChange={(v) => {
                if (!v) {
                  set({ month: '' })
                  return
                }
                const [y, m] = v.split('-').map(Number)
                if (y && m) set({ year: y, month: m })
              }}
              clearable
              placeholder="Barcha oylar"
              ariaLabel="Oy"
              title="Oy"
            />
          )}

          {filters.kind === 'quarterly' && (
            <select
              value={filters.quarter}
              onChange={(e) => set({ quarter: e.target.value === '' ? '' : Number(e.target.value) })}
              aria-label="Chorak"
              className={controlClass}
            >
              <option value="">Barcha choraklar</option>
              {QUARTERS.map((q) => (
                <option key={q} value={q}>
                  {q}-chorak
                </option>
              ))}
            </select>
          )}

          <select
            value={filters.classId}
            onChange={(e) => {
              const classId = e.target.value
              const stillTaught =
                !filters.subjectId ||
                !scope.data ||
                subjectOptions(scope.data, classId ? [classId] : null).some(
                  (s) => s.id === filters.subjectId,
                )
              set({ classId, subjectId: stillTaught ? filters.subjectId : '' })
            }}
            disabled={Boolean(scope.error) || scope.loading}
            aria-label="Sinf"
            className={controlClass}
          >
            <option value="">{scope.error ? "Sinflar yuklanmadi" : 'Barcha sinflar'}</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>

          <select
            value={filters.subjectId}
            onChange={(e) => set({ subjectId: e.target.value })}
            disabled={Boolean(scope.error) || scope.loading}
            aria-label="Fan"
            className={controlClass}
          >
            <option value="">{scope.error ? 'Fanlar yuklanmadi' : 'Barcha fanlar'}</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>

          {filters.studentId && (
            <span className="inline-flex items-center gap-1 rounded-full bg-brand-50 py-1 pl-3 pr-1 text-xs font-medium text-brand-700">
              O'quvchi: {studentName ?? 'tanlangan'}
              <button
                type="button"
                onClick={() => set({ studentId: '' })}
                aria-label="O'quvchi filtrini olib tashlash"
                className="rounded-full p-0.5 transition-colors hover:bg-brand-100"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </span>
          )}

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

        {list.loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : list.error ? (
          <ErrorState
            title="Baholarni yuklab bo'lmadi"
            message={seasonalErrorMessage(list.error)}
            onRetry={list.reload}
          />
        ) : rows.length === 0 ? (
          filtersActive ? (
            <EmptyState
              icon={ClipboardList}
              title="Bu shartlar bo'yicha baho topilmadi"
              hint="Davrni kengaytiring yoki boshqa sinf va fanni tanlang."
              action={
                <button
                  type="button"
                  onClick={reset}
                  className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  <RotateCcw className="h-3.5 w-3.5" /> Filtrlarni tozalash
                </button>
              }
            />
          ) : (
            <EmptyState
              icon={ClipboardList}
              title="Hali birorta mavsumiy baho kiritilmagan"
              hint="Baholar sinf va fan bo'yicha, butun sinfga birdaniga kiritiladi."
              action={
                canWrite ? (
                  <Link
                    to="/admin/seasonal-marks/entry"
                    className="text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                  >
                    Baholashni boshlash
                  </Link>
                ) : undefined
              }
            />
          )
        ) : (
          <div className={cn('overflow-x-auto', list.refreshing && 'opacity-60')}>
            <table className="w-full min-w-[64rem] text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Tur</th>
                  <th className="px-4 py-3">Davr</th>
                  <th className="px-4 py-3">Fan</th>
                  <th className="px-4 py-3 normal-case">{SCORE_COLUMN_LABEL}</th>
                  <th className="px-4 py-3">Izoh</th>
                  {showActions && <th className="px-4 py-3" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => {
                  const who = `${row.student.fullName}, ${row.subject.name}`
                  return (
                    <tr key={row.id} className="align-top hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-800">
                        <span className="block max-w-[14rem] truncate" title={row.student.fullName}>
                          {row.student.fullName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.class.name}</td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                        {periodKindLabels[row.periodKind]}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.periodLabel}</td>
                      <td className="px-4 py-3 text-slate-600">
                        <span className="block max-w-[12rem] truncate" title={row.subject.name}>
                          {row.subject.name}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5">
                        <InlineScoreCell
                          value={row.score}
                          editable={canWrite}
                          label={who}
                          onCommit={(score) => saveRow(row, { score, comment: row.comment })}
                          onInvalid={(message) => setToast({ tone: 'error', message })}
                        />
                      </td>
                      <td className="px-3 py-2.5">
                        <InlineCommentCell
                          value={row.comment}
                          editable={canWrite}
                          label={who}
                          onCommit={(comment) => saveRow(row, { score: row.score, comment })}
                          onInvalid={(message) => setToast({ tone: 'error', message })}
                        />
                      </td>
                      {showActions && (
                        <td className="whitespace-nowrap px-4 py-2.5 text-right">
                          {canSeeHistory && (
                            <button
                              type="button"
                              onClick={() => setHistoryOf(row)}
                              title="Tarix"
                              aria-label={`${who}: tarix`}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                            >
                              <History className="h-4 w-4" />
                            </button>
                          )}
                          {canWrite && (
                            <button
                              type="button"
                              onClick={() => {
                                setDeleteError(null)
                                setDeleting(row)
                              }}
                              title="O'chirish"
                              aria-label={`${who}: o'chirish`}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                        </td>
                      )}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}

        <Pager page={page} limit={PAGE_SIZE} total={total} disabled={list.loading} onPage={setPage} />
      </Card>

      <MarkHistoryModal mark={historyOf} onClose={() => setHistoryOf(null)} />

      <Modal
        open={deleting !== null}
        onClose={() => !deleteBusy && setDeleting(null)}
        size="sm"
        title="Bahoni o'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)} disabled={deleteBusy}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmDelete} disabled={deleteBusy}>
              <Trash2 className="h-4 w-4" />
              {deleteBusy ? "O'chirilmoqda..." : "O'chirish"}
            </Button>
          </>
        }
      >
        {deleting && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{deleting.student.fullName}</span> —{' '}
              {deleting.subject.name}, {deleting.periodLabel}. Bu baho o'chiriladi; o'chirilgani
              tarixda qoladi.
            </p>
            {deleteError && <InlineError message={deleteError} />}
          </div>
        )}
      </Modal>

      <Toast message={toast?.message ?? null} tone={toast?.tone} onClose={closeToast} />
    </div>
  )
}
