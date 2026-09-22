/**
 * IMTIHONLAR — the exam register
 * (`docs/modules/admission-and-testing.md` §3.2 screen 6, §5.5, §6.3, §8.1; unit C3).
 *
 * One list for both kinds: the block test (paper, scores typed into the entry
 * grid — §13 Q4) and the online admission exam. EduSchool's own screen was
 * not in the bundle (§0); the columns and actions are §3.2's.
 *
 * THE LIFECYCLE IS THE SERVER'S. `draft → published → closed`, and
 * `draft|published → cancelled` (§5.5). This page offers exactly the moves the
 * current status allows and lets the server refuse the rest: a publish that
 * fails §8.1 comes back 409 with the reason, and that sentence is shown as is.
 * "Closed" has no button — the nightly job sets it (§5.5).
 *
 * SERVER-SIDE LIST. Filtering and paging are `GET /api/admin/exams` (§6 paging
 * envelope); the browser never holds more than one page.
 *
 * PERMISSION. Route `exams`. Without the key: no add, edit, publish or cancel
 * (§3.7) — the list and the links to results stay. The admission kind in the
 * form needs `admission` as well (the bank picker is read-gated by it, §4.3).
 */
import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  Ban,
  ChevronLeft,
  ChevronRight,
  ClipboardCheck,
  Loader2,
  Megaphone,
  Pencil,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
} from 'lucide-react'
import {
  cancelExam,
  examsErrorMessage,
  listExamTypes,
  listExams,
  publishExam,
  type ExamKind,
  type ExamListQuery,
  type ExamRow,
  type ExamStatus,
  type ExamType,
} from '@/api/services/exams'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { DatePicker } from '@/components/ui/DatePicker'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { ExamFormModal } from './ExamFormModal'
import {
  DASH,
  PAGE_SIZE,
  SEARCH_DEBOUNCE_MS,
  control,
  examStatusLabel,
  examStatusLabels,
  examStatusTone,
  formatDay,
  formatWallClock,
  gradeLabel,
  hasPerm,
  kindLabel,
  kindLabels,
  kindTone,
} from './examLabels'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

interface FilterState {
  kind: '' | ExamKind
  status: '' | ExamStatus
  examTypeId: string
  /** `YYYY-MM-DD`, straight from the shared `DatePicker`. */
  from: string
  to: string
}

const INITIAL: FilterState = { kind: '', status: '', examTypeId: '', from: '', to: '' }

const STATUSES: ExamStatus[] = ['draft', 'published', 'closed', 'cancelled']
const KINDS: ExamKind[] = ['block', 'admission']

/** Where "results" lives for a row: the entry grid for paper exams, the register for online ones. */
function resultsLink(row: ExamRow): string {
  return row.delivery === 'manual'
    ? `/admin/exams/results/${row.id}/entry`
    : `/admin/exams/results?examId=${encodeURIComponent(row.id)}`
}

