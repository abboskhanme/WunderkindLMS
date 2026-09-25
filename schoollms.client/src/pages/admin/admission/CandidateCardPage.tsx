/**
 * NOMZOD KARTOCHKASI — one candidate
 * (`docs/modules/admission-and-testing.md` §3.1 screen 2, §6.1, §6.4, §8.5; unit C2).
 *
 * Left: the lead, read-only — editing a lead is the board's job, and the board
 * is design-frozen (§2.3), so this page links to it and stops there. Right:
 * the exam assignment(s), the link's state, the result with per-subject bars
 * and the per-question review, and the actions of §3.1.
 *
 * THE DECISION IS A HUMAN ONE (§8.5). "Qabul qilish" / "Rad etish" set
 * `accepted` / `rejected` through `PATCH /admission-status`; nothing on this
 * screen sets a status from a score. `enrolled` is not offered anywhere: it is
 * not a stored status (§2.2, 2026-09-22).
 *
 * ENROLMENT is `LeadEnrolModal`, unchanged (see `CandidateEnrol.ts` for where
 * its `Lead` comes from). On success the lead no longer exists, so the page
 * returns to the register with a notice instead of re-reading a 404.
 *
 * PERMISSION. The route carries `RequirePerm perm="admission"`; the controls
 * follow §3.7 through `candidatePerms` — a control the user cannot use is not
 * rendered.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  AlertTriangle,
  ArrowLeft,
  ClipboardList,
  ClipboardPlus,
  ExternalLink,
  GraduationCap,
  Loader2,
  RefreshCw,
  ThumbsDown,
  ThumbsUp,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import {
  candidateError,
  getCandidate,
  setAdmissionStatus,
  type AdmissionDecision,
  type CandidateCard,
  type CandidateErrorInfo,
  type IssuedInvitation,
} from '@/api/services/candidates'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { LeadEnrolModal } from '@/pages/admin/leads-enrol/LeadEnrolModal'
import { cn } from '@/lib/utils'
import {
  DASH,
  formatDay,
  formatPercent,
  formatScore,
  gradeLabel,
  participantStatusLabel,
} from '../exams/examLabels'
import {
  CANDIDATES_PATH,
  admissionStatusLabel,
  admissionStatusTone,
  candidatePerms,
  type CandidateNavState,
} from './CandidateHelpers'
import { useCandidateEnrol } from './CandidateEnrol'
import { CandidateAssignModal } from './CandidateAssignModal'
import { CandidateLinkModal } from './CandidateLinkModal'
import { CandidateParticipationPanel } from './CandidateParticipationPanel'
import { CandidateReasonModal, type ReasonRequest } from './CandidateReasonModal'
import { CandidateResult } from './CandidateResult'
import { formatPhone, phoneHref } from '@/lib/phone'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/** One finished request, tagged with the load it answered. */
interface Loaded {
  key: string
  data: CandidateCard | null
  error: CandidateErrorInfo | null
}

const GENDER_LABELS: Record<string, string> = { male: "O'g'il bola", female: 'Qiz bola' }

function BackLink() {
  return (
    <Link
      to={CANDIDATES_PATH}
      className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-800"
    >
      <ArrowLeft className="h-4 w-4" /> Nomzodlar
    </Link>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="py-2">
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="mt-0.5 break-words text-sm text-slate-700">{children}</dd>
    </div>
  )
}

