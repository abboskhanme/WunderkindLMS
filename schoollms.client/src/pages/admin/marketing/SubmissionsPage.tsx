/**
 * TOPSHIRILGAN ARIZALAR — the submissions register
 * (`docs/modules/sales-marketing.md` §2.7, §5.3, §6.2 — task SM-9).
 *
 * WHAT THIS SCREEN IS FOR. EduSchool answers "which leads came from a form?"
 * with a filter on their kanban board. Ours is design-frozen (`CLAUDE.md`), so
 * this page is the answer instead — and a better one: it also shows the
 * submissions that were duplicates and the values the parent typed BEFORE
 * anyone edited the lead (§2.7).
 *
 * SERVER-SIDE EVERYTHING. Filtering, paging, sorting and the Excel file are all
 * the server's work (§5.3): this list grows without limit, unlike the board.
 * The browser never assembles a spreadsheet here — the export is an `.xlsx`
 * blob from `ExcelExport.cs` covering the WHOLE filter, not the 50 rows on
 * screen.
 *
 * THE BOARD STAYS SHUT. The `Lid` column links to `/admin/leads` and nothing in
 * this folder imports from `pages/admin/leads/*`.
 *
 * PERMISSION. The route carries `RequirePerm perm="marketing"` (SM-12) and the
 * endpoints carry `[AdminPerm("marketing")]`. Inside the page, the link to the
 * Lidlar board is rendered only for a user who also holds `leads` — the board
 * would refuse them, and a control that leads to a locked door is worse than no
 * control.
 */
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  ExternalLink,
  Eye,
  FileSpreadsheet,
  Inbox,
  RefreshCw,
  RotateCcw,
  Search,
} from 'lucide-react'
import {
  downloadSubmissions,
  listSubmissions,
  listSurveyOptions,
  submissionsErrorMessage,
  type Submission,
  type SubmissionFilters,
  type SubmissionStatus,
  type SurveyOption,
} from '@/api/services/surveySubmissions'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'
import { SubmissionDetailDrawer } from './SubmissionDetailDrawer'
import { DASH, formatDateTime, gradeLabel, statusLabel, statusTone } from './submissionLabels'
import { MarketingTabs } from './MarketingTabs'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** §5.3: the server default is 50 and the cap is 200. */
const PAGE_SIZE = 50

/** A keystroke must not become a request — the register is a server-side query. */
const SEARCH_DEBOUNCE_MS = 350

interface FilterState {
  surveyId: string
  status: '' | SubmissionStatus
  /** "YYYY-MM-DD", straight from the shared `DatePicker`. */
  from: string
  to: string
}

const INITIAL: FilterState = { surveyId: '', status: '', from: '', to: '' }

