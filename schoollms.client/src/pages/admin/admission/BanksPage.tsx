/**
 * TEST BAZASI — the question-bank register
 * (`docs/modules/admission-and-testing.md` §3.1 screen 3, §6.2; unit C1).
 *
 * One bank per grade × subject (§5.1). The entrance exam draws its paper from
 * here (§8.2), so the register answers one question at a glance: which banks
 * are ready to be used, and which still need questions or settings.
 *
 * THE BADGE IS THE SERVER'S. `state` and `questionsCount` are derived on the
 * server (§10 "Reports, not storage"); this page prints them and never
 * re-derives ready / notEnough / unconfigured.
 *
 * PERMISSION (§3.7, §4.3). The route is wrapped in `RequirePerm perm="admission"`
 * and, unlike most admin lists, the READ is gated on the server too — the bank
 * holds a live exam's answer key. Inside the page the write controls follow
 * the same key; a button that always 403s is worse than no button.
 *
 * Route and menu are the wiring pass's job (§11 S4, S5); until then this page
 * is unreachable on purpose.
 */
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  Library,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  SlidersHorizontal,
  Trash2,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { useAsync } from '@/hooks/useAsync'
import { getSubjects } from '@/api/services/subjects'
import {
  DEFAULT_PAGE_LIMIT,
  admissionBankError,
  deleteBank,
  listBanks,
  type BankErrorInfo,
  type BankListQuery,
  type Paged,
  type QuestionBank,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import {
  GRADES,
  bankPath,
  bankStateLabel,
  bankStateTone,
  bankTitle,
  canManageAdmission,
  formatCount,
  formatMinutes,
  formatPoints,
  gradeLabel,
  type BankNavState,
} from './BankHelpers'
import { BankFormModal } from './BankFormModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** A keystroke must not become a request — search is server-side (§6.2). */
const SEARCH_DEBOUNCE_MS = 350

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/** One finished request, tagged with the query it answered. */
interface ListResult {
  key: string
  page: Paged<QuestionBank> | null
  error: BankErrorInfo | null
}

export function BanksPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const canWrite = canManageAdmission(user?.permissions)

  const subjects = useAsync(() => getSubjects(), [])
  const allSubjects = useMemo(() => subjects.data ?? [], [subjects.data])
  const activeSubjects = useMemo(
    () => allSubjects.filter((s) => s.isActive !== false),
    [allSubjects],
  )
  const subjectNames = useMemo(
    () => new Map(allSubjects.map((s) => [s.id, s.name] as const)),
    [allSubjects],
  )

  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [grade, setGrade] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState<ListResult | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<QuestionBank | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search.trim())
      setPage(1)
    }, SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const query = useMemo<BankListQuery>(
    () => ({
      page,
      limit: DEFAULT_PAGE_LIMIT,
      search: debouncedSearch || undefined,
      grade: grade === '' ? undefined : Number(grade),
      subjectId: subjectId || undefined,
    }),
    [page, debouncedSearch, grade, subjectId],
  )
  const queryKey = `${JSON.stringify(query)}#${reloadToken}`

  useEffect(() => {
    let cancelled = false
    listBanks(query)
      .then((data) => {
        if (!cancelled) setResult({ key: queryKey, page: data, error: null })
      })
      .catch((err: unknown) => {
        if (!cancelled) setResult({ key: queryKey, page: null, error: admissionBankError(err, 'list') })
      })
    return () => {
      cancelled = true
    }
  }, [query, queryKey])

  // Loading is derived: the stored result belongs to an older query.
  const current = result?.key === queryKey ? result : null
  const loading = current === null
  const rows = current?.page?.items ?? []
  // Paging keeps the last known total while the next page loads — no "2 / 1" flash.
  const total = result?.page?.total ?? 0
  const limit = result?.page?.limit || DEFAULT_PAGE_LIMIT
  const lastPage = Math.max(1, Math.ceil(total / limit))
  const first = total === 0 ? 0 : (page - 1) * limit + 1
  const last = Math.min(page * limit, total)

  const filtersActive = Boolean(debouncedSearch) || grade !== '' || subjectId !== ''

  const reload = () => setReloadToken((t) => t + 1)

  const resetFilters = () => {
    setSearch('')
    setDebouncedSearch('')
    setGrade('')
    setSubjectId('')
    setPage(1)
  }

  const subjectOf = (row: QuestionBank) =>
    row.subjectName?.trim() || subjectNames.get(row.subjectId) || '—'

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (row: QuestionBank) => {
    setEditing(row)
    setFormOpen(true)
  }

  const closeForm = () => {
    setFormOpen(false)
    setEditing(null)
  }

  const onSaved = (saved: QuestionBank, created: boolean) => {
    closeForm()
    if (created) {
      // Straight into the new bank — the next thing anyone does is add questions.
      const state: BankNavState = { notice: `${bankTitle(saved)} bazasi yaratildi` }
      navigate(bankPath(saved.id), { state })
      return
    }
    // The server's row replaces ours: its `state` may have changed with the settings.
    setResult((prev) =>
      prev?.page
        ? {
            ...prev,
            page: {
              ...prev.page,
              items: prev.page.items.map((r) => (r.id === saved.id ? { ...r, ...saved } : r)),
            },
          }
        : prev,
    )
    setNotice({ message: 'Sozlamalar saqlandi', tone: 'success' })
  }

  const remove = async (row: QuestionBank) => {
    const title = `${gradeLabel(row.grade)} · ${subjectOf(row)}`
    const questions =
      row.questionsCount > 0 ? ` Undagi ${row.questionsCount} ta savol ham o'chadi.` : ''
    if (!confirm(`«${title}» bazasini o'chirasizmi?${questions}`)) return
    setBusyId(row.id)
    try {
      await deleteBank(row.id)
      setNotice({ message: `«${title}» bazasi o'chirildi`, tone: 'success' })
      if (rows.length === 1 && page > 1) setPage((p) => p - 1)
      else reload()
    } catch (err) {
      setNotice({ message: admissionBankError(err, 'deleteBank').message, tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Test bazasi</h1>
          <p className="text-sm text-slate-400">
            Qabul imtihonining savollari — har bir sinf va fan uchun bitta baza
            {!loading && total > 0 && ` · ${total} ta baza`}
          </p>
        </div>
        {canWrite && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Baza qo'shish
          </Button>
        )}
      </div>

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Fan nomi bo'yicha qidirish..."
              aria-label="Qidiruv"
              className={cn(control, 'w-full pl-9')}
            />
          </div>

          <select
            value={grade}
            onChange={(e) => {
              setGrade(e.target.value)
              setPage(1)
            }}
            aria-label="Sinf"
            className={control}
          >
            <option value="">Barcha sinflar</option>
            {GRADES.map((g) => (
              <option key={g} value={g}>
                {gradeLabel(g)}
              </option>
            ))}
          </select>

          <select
            value={subjectId}
            onChange={(e) => {
              setSubjectId(e.target.value)
              setPage(1)
            }}
            disabled={Boolean(subjects.error)}
            aria-label="Fan"
            className={cn(control, subjects.error && 'text-slate-400')}
          >
            <option value="">
              {subjects.error ? "Fanlar ro'yxati yuklanmadi" : 'Barcha fanlar'}
            </option>
            {allSubjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.isActive === false ? `${s.name} (faol emas)` : s.name}
              </option>
            ))}
          </select>

          {filtersActive && (
            <button
              type="button"
              onClick={resetFilters}
              title="Filtrlarni tozalash"
              className="ml-auto inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              Tozalash
            </button>
          )}
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : current?.error ? (
          <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <div>
              <p className="font-medium text-slate-800">
                {current.error.kind === 'forbidden'
                  ? "Bu bo'limga ruxsatingiz yo'q"
                  : "Test bazasini yuklab bo'lmadi"}
              </p>
              <p className="mt-1 max-w-md text-sm text-slate-500">{current.error.message}</p>
            </div>
            {current.error.kind !== 'forbidden' && (
              <Button variant="secondary" onClick={reload}>
                <RefreshCw className="h-4 w-4" /> Qayta urinish
              </Button>
            )}
          </div>
        ) : rows.length === 0 ? (
          <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
              <Library className="h-7 w-7 text-slate-400" />
            </div>
            {filtersActive ? (
              <>
                <p className="font-medium text-slate-600">Bu shartlar bo'yicha baza topilmadi</p>
                <p className="max-w-md text-sm text-slate-400">
                  Boshqa sinf yoki fanni tanlang, yoki filtrlarni tozalang.
                </p>
                <button
                  type="button"
                  onClick={resetFilters}
                  className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  <RotateCcw className="h-3.5 w-3.5" /> Filtrlarni tozalash
                </button>
              </>
            ) : (
              <>
                <p className="font-medium text-slate-600">Hali birorta baza yo'q</p>
                <p className="max-w-md text-sm text-slate-400">
                  Baza — bitta sinf va fan bo'yicha savollar to'plami. Uni yarating, savollarni
                  qo'lda yoki Excel'dan qo'shing: qabul imtihoni savollarni shu yerdan tasodifiy
                  tanlab oladi.
                </p>
                {canWrite && (
                  <Button onClick={openCreate}>
                    <Plus className="h-4 w-4" /> Birinchi bazani yaratish
                  </Button>
                )}
              </>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[52rem] text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Fan</th>
                  <th className="px-4 py-3 text-right">Bazadagi savollar</th>
                  <th className="px-4 py-3 text-right">Ball</th>
                  <th className="px-4 py-3 text-right">Testdagi savollar</th>
                  <th className="px-4 py-3 text-right">Vaqt</th>
                  <th className="px-4 py-3">Holati</th>
                  {canWrite && <th className="px-4 py-3 text-right">Amallar</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, i) => (
                  <tr
                    key={row.id}
                    tabIndex={0}
                    onClick={() => navigate(bankPath(row.id))}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' && e.target === e.currentTarget) {
                        navigate(bankPath(row.id))
                      }
                    }}
                    className="cursor-pointer whitespace-nowrap outline-none hover:bg-slate-50/60 focus-visible:bg-brand-50/60"
                  >
                    <td className="px-4 py-3 text-slate-400">{first + i}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {gradeLabel(row.grade)}
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      <span className="block max-w-[16rem] truncate" title={subjectOf(row)}>
                        {subjectOf(row)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right font-medium text-slate-700">
                      {row.questionsCount}
                    </td>
                    <td className="px-4 py-3 text-right text-slate-600">
                      {formatPoints(row.pointsPerCorrect)}
                    </td>
                    <td className="px-4 py-3 text-right text-slate-600">
                      {formatCount(row.questionsPerTest)}
                    </td>
                    <td className="px-4 py-3 text-right text-slate-600">
                      {formatMinutes(row.timeLimitMin)}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-full px-2 py-0.5 text-xs font-medium',
                          bankStateTone(row.state),
                        )}
                      >
                        {bankStateLabel(row.state)}
                      </span>
                    </td>
                    {canWrite && (
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          <button
                            type="button"
                            title="Sozlamalar"
                            aria-label="Sozlamalar"
                            onClick={(e) => {
                              e.stopPropagation()
                              openEdit(row)
                            }}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <SlidersHorizontal className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title="O'chirish"
                            aria-label="O'chirish"
                            disabled={busyId === row.id}
                            onClick={(e) => {
                              e.stopPropagation()
                              void remove(row)
                            }}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!current?.error && (
          <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
            <p className="text-xs text-slate-400">
              {loading ? '' : total === 0 ? "Yozuv yo'q" : `${first}–${last} / ${total} ta`}
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
        )}
      </Card>

      {formOpen && canWrite && (
        <BankFormModal
          editing={editing}
          subjects={activeSubjects}
          subjectsError={
            subjects.error ? "Fanlar ro'yxatini yuklab bo'lmadi — sahifani yangilang" : null
          }
          subjectsLoading={subjects.loading}
          onClose={closeForm}
          onSaved={onSaved}
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