export function CandidateCardPage() {
  const { leadId = '' } = useParams<{ leadId: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()
  const perms = candidatePerms(user?.permissions)

  const [notice, setNotice] = useState<Notice | null>(null)
  const toastError = useCallback((message: string) => setNotice({ message, tone: 'error' }), [])

  // ---- the card --------------------------------------------------------------
  const [token, setToken] = useState(0)
  const loadKey = `${leadId}#${token}`
  const [loaded, setLoaded] = useState<Loaded | null>(null)

  useEffect(() => {
    if (!leadId) return
    let cancelled = false
    getCandidate(leadId)
      .then((data) => {
        if (!cancelled) setLoaded({ key: loadKey, data, error: null })
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoaded({ key: loadKey, data: null, error: candidateError(err, 'open') })
      })
    return () => {
      cancelled = true
    }
  }, [leadId, loadKey])

  // Loading is derived: the stored result belongs to an older load.
  const current = loaded?.key === loadKey ? loaded : null
  const card = current?.data ?? null

  /** Re-read after a change — quietly: the screen stays, only the values move. */
  const refresh = useCallback(async () => {
    try {
      const data = await getCandidate(leadId)
      setLoaded((prev) => (prev && prev.key === loadKey ? { ...prev, data } : prev))
    } catch (err) {
      setNotice({ message: candidateError(err, 'open').message, tone: 'error' })
    }
  }, [leadId, loadKey])

  const changed = useCallback(
    (message: string) => {
      setNotice({ message, tone: 'success' })
      void refresh()
    },
    [refresh],
  )

  // ---- which participation is on screen --------------------------------------
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const participations = useMemo(() => card?.participations ?? [], [card])
  const selected =
    participations.find((p) => p.participantId === selectedId) ?? participations[0] ?? null

  // ---- modals ----------------------------------------------------------------
  const [assignOpen, setAssignOpen] = useState(false)
  const [issued, setIssued] = useState<IssuedInvitation | null>(null)
  const [reason, setReason] = useState<{ id: number; request: ReasonRequest } | null>(null)
  const [deciding, setDeciding] = useState<AdmissionDecision | null>(null)
  const enrol = useCandidateEnrol(toastError)

  const onIssued = (result: IssuedInvitation) => {
    setIssued(result)
    void refresh()
  }

  const ask = (request: ReasonRequest) => setReason((prev) => ({ id: (prev?.id ?? 0) + 1, request }))

  const decide = async (status: AdmissionDecision) => {
    if (!card || deciding) return
    if (status === 'rejected' && !confirm(`${card.fullName} rad etilsinmi?`)) return
    setDeciding(status)
    try {
      await setAdmissionStatus(card.leadId, status)
      changed(status === 'accepted' ? 'Nomzod qabul qilindi' : 'Nomzod rad etildi')
    } catch (err) {
      toastError(candidateError(err, 'decide').message)
    } finally {
      setDeciding(null)
    }
  }

  const onEnrolled = () => {
    const state: CandidateNavState = {
      notice: `${card?.fullName ?? 'Nomzod'} o'quvchilar ro'yxatiga qo'shildi`,
    }
    navigate(CANDIDATES_PATH, { state })
  }

  // ---- render ----------------------------------------------------------------
  if (current === null) {
    return (
      <div className="space-y-6">
        <BackLink />
        <Loader label="Yuklanmoqda..." />
      </div>
    )
  }

  if (current.error || !card) {
    const gone = current.error?.kind === 'not_found'
    const forbidden = current.error?.kind === 'forbidden'
    return (
      <div className="space-y-6">
        <BackLink />
        <Card className="flex flex-col items-center gap-3 px-4 py-14 text-center">
          <div
            className={cn(
              'flex h-12 w-12 items-center justify-center rounded-xl',
              gone ? 'bg-slate-100 text-slate-400' : 'bg-red-50 text-red-600',
            )}
          >
            <AlertTriangle className="h-6 w-6" />
          </div>
          <div>
            <p className="font-medium text-slate-800">
              {gone
                ? 'Nomzod topilmadi'
                : forbidden
                  ? "Bu bo'limga ruxsatingiz yo'q"
                  : "Nomzod kartochkasini ochib bo'lmadi"}
            </p>
            <p className="mt-1 max-w-md text-sm text-slate-500">{current.error?.message}</p>
          </div>
          {gone || forbidden ? (
            <Link
              to={CANDIDATES_PATH}
              className="text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
            >
              Nomzodlar ro'yxatiga qaytish
            </Link>
          ) : (
            <Button variant="secondary" onClick={() => setToken((t) => t + 1)}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          )}
        </Card>
      </div>
    )
  }

  const status = card.admissionStatus
  const showAccept = perms.decide && status !== 'accepted' && status !== 'none'
  const showReject = perms.decide && status !== 'rejected' && status !== 'none'
  const showEnrol = perms.enrol && status !== 'rejected'
  const scored = selected && (selected.status === 'in_progress' || selected.status === 'finished')

  return (
    <div className="space-y-6">
      <BackLink />

      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="truncate text-xl font-semibold text-slate-800">{card.fullName}</h1>
          <p className="mt-1 flex flex-wrap items-center gap-2 text-sm text-slate-400">
            <span className={cn('rounded-full px-2 py-0.5 text-xs font-medium', admissionStatusTone(status))}>
              {admissionStatusLabel(status)}
            </span>
            <span>{gradeLabel(card.targetGrade)}ga</span>
            {card.percent !== null && (
              <span>
                · {formatScore(card.totalPoints, card.maxPoints)} ball ({formatPercent(card.percent)})
              </span>
            )}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {showReject && (
            <Button
              variant="ghost"
              onClick={() => void decide('rejected')}
              disabled={deciding !== null}
              className="text-red-600 hover:bg-red-50 hover:text-red-700"
            >
              {deciding === 'rejected' ? <Loader2 className="h-4 w-4 animate-spin" /> : <ThumbsDown className="h-4 w-4" />}
              Rad etish
            </Button>
          )}
          {showAccept && (
            <Button
              variant={status === 'tested' ? 'primary' : 'secondary'}
              onClick={() => void decide('accepted')}
              disabled={deciding !== null}
            >
              {deciding === 'accepted' ? <Loader2 className="h-4 w-4 animate-spin" /> : <ThumbsUp className="h-4 w-4" />}
              Qabul qilish
            </Button>
          )}
          {showEnrol && (
            <Button
              variant={status === 'accepted' ? 'primary' : 'secondary'}
              onClick={() => void enrol.start(card.leadId)}
              disabled={enrol.pendingId !== null}
              title="Oddiy o'quvchi formasi lid ma'lumotlari bilan to'ldirilib ochiladi; saqlangach lid doskadan o'chadi"
            >
              {enrol.pendingId ? <Loader2 className="h-4 w-4 animate-spin" /> : <GraduationCap className="h-4 w-4" />}
              O'quvchi qilib ro'yxatga olish
            </Button>
          )}
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="h-fit">
          <h2 className="font-semibold text-slate-800">Lid ma'lumotlari</h2>
          <dl className="mt-2 divide-y divide-slate-50">
            <Field label="FISH">{card.fullName}</Field>
            <Field label="Jinsi">{GENDER_LABELS[card.gender] ?? DASH}</Field>
            <Field label="Tug'ilgan sana">{card.birthDate ? formatDay(card.birthDate) : DASH}</Field>
            <Field label="Qaysi sinfga">{gradeLabel(card.targetGrade)}</Field>
            <Field label="Ota-onasi">{card.parentFullName || DASH}</Field>
            <Field label="Telefon">
              {card.parentPhone ? (
                <a href={phoneHref(card.parentPhone)} className="text-brand-600 hover:text-brand-700">
                  {formatPhone(card.parentPhone)}
                </a>
              ) : (
                DASH
              )}
            </Field>
            <Field label="Doskadagi bosqich">{card.stageName || DASH}</Field>
            {card.note && <Field label="Izoh">{card.note}</Field>}
          </dl>
          <p className="mt-3 border-t border-slate-100 pt-3 text-xs text-slate-400">
            Ma'lumotlar faqat o'qish uchun — tahrirlash lidlar doskasida.
            {perms.openLeads && (
              <Link
                to="/admin/leads"
                className="ml-1 inline-flex items-center gap-1 font-medium text-brand-600 hover:text-brand-700"
              >
                Doskani ochish <ExternalLink className="h-3 w-3" />
              </Link>
            )}
          </p>
        </Card>

        <div className="space-y-6 lg:col-span-2">
          <Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="font-semibold text-slate-800">Imtihon</h2>
              {perms.assign && participations.length > 0 && (
                <Button variant="secondary" onClick={() => setAssignOpen(true)}>
                  <ClipboardPlus className="h-4 w-4" /> Imtihonga biriktirish
                </Button>
              )}
            </div>

            {participations.length === 0 ? (
              <div className="flex flex-col items-center gap-2 px-4 py-10 text-center">
                <ClipboardList className="h-8 w-8 text-slate-300" />
                <p className="font-medium text-slate-600">Nomzod hali imtihonga biriktirilmagan</p>
                <p className="max-w-sm text-sm text-slate-400">
                  Qabul imtihoniga biriktiring, so'ng havola chiqarib ota-onaga Telegram orqali
                  yuboring.
                </p>
                {perms.assign && (
                  <Button className="mt-1" onClick={() => setAssignOpen(true)}>
                    <ClipboardPlus className="h-4 w-4" /> Imtihonga biriktirish
                  </Button>
                )}
              </div>
            ) : (
              <div className="mt-4 space-y-4">
                {participations.length > 1 && (
                  <div className="flex flex-wrap gap-2" role="tablist" aria-label="Imtihonlar">
                    {participations.map((p) => {
                      const active = p.participantId === selected?.participantId
                      return (
                        <button
                          key={p.participantId}
                          type="button"
                          role="tab"
                          aria-selected={active}
                          onClick={() => setSelectedId(p.participantId)}
                          className={cn(
                            'max-w-[16rem] rounded-lg border px-3 py-1.5 text-left text-xs transition-colors',
                            active
                              ? 'border-brand-300 bg-brand-50 text-brand-700'
                              : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                          )}
                        >
                          <span className="block truncate font-medium">{p.examTitle}</span>
                          <span className="block text-slate-400">{participantStatusLabel(p.status)}</span>
                        </button>
                      )
                    })}
                  </div>
                )}
                {selected && (
                  <CandidateParticipationPanel
                    key={selected.participantId}
                    participation={selected}
                    candidateName={card.fullName}
                    perms={perms}
                    onIssued={onIssued}
                    onChanged={changed}
                    onError={toastError}
                  />
                )}
              </div>
            )}
          </Card>

          {selected && (
            <Card>
              <h2 className="mb-4 font-semibold text-slate-800">Natija</h2>
              {scored ? (
                <CandidateResult
                  key={`${selected.participantId}:${selected.status}:${selected.scoredAt ?? ''}`}
                  participation={selected}
                  perms={perms}
                  onAsk={ask}
                  onChanged={changed}
                />
              ) : (
                <p className="py-6 text-center text-sm text-slate-400">
                  {selected.status === 'cancelled'
                    ? "Bu biriktirish bekor qilingan — natija yo'q."
                    : "Natija nomzod testni boshlagach shu yerda ko'rinadi."}
                </p>
              )}
            </Card>
          )}
        </div>
      </div>

      {assignOpen && perms.assign && (
        <CandidateAssignModal
          targets={[{ leadId: card.leadId, fullName: card.fullName, targetGrade: card.targetGrade }]}
          pickLeads={false}
          onClose={() => setAssignOpen(false)}
          onAssigned={(message) => {
            setAssignOpen(false)
            setSelectedId(null)
            changed(message)
          }}
        />
      )}

      <CandidateLinkModal issued={issued} candidateName={card.fullName} onClose={() => setIssued(null)} />

      <CandidateReasonModal
        key={reason?.id ?? 0}
        request={reason?.request ?? null}
        onClose={() => setReason(null)}
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