export function ExamsPage() {
  const { user } = useAuth()
  const canWrite = hasPerm(user?.permissions, 'exams')
  const canAdmission = canWrite && hasPerm(user?.permissions, 'admission')

  const [rows, setRows] = useState<ExamRow[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const [filters, setFilters] = useState<FilterState>(INITIAL)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  const [types, setTypes] = useState<ExamType[]>([])
  const [typesFailed, setTypesFailed] = useState(false)

  const [formFor, setFormFor] = useState<{ examId: string | null } | null>(null)
  const [cancelling, setCancelling] = useState<ExamRow | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  useEffect(() => {
    let cancelled = false
    listExamTypes()
      .then((data) => {
        if (!cancelled) setTypes(data)
      })
      .catch(() => {
        if (!cancelled) setTypesFailed(true)
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

  const query = useMemo<ExamListQuery>(
    () => ({
      kind: filters.kind || undefined,
      status: filters.status || undefined,
      examTypeId: filters.examTypeId || undefined,
      from: filters.from || undefined,
      to: filters.to || undefined,
      search: debouncedSearch.trim() || undefined,
    }),
    [filters, debouncedSearch],
  )

  const filtersActive = Object.values(query).some(Boolean)

  useEffect(() => {
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin "yuklanmoqda" holatini belgilaymiz (loyihadagi mavjud naqsh)
    setLoading(true)
    listExams({ ...query, page, limit: PAGE_SIZE })
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
        setError(examsErrorMessage(err, 'exams.load'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [query, page, reloadToken])

  const reload = () => setReloadToken((t) => t + 1)

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

  /** §8.1 — the server checks everything; its 409 sentence is the reason shown. */
  const publish = async (row: ExamRow) => {
    const consequence =
      row.delivery === 'online'
        ? "E'lon qilingach, nomzodlarga havola chiqarish mumkin bo'ladi, fanlar va test bazalari esa o'zgarmaydi."
        : "E'lon qilingach, fanlar ro'yxati va maksimal ballar o'zgarmaydi."
    if (!confirm(`"${row.title}" imtihonini e'lon qilasizmi?\n\n${consequence}`)) return
    setBusyId(row.id)
    try {
      await publishExam(row.id)
      setNotice({ message: `"${row.title}" e'lon qilindi`, tone: 'success' })
      reload()
    } catch (err) {
      setNotice({ message: examsErrorMessage(err, 'exam.publish'), tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const first = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const last = Math.min(page * PAGE_SIZE, total)

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Imtihonlar</h1>
          <p className="text-sm text-slate-400">
            Blok testlar va qabul imtihonlari: fanlar, sinflar, e'lon qilish va natija kiritish.
          </p>
        </div>
        {canWrite && (
          <Button onClick={() => setFormFor({ examId: null })}>
            <Plus className="h-4 w-4" /> Imtihon qo'shish
          </Button>
        )}
      </div>

      {!canWrite && (
        <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-500">
          Sizda "Imtihonlar" ruxsati yo'q — ro'yxat faqat ko'rish uchun ochiq.
        </p>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Imtihon nomi..."
              aria-label="Qidiruv"
              className={cn(control, 'w-full pl-9')}
            />
          </div>

          <select
            value={filters.kind}
            onChange={(e) => set({ kind: e.target.value as '' | ExamKind })}
            aria-label="Imtihon ko'rinishi"
            className={control}
          >
            <option value="">Blok test va qabul</option>
            {KINDS.map((k) => (
              <option key={k} value={k}>
                {kindLabels[k]}
              </option>
            ))}
          </select>

          <select
            value={filters.status}
            onChange={(e) => set({ status: e.target.value as '' | ExamStatus })}
            aria-label="Holati"
            className={control}
          >
            <option value="">Barcha holatlar</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {examStatusLabels[s]}
              </option>
            ))}
          </select>

          <select
            value={filters.examTypeId}
            onChange={(e) => set({ examTypeId: e.target.value })}
            disabled={typesFailed}
            aria-label="Imtihon turi"
            className={cn(control, typesFailed && 'text-slate-400')}
          >
            <option value="">{typesFailed ? 'Turlar yuklanmadi' : 'Barcha turlar'}</option>
            {types.map((t) => (
              <option key={t.id} value={t.id}>
                {t.isActive ? t.name : `${t.name} (faol emas)`}
              </option>
            ))}
          </select>

          <DatePicker
            value={filters.from}
            onChange={(value: string) => set({ from: value })}
            title="Imtihon sanasi — dan"
            ariaLabel="Imtihon sanasi — dan"
            clearable
            className="w-40"
          />
          <DatePicker
            value={filters.to}
            onChange={(value: string) => set({ to: value })}
            min={filters.from || undefined}
            title="Imtihon sanasi — gacha"
            ariaLabel="Imtihon sanasi — gacha"
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
              <p className="font-medium text-slate-800">Imtihonlarni yuklab bo'lmadi</p>
              <p className="mt-1 max-w-md text-sm text-slate-500">{error}</p>
            </div>
            <Button variant="secondary" onClick={reload}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        ) : rows.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
            <ClipboardCheck className="h-8 w-8 text-slate-300" />
            {filtersActive ? (
              <>
                <p className="font-medium text-slate-600">Bu shartlar bo'yicha imtihon topilmadi</p>
                <p className="max-w-md text-sm text-slate-400">
                  Sana oralig'ini kengaytiring yoki boshqa holatni tanlang.
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
                <p className="font-medium text-slate-600">Hali birorta imtihon yo'q</p>
                <p className="max-w-md text-sm text-slate-400">
                  Imtihonni yarating: fanlar va ularning maksimal ballini, qatnashadigan sinflarni
                  belgilang. Keyin natijalarni jadvalga kiritasiz yoki Excel'dan yuklaysiz.
                </p>
                {canWrite && (
                  <Button className="mt-1" onClick={() => setFormFor({ examId: null })}>
                    <Plus className="h-4 w-4" /> Birinchi imtihonni qo'shish
                  </Button>
                )}
              </>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[60rem] text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Ko'rinishi</th>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3 text-right">Fanlar</th>
                  <th className="px-4 py-3 text-right">Ishtirokchilar</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => {
                  const live = row.status === 'draft' || row.status === 'published'
                  const busy = busyId === row.id
                  return (
                    <tr key={row.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3">
                        <span
                          className="block max-w-[18rem] truncate font-medium text-slate-800"
                          title={row.title}
                        >
                          {row.title}
                        </span>
                        {row.examTypeName && (
                          <span className="block max-w-[18rem] truncate text-xs text-slate-400">
                            {row.examTypeName}
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3">
                        <span
                          className={cn(
                            'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                            kindTone(row.kind),
                          )}
                        >
                          {kindLabel(row.kind)}
                        </span>
                        {row.kind === 'admission' && row.grade !== null && (
                          <span className="ml-1.5 text-xs text-slate-400">{gradeLabel(row.grade)}</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                        {row.delivery === 'online' ? (
                          <span className="block text-xs leading-5">
                            {formatWallClock(row.opensAt)}
                            <br />
                            {formatWallClock(row.closesAt)}
                          </span>
                        ) : (
                          formatDay(row.examDate)
                        )}
                      </td>
                      <td className="px-4 py-3 text-right text-slate-600">
                        {row.sectionCount ?? DASH}
                      </td>
                      <td
                        className="whitespace-nowrap px-4 py-3 text-right text-slate-600"
                        title="Baholangan / jami"
                      >
                        {row.participantCount === undefined
                          ? DASH
                          : row.finishedCount === undefined
                            ? row.participantCount
                            : `${row.finishedCount} / ${row.participantCount}`}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                            examStatusTone(row.status),
                          )}
                        >
                          {examStatusLabel(row.status)}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          <Link
                            to={resultsLink(row)}
                            className="mr-1 inline-flex items-center gap-1 whitespace-nowrap rounded-lg px-2 py-1.5 text-xs font-medium text-brand-600 transition-colors hover:bg-brand-50"
                          >
                            {row.delivery === 'manual' && canWrite && live ? 'Natija kiritish' : 'Natijalar'}
                          </Link>
                          {canWrite && live && (
                            <button
                              type="button"
                              title="Tahrirlash"
                              aria-label="Tahrirlash"
                              onClick={() => setFormFor({ examId: row.id })}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <Pencil className="h-4 w-4" />
                            </button>
                          )}
                          {canWrite && row.status === 'draft' && (
                            <button
                              type="button"
                              disabled={busy}
                              title="E'lon qilish"
                              aria-label="E'lon qilish"
                              onClick={() => void publish(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-emerald-50 hover:text-emerald-600 disabled:cursor-not-allowed disabled:text-slate-200"
                            >
                              {busy ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <Megaphone className="h-4 w-4" />
                              )}
                            </button>
                          )}
                          {canWrite && live && (
                            <button
                              type="button"
                              disabled={busy}
                              title="Bekor qilish"
                              aria-label="Bekor qilish"
                              onClick={() => setCancelling(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200"
                            >
                              <Ban className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
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

      {formFor && canWrite && (
        <ExamFormModal
          examId={formFor.examId}
          canAdmission={canAdmission}
          onClose={() => setFormFor(null)}
          onChanged={reload}
          onSaved={(message) => {
            setFormFor(null)
            setNotice({ message, tone: 'success' })
            reload()
          }}
        />
      )}

      {cancelling && canWrite && (
        <CancelExamModal
          key={cancelling.id}
          exam={cancelling}
          onClose={() => setCancelling(null)}
          onCancelled={() => {
            setNotice({ message: `"${cancelling.title}" bekor qilindi`, tone: 'success' })
            setCancelling(null)
            reload()
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

interface CancelProps {
  exam: ExamRow
  onClose: () => void
  onCancelled: () => void
}

/**
 * `POST /exams/{id}/cancel { reason }`. Cancelling revokes every live
 * invitation and marks every unfinished participant `cancelled` (§5.5) —
 * the dialog says so, because it cannot be undone from this screen.
 */
function CancelExamModal({ exam, onClose, onCancelled }: CancelProps) {
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!reason.trim() || saving) return
    setSaving(true)
    setError(null)
    try {
      await cancelExam(exam.id, reason.trim())
      onCancelled()
    } catch (err) {
      setError(examsErrorMessage(err, 'exam.cancel'))
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Imtihonni bekor qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Ortga
          </Button>
          <Button variant="danger" type="submit" form="exam-cancel-form" disabled={!reason.trim() || saving}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Bekor qilish
          </Button>
        </>
      }
    >
      <form id="exam-cancel-form" onSubmit={(e) => void submit(e)} className="space-y-3 text-sm">
        <p className="text-slate-600">
          <b className="text-slate-800">"{exam.title}"</b> bekor qilinadi. Hali baholanmagan
          ishtirokchilar "bekor qilingan" bo'ladi
          {exam.delivery === 'online' && ', chiqarilgan havolalar esa ishlamay qoladi'}. Buni ortga
          qaytarib bo'lmaydi.
        </p>
        <Textarea
          label="Sababi"
          required
          autoFocus
          rows={3}
          maxLength={500}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="masalan: imtihon boshqa kunga ko'chirildi"
        />
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-red-700">{error}</p>
        )}
      </form>
    </Modal>
  )
}
