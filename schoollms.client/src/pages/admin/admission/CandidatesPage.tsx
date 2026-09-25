/**
 * NOMZODLAR — the admission candidate register
 * (`docs/modules/admission-and-testing.md` §2.2, §2.3, §3.1 screen 1, §6.1, §8.5;
 * unit C2).
 *
 * A TABLE, NOT A BOARD (§2.3). A candidate is a lead in a later phase of its
 * life; the Lidlar board is design-frozen and nothing here imports from
 * `pages/admin/leads/*`. The board links in, this page links out — that is the
 * whole relationship.
 *
 * SERVER-SIDE LIST. Search, filters and paging are `GET /api/admin/leads/candidates`
 * (§6.1). Score, link state and status are printed as the server sent them;
 * nothing is recomputed here.
 *
 * ROW ACTIONS (§3.1): kartochka, imtihonga biriktirish, havolani (qayta)
 * chiqarish, biriktirishni bekor qilish — plus "O'quvchi qilish", which opens
 * the existing `LeadEnrolModal`; on success the row leaves the list (the lead
 * is deleted by enrolment, §2.2). §6.1's row has no participant id, so the two
 * participant actions resolve it through the card first
 * (`resolveParticipation`). BULK: tick rows, "Imtihonga biriktirish".
 *
 * THE DOOR IN. "Nomzod qo'shish" puts leads from the board on an admission
 * exam — that is what turns a lead into a candidate (§8.5 `invited`).
 *
 * PERMISSION. The route carries `RequirePerm perm="admission"`; every write
 * control follows §3.7 via `candidatePerms`.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  ClipboardPlus,
  Eye,
  GraduationCap,
  Link2,
  Loader2,
  RefreshCw,
  RotateCcw,
  Search,
  UserPlus,
  UserRoundCheck,
  XCircle,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import {
  CANDIDATE_PAGE_LIMIT,
  candidateError,
  issueInvitation,
  listCandidates,
  resolveParticipation,
  type CandidateErrorInfo,
  type CandidateListQuery,
  type CandidateRow,
  type CandidateStatus,
  type IssuedInvitation,
} from '@/api/services/candidates'
import { listExams, removeParticipant, type ExamRow, type Paged } from '@/api/services/exams'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { LeadEnrolModal } from '@/pages/admin/leads-enrol/LeadEnrolModal'
import { cn } from '@/lib/utils'
import {
  DASH,
  GRADES,
  ONLINE_EXAMS_ENABLED,
  SEARCH_DEBOUNCE_MS,
  control,
  formatPercent,
  formatScore,
  gradeLabel,
} from '../exams/examLabels'
import {
  CANDIDATE_STATUSES,
  admissionStatusLabel,
  admissionStatusTone,
  canIssueLink,
  canUnassign,
  candidatePath,
  candidatePerms,
  linkStateLabel,
  linkStateTone,
  noticeFromState,
  rowLinkState,
} from './CandidateHelpers'
import { useCandidateEnrol } from './CandidateEnrol'
import { CandidateAssignModal, type AssignTarget } from './CandidateAssignModal'
import { CandidateLinkModal } from './CandidateLinkModal'
import { formatPhone } from '@/lib/phone'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/** One finished request, tagged with the query it answered. */
interface ListResult {
  key: string
  page: Paged<CandidateRow> | null
  error: CandidateErrorInfo | null
}

type AssignState = { targets: AssignTarget[]; pickLeads: boolean } | null

const checkbox = 'h-4 w-4 rounded border-slate-300 accent-brand-600'

const iconButton =
  'rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent'

function targetOf(row: CandidateRow): AssignTarget {
  return { leadId: row.leadId, fullName: row.fullName, targetGrade: row.targetGrade }
}