export function SubmissionsPage() {
  const { user } = useAuth()
  // Admin and superadmin carry no `permissions` list at all — they see everything.
  const canOpenLeads = !user?.permissions || user.permissions.includes('leads')

  const [rows, setRows] = useState<Submission[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [filters, setFilters] = useState<FilterState>(INITIAL)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  const [surveys, setSurveys] = useState<SurveyOption[]>([])
  const [surveysFailed, setSurveysFailed] = useState(false)

  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const [opened, setOpened] = useState<Submission | null>(null)

  // The survey dropdown. Closed surveys are included: their submissions stay in
  // the register forever, so filtering by them has to remain possible.
  useEffect(() => {
    let cancelled = false
    listSurveyOptions()
      .then((data) => {
        if (!cancelled) setSurveys(data)
      })
      .catch(() => {
        if (!cancelled) setSurveysFailed(true)
      })
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

  const query = useMemo<SubmissionFilters>(
    () => ({
      surveyId: filters.surveyId || undefined,
      status: filters.status || undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
      q: debouncedSearch.trim() || undefined,
    }),
    [filters, debouncedSearch],
  )

  /** Is anything narrowing the list right now? Decides which empty state is honest. */
  const filtersActive =
    Boolean(query.surveyId) ||
    Boolean(query.status) ||
    Boolean(query.from) ||
    Boolean(query.to) ||
    Boolean(query.q)

  useEffect(() => {
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin "yuklanmoqda" holatini belgilaymiz (loyihadagi mavjud naqsh)
    setLoading(true)
    listSubmissions({ ...query, page, pageSize: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return
        setRows(result.rows)
        setTotal(result.total)
        setError(null)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setRows([])
        setTotal(0)
        setError(submissionsErrorMessage(err, "Arizalarni yuklab bo'lmadi"))
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
    setFilters(INITIAL)
  }

  /** The whole filter as .xlsx — built by the server, never in the browser (§5.3). */
  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await downloadSubmissions(query)
    } catch (err: unknown) {
      setExportError(submissionsErrorMessage(err, "Eksport qilib bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const first = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const last = Math.min(page * PAGE_SIZE, total)

  return (
    <div className="space-y-6">
      <MarketingTabs />
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Topshirilgan arizalar</h1>
          <p className="text-sm text-slate-400">
            Ommaviy ariza formasi orqali kelgan murojaatlar — ota-ona yozgan holida.
          </p>
        </div>
        <Button
          variant="secondary"
          onClick={handleExport}
          disabled={exporting || loading || total === 0}
          title="Butun filtr bo'yicha Excel fayl (ko'rinib turgan sahifa emas)"
        >
          <FileSpreadsheet className="h-4 w-4" />
          {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </div>

      {exportError && (
        <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {exportError}
        </p>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ota-ona ismi yoki telefon..."
              aria-label="Qidiruv"
              className={cn(control, 'w-full pl-9')}
            />
          </div>

          <select
            value={filters.surveyId}
            onChange={(e) => set({ surveyId: e.target.value })}
            disabled={surveysFailed}
            aria-label="Ariza formasi"
            className={cn(control, surveysFailed && 'text-slate-400')}
          >
            <option value="">
              {surveysFailed ? "Arizalar ro'yxati yuklanmadi" : 'Barcha arizalar'}
            </option>
            {surveys.map((s) => (
              <option key={s.id} value={s.id}>
                {s.isActive ? s.name : `${s.name} (yopiq)`}
              </option>
            ))}
          </select>

          <select
            value={filters.status}
            onChange={(e) => set({ status: e.target.value as '' | SubmissionStatus })}
            aria-label="Holati"
            className={control}
          >
            <option value="">Barcha holatlar</option>
            <option value="lead">Lid yaratildi</option>
            <option value="duplicate">Takror</option>
          </select>

          <DatePicker
            value={filters.from}
            onChange={(value: string) => set({ from: value })}
            title="Topshirilgan sana — dan"
            ariaLabel="Topshirilgan sana — dan"
            clearable
            className="w-40"
          />
          <DatePicker
            value={filters.to}
            onChange={(value: string) => set({ to: value })}
            title="Topshirilgan sana — gacha"
            ariaLabel="Topshirilgan sana — gacha"
            clearable
            className="w-40"
          />

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
              <p className="font-medium text-slate-800">Arizalarni yuklab bo'lmadi</p>
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
                <p className="font-medium text-slate-600">
                  Bu shartlar bo'yicha ariza topilmadi
                </p>
                <p className="max-w-md text-sm text-slate-400">
                  Sana oralig'ini kengaytiring yoki boshqa ariza formasini tanlang.
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
                <p className="font-medium text-slate-600">Hali birorta ariza topshirilmagan</p>
                <p className="max-w-md text-sm text-slate-400">
                  Ariza formasining havolasini Instagram profiliga yoki Telegram kanaliga
                  joylang — topshirilgan har bir ariza shu yerda va lidlar doskasida
                  paydo bo'ladi.
                </p>
                <Link
                  to="/admin/marketing/arizalar"
                  className="mt-1 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  Arizalar bo'limiga o'tish
                </Link>
              </>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[60rem] text-left text-sm">
              {/* Bir qator — bir satr (mijoz, 2026-09-18): uzun ism "..." bilan
                  kesiladi, to'lig'i hoverda va yon oynada ko'rinadi. */}
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Ariza</th>
                  <th className="px-4 py-3">Ota-ona</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3">Lid</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr
                    key={row.id}
                    onClick={() => setOpened(row)}
                    className="cursor-pointer hover:bg-slate-50/60"
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {formatDateTime(row.createdAt)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      <span className="block max-w-[12rem] truncate" title={row.surveyName}>
                        {row.surveyName}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[12rem] truncate" title={row.parentFullName}>
                        {row.parentFullName}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                      {row.parentPhone}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.studentFullName ? (
                        <span className="block max-w-[12rem] truncate" title={row.studentFullName}>
                          {row.studentFullName}
                        </span>
                      ) : (
                        <span className="text-slate-400">{DASH}</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {gradeLabel(row.studentGrade)}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                          statusTone(row.status),
                        )}
                      >
                        {statusLabel(row.status)}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3">
                      {row.leadId === null ? (
                        <span className="text-slate-400" title="Lid doskadan o'chirilgan">
                          O'chirilgan
                        </span>
                      ) : canOpenLeads ? (
                        // Doskaning o'zi ochiladi: `pages/admin/leads/*` muzlatilgan,
                        // unga bitta lidga havola qiladigan marshrut qo'shilmaydi.
                        <Link
                          to="/admin/leads"
                          onClick={(e) => e.stopPropagation()}
                          title="Lidlar doskasini ochish"
                          className="inline-flex items-center gap-1 font-medium text-brand-600 hover:text-brand-700"
                        >
                          <span className="max-w-[10rem] truncate">
                            {row.leadStageTitle ?? 'Doskada'}
                          </span>
                          <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                        </Link>
                      ) : (
                        <span className="text-slate-500">{row.leadStageTitle ?? 'Doskada'}</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-right">
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
        <SubmissionDetailDrawer
          key={opened.id}
          row={opened}
          canOpenLeads={canOpenLeads}
          onClose={() => setOpened(null)}
        />
      )}
    </div>
  )
}