export function CandidatesPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { user } = useAuth()
  const perms = candidatePerms(user?.permissions)

  // "… o'quvchilar ro'yxatiga qo'shildi" arrives from the card in the navigation state.
  const [notice, setNotice] = useState<Notice | null>(() => {
    const message = noticeFromState(location.state)
    return message ? { message, tone: 'success' } : null
  })
  const arrivedWithNotice = noticeFromState(location.state) !== null
  useEffect(() => {
    // Clear it from history, so a reload does not announce it again.
    if (arrivedWithNotice) navigate(location.pathname, { replace: true, state: null })
  }, [arrivedWithNotice, location.pathname, navigate])

  const toastError = useCallback((message: string) => setNotice({ message, tone: 'error' }), [])

  // ---- filter lookups --------------------------------------------------------
  // Every admission exam, closed ones included: their candidates stay filterable.
  const [exams, setExams] = useState<ExamRow[]>([])
  const [examsFailed, setExamsFailed] = useState(false)
  useEffect(() => {
    let cancelled = false
    listExams({ kind: 'admission', page: 1, limit: 200 })
      .then((page) => {
        if (!cancelled) setExams(page.items)
      })
      .catch(() => {
        if (!cancelled) setExamsFailed(true)
      })
    return () => {
      cancelled = true
    }
  }, [])

  // ---- query -----------------------------------------------------------------
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [status, setStatus] = useState<'' | CandidateStatus>('')
  const [grade, setGrade] = useState('')
  const [examId, setExamId] = useState('')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState<ListResult | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search.trim())
      setPage(1)
    }, SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const query = useMemo<CandidateListQuery>(
    () => ({
      page,
      limit: CANDIDATE_PAGE_LIMIT,
      search: debouncedSearch || undefined,
      admissionStatus: status || undefined,
      grade: grade === '' ? undefined : Number(grade),
      examId: examId || undefined,
    }),
    [page, debouncedSearch, status, grade, examId],
  )
  const queryKey = `${JSON.stringify(query)}#${reloadToken}`

  useEffect(() => {
    let cancelled = false
    listCandidates(query)
      .then((data) => {
        if (!cancelled) setResult({ key: queryKey, page: data, error: null })
      })
      .catch((err: unknown) => {
        if (!cancelled) setResult({ key: queryKey, page: null, error: candidateError(err, 'list') })
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
  const limit = result?.page?.limit || CANDIDATE_PAGE_LIMIT
  const lastPage = Math.max(1, Math.ceil(total / limit))
  const first = total === 0 ? 0 : (page - 1) * limit + 1
  const last = Math.min(page * limit, total)

  const filtersActive = Boolean(debouncedSearch) || status !== '' || grade !== '' || examId !== ''

  const reload = () => setReloadToken((t) => t + 1)

  const resetFilters = () => {
    setSearch('')
    setDebouncedSearch('')
    setStatus('')
    setGrade('')
    setExamId('')
    setPage(1)
  }

  // ---- selection (bulk assign) -------------------------------------------------
  // Kept across pages: the officer may tick candidates from several pages of one grade.
  const [selected, setSelected] = useState<Map<string, AssignTarget>>(() => new Map())
  const pageAllSelected = rows.length > 0 && rows.every((r) => selected.has(r.leadId))

  const toggleRow = (row: CandidateRow) =>
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(row.leadId)) next.delete(row.leadId)
      else next.set(row.leadId, targetOf(row))
      return next
    })

  const togglePage = () =>
    setSelected((prev) => {
      const next = new Map(prev)
      if (pageAllSelected) rows.forEach((r) => next.delete(r.leadId))
      else rows.forEach((r) => next.set(r.leadId, targetOf(r)))
      return next
    })

  // ---- actions ---------------------------------------------------------------
  const [assign, setAssign] = useState<AssignState>(null)
  const [issued, setIssued] = useState<{ result: IssuedInvitation; name: string } | null>(null)
  const [busy, setBusy] = useState<{ leadId: string; action: 'issue' | 'unassign' } | null>(null)
  const enrol = useCandidateEnrol(toastError)

  const onAssigned = (message: string) => {
    setAssign(null)
    setSelected(new Map())
    setNotice({ message, tone: 'success' })
    reload()
  }

  /** §6.1's row has no participant id — read it off the card, then act. */
  const issueLink = async (row: CandidateRow) => {
    if (busy || !row.examId) return
    const live = row.invitationState === 'issued' || row.invitationState === 'opened'
    if (live && !confirm(`${row.fullName} uchun yangi havola chiqarilsinmi?\n\nHozirgi havola darhol ishlamay qoladi.`)) {
      return
    }
    setBusy({ leadId: row.leadId, action: 'issue' })
    try {
      const participation = await resolveParticipation(row.leadId, row.examId)
      if (!participation) {
        toastError("Biriktirish topilmadi — ro'yxatni yangilang")
        reload()
        return
      }
      if (!canIssueLink(participation)) {
        toastError(
          participation.delivery === 'online'
            ? "Bu biriktirish uchun havola chiqarib bo'lmaydi: imtihon yopilgan yoki bekor qilingan, yoki nomzod testni tugatgan"
            : "Bu imtihon qog'ozda o'tkaziladi — havola kerak emas",
        )
        return
      }
      const result = await issueInvitation(participation.participantId)
      setIssued({ result, name: row.fullName })
      reload()
    } catch (err) {
      toastError(candidateError(err, 'issue').message)
    } finally {
      setBusy(null)
    }
  }

  const unassign = async (row: CandidateRow) => {
    if (busy || !row.examId) return
    if (!confirm(`${row.fullName} «${row.examTitle ?? 'imtihon'}»dan chiqarilsinmi?`)) return
    setBusy({ leadId: row.leadId, action: 'unassign' })
    try {
      const participation = await resolveParticipation(row.leadId, row.examId)
      if (participation) {
        await removeParticipant(participation.examId, participation.participantId)
        setNotice({ message: 'Biriktirish bekor qilindi', tone: 'success' })
      } else {
        toastError("Bu biriktirish allaqachon bekor qilingan — ro'yxat yangilandi")
      }
      reload()
    } catch (err) {
      toastError(candidateError(err, 'unassign').message)
    } finally {
      setBusy(null)
    }
  }

  /** Enrolment deleted the lead: take the row out now rather than re-reading the page. */
  const onEnrolled = (leadId: string) => {
    const name = rows.find((r) => r.leadId === leadId)?.fullName ?? 'Nomzod'
    setResult((prev) =>
      prev?.page
        ? {
            ...prev,
            page: {
              ...prev.page,
              items: prev.page.items.filter((r) => r.leadId !== leadId),
              total: Math.max(0, prev.page.total - 1),
            },
          }
        : prev,
    )
    setSelected((prev) => {
      if (!prev.has(leadId)) return prev
      const next = new Map(prev)
      next.delete(leadId)
      return next
    })
    setNotice({ message: `${name} o'quvchilar ro'yxatiga qo'shildi`, tone: 'success' })
  }

  const openCard = (row: CandidateRow) => navigate(candidatePath(row.leadId))

  const showSelect = perms.assign

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Nomzodlar</h1>
          <p className="text-sm text-slate-400">
            Qabul imtihoniga biriktirilgan lidlar — taklifdan o'quvchi bo'lguncha
            {!loading && total > 0 && ` · ${total} ta nomzod`}
          </p>
        </div>
        {perms.assign && (
          <Button onClick={() => setAssign({ targets: [], pickLeads: true })}>
            <UserPlus className="h-4 w-4" /> Nomzod qo'shish
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
              placeholder="Ism yoki telefon..."
              aria-label="Qidiruv"
              className={cn(control, 'w-full pl-9')}
            />
          </div>

          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as '' | CandidateStatus)
              setPage(1)
            }}
            aria-label="Holati"
            className={control}
          >
            <option value="">Barcha holatlar</option>
            {CANDIDATE_STATUSES.map((s) => (
              <option key={s} value={s}>
                {admissionStatusLabel(s)}
              </option>
            ))}
          </select>

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
            value={examId}
            onChange={(e) => {
              setExamId(e.target.value)
              setPage(1)
            }}
            disabled={examsFailed}
            aria-label="Imtihon"
            className={cn(control, 'max-w-[16rem]', examsFailed && 'text-slate-400')}
          >
            <option value="">{examsFailed ? "Imtihonlar ro'yxati yuklanmadi" : 'Barcha imtihonlar'}</option>
            {exams.map((e) => (
              <option key={e.id} value={e.id}>
                {e.title}
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

        {showSelect && selected.size > 0 && (
          <div className="flex flex-wrap items-center gap-3 border-b border-brand-100 bg-brand-50/60 px-4 py-2.5">
            <p className="text-sm font-medium text-brand-700">{selected.size} ta nomzod tanlandi</p>
            <Button onClick={() => setAssign({ targets: [...selected.values()], pickLeads: false })}>
              <ClipboardPlus className="h-4 w-4" /> Imtihonga biriktirish
            </Button>
            <button
              type="button"
              onClick={() => setSelected(new Map())}
              className="text-sm font-medium text-slate-500 transition-colors hover:text-slate-700"
            >
              Tanlovni bekor qilish
            </button>
          </div>
        )}

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
                  : "Nomzodlarni yuklab bo'lmadi"}
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
              <UserRoundCheck className="h-7 w-7 text-slate-400" />
            </div>
            {filtersActive ? (
              <>
                <p className="font-medium text-slate-600">Bu shartlar bo'yicha nomzod topilmadi</p>
                <p className="max-w-md text-sm text-slate-400">
                  Boshqa holat, sinf yoki imtihonni tanlang, yoki filtrlarni tozalang.
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
                <p className="font-medium text-slate-600">Hali nomzod yo'q</p>
                <p className="max-w-md text-sm text-slate-400">
                  Nomzod — qabul imtihoniga biriktirilgan lid. Lidlar doskasidagi lidlarni
                  imtihonga biriktiring: ular shu yerda paydo bo'ladi, test havolasi va natijasi
                  bilan.
                </p>
                {perms.assign && (
                  <Button onClick={() => setAssign({ targets: [], pickLeads: true })}>
                    <UserPlus className="h-4 w-4" /> Birinchi nomzodni qo'shish
                  </Button>
                )}
              </>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[64rem] text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  {showSelect && (
                    <th className="w-10 px-4 py-3">
                      <input
                        type="checkbox"
                        checked={pageAllSelected}
                        onChange={togglePage}
                        aria-label="Sahifadagi hammasini tanlash"
                        className={checkbox}
                      />
                    </th>
                  )}
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">FISH</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Ota-ona telefoni</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3">Imtihon</th>
                  <th className="px-4 py-3 text-right">Ball</th>
                  <th className="px-4 py-3">Havola</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, i) => {
                  const link = rowLinkState(row)
                  const rowBusy = busy?.leadId === row.leadId
                  const hasExam = row.examId !== null
                  const showIssue =
                    ONLINE_EXAMS_ENABLED &&
                    perms.invite &&
                    hasExam &&
                    (row.participantStatus === 'assigned' || row.participantStatus === 'in_progress')
                  const showUnassign = perms.unassign && hasExam && canUnassign(row.participantStatus)
                  const showEnrol = perms.enrol && row.admissionStatus !== 'rejected'
                  const liveLink = row.invitationState === 'issued' || row.invitationState === 'opened'
                  return (
                    <tr
                      key={row.leadId}
                      tabIndex={0}
                      onClick={() => openCard(row)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' && e.target === e.currentTarget) openCard(row)
                      }}
                      className={cn(
                        'cursor-pointer whitespace-nowrap outline-none hover:bg-slate-50/60 focus-visible:bg-brand-50/60',
                        selected.has(row.leadId) && 'bg-brand-50/40',
                      )}
                    >
                      {showSelect && (
                        <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={selected.has(row.leadId)}
                            onChange={() => toggleRow(row)}
                            aria-label={`${row.fullName} — tanlash`}
                            className={checkbox}
                          />
                        </td>
                      )}
                      <td className="px-4 py-3 text-slate-400">{first + i}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                        <span className="block max-w-[14rem] truncate" title={row.fullName}>
                          {row.fullName}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-600">{gradeLabel(row.targetGrade)}</td>
                      <td className="px-4 py-3 text-slate-600">{formatPhone(row.parentPhone) || DASH}</td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            admissionStatusTone(row.admissionStatus),
                          )}
                        >
                          {admissionStatusLabel(row.admissionStatus)}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {row.examTitle ? (
                          <span className="block max-w-[14rem] truncate" title={row.examTitle}>
                            {row.examTitle}
                          </span>
                        ) : (
                          <span className="text-slate-400">{DASH}</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {row.totalPoints === null ? (
                          <span className="text-slate-400">{DASH}</span>
                        ) : (
                          <>
                            <span className="font-medium text-slate-700">
                              {formatScore(row.totalPoints, row.maxPoints)}
                            </span>
                            {row.percent !== null && (
                              <span className="ml-1.5 text-xs text-slate-400">
                                {formatPercent(row.percent)}
                              </span>
                            )}
                          </>
                        )}
                      </td>
                      <td className={cn('px-4 py-3 text-sm', linkStateTone(link))}>
                        {hasExam ? linkStateLabel(link) : DASH}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          <button
                            type="button"
                            title="Kartochka"
                            aria-label="Kartochka"
                            onClick={(e) => {
                              e.stopPropagation()
                              openCard(row)
                            }}
                            className={cn(iconButton, 'hover:text-brand-600')}
                          >
                            <Eye className="h-4 w-4" />
                          </button>
                          {perms.assign && (
                            <button
                              type="button"
                              title="Imtihonga biriktirish"
                              aria-label="Imtihonga biriktirish"
                              onClick={(e) => {
                                e.stopPropagation()
                                setAssign({ targets: [targetOf(row)], pickLeads: false })
                              }}
                              className={cn(iconButton, 'hover:text-slate-700')}
                            >
                              <ClipboardPlus className="h-4 w-4" />
                            </button>
                          )}
                          {showIssue && (
                            <button
                              type="button"
                              title={liveLink ? 'Havolani qayta chiqarish' : 'Havola chiqarish'}
                              aria-label={liveLink ? 'Havolani qayta chiqarish' : 'Havola chiqarish'}
                              disabled={busy !== null}
                              onClick={(e) => {
                                e.stopPropagation()
                                void issueLink(row)
                              }}
                              className={cn(iconButton, 'hover:text-brand-600')}
                            >
                              {rowBusy && busy?.action === 'issue' ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <Link2 className="h-4 w-4" />
                              )}
                            </button>
                          )}
                          {showUnassign && (
                            <button
                              type="button"
                              title="Biriktirishni bekor qilish"
                              aria-label="Biriktirishni bekor qilish"
                              disabled={busy !== null}
                              onClick={(e) => {
                                e.stopPropagation()
                                void unassign(row)
                              }}
                              className={cn(iconButton, 'hover:bg-red-50 hover:text-red-600')}
                            >
                              {rowBusy && busy?.action === 'unassign' ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <XCircle className="h-4 w-4" />
                              )}
                            </button>
                          )}
                          {showEnrol && (
                            <button
                              type="button"
                              title="O'quvchi qilib ro'yxatga olish"
                              aria-label="O'quvchi qilib ro'yxatga olish"
                              disabled={enrol.pendingId !== null}
                              onClick={(e) => {
                                e.stopPropagation()
                                void enrol.start(row.leadId)
                              }}
                              className={cn(iconButton, 'hover:bg-emerald-50 hover:text-emerald-600')}
                            >
                              {enrol.pendingId === row.leadId ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <GraduationCap className="h-4 w-4" />
                              )}
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

      {assign && perms.assign && (
        <CandidateAssignModal
          targets={assign.targets}
          pickLeads={assign.pickLeads}
          onClose={() => setAssign(null)}
          onAssigned={onAssigned}
        />
      )}

      <CandidateLinkModal
        issued={issued?.result ?? null}
        candidateName={issued?.name ?? ''}
        onClose={() => setIssued(null)}
      />

      {perms.enrol && (
        <LeadEnrolModal lead={enrol.lead} onClose={enrol.close} onEnrolled={onEnrolled} />
      )}

      <Toast
        message={notice?.message ?? null}
        tone={notice?.tone ?? 'success'}
        onClose={() => setNotice(null)}
      />
    </div>
  )
}
