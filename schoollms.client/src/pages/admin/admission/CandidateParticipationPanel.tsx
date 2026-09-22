/**
 * One exam assignment of a candidate — the exam, the link, and what can be
 * done to either (`docs/modules/admission-and-testing.md` §3.1 screen 2,
 * §6.3, §6.4, §7.2; unit C2).
 *
 * THE LINK IS NEVER SHOWN HERE. The card knows only the invitation's state and
 * its last six characters (`tokenHint`, §5.9) — enough for support to tell
 * which link a parent is holding. The URL itself exists once, in the issue
 * call's answer, which the page shows in `CandidateLinkModal` (§7.2).
 *
 * ACTIONS AND THEIR KEYS (§3.7):
 *   · "Havola chiqarish" / "Havolani qayta chiqarish" — `admission`; a re-issue
 *     revokes the live link, and the user is told so before it happens;
 *   · "Havolani bekor qilish"     — `admission`;
 *   · "Biriktirishni bekor qilish" — `exams` (the participant endpoint), and
 *     only before the candidate has started (§6.3: 409 once an attempt exists).
 */
import { useState } from 'react'
import type { ReactNode } from 'react'
import { Link2, Link2Off, Loader2, XCircle } from 'lucide-react'
import {
  candidateError,
  issueInvitation,
  revokeInvitation,
  type CandidateParticipation,
  type IssuedInvitation,
} from '@/api/services/candidates'
import { removeParticipant } from '@/api/services/exams'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import {
  DASH,
  examStatusLabel,
  examStatusTone,
  formatDay,
  formatWallClock,
  gradeLabel,
  participantStatusLabel,
  participantStatusTone,
} from '../exams/examLabels'
import {
  canIssueLink,
  canUnassign,
  linkStateLabel,
  linkStateTone,
  participationLinkState,
  type CandidatePerms,
} from './CandidateHelpers'

interface Props {
  participation: CandidateParticipation
  candidateName: string
  perms: CandidatePerms
  /** A link was issued: the page shows it once and re-reads the card. */
  onIssued: (issued: IssuedInvitation) => void
  /** Something was written: the page toasts the sentence and re-reads the card. */
  onChanged: (message: string) => void
  onError: (message: string) => void
}

type Busy = 'issue' | 'revoke' | 'unassign' | null

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[8.5rem_1fr] gap-3 py-1.5">
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="break-words text-sm text-slate-700">{children}</dd>
    </div>
  )
}

export function CandidateParticipationPanel({
  participation: p,
  candidateName,
  perms,
  onIssued,
  onChanged,
  onError,
}: Props) {
  const [busy, setBusy] = useState<Busy>(null)

  const invitation = p.invitation
  const linkState = participationLinkState(p)
  const liveLink = invitation !== null && (invitation.state === 'issued' || invitation.state === 'opened')

  const showIssue = perms.invite && canIssueLink(p)
  const showRevoke = perms.invite && liveLink && (p.status === 'assigned' || p.status === 'in_progress')
  const showUnassign = perms.unassign && canUnassign(p.status)

  const issue = async () => {
    if (busy) return
    if (
      liveLink &&
      !confirm(
        `${candidateName} uchun yangi havola chiqarilsinmi?\n\nHozirgi havola (…${invitation.tokenHint}) darhol ishlamay qoladi.`,
      )
    ) {
      return
    }
    setBusy('issue')
    try {
      onIssued(await issueInvitation(p.participantId))
    } catch (err) {
      onError(candidateError(err, 'issue').message)
    } finally {
      setBusy(null)
    }
  }

  const revoke = async () => {
    if (busy || !invitation) return
    if (!confirm(`Havola (…${invitation.tokenHint}) bekor qilinsinmi? Nomzod u orqali kira olmaydi.`)) return
    setBusy('revoke')
    try {
      await revokeInvitation(p.participantId)
      onChanged('Havola bekor qilindi')
    } catch (err) {
      onError(candidateError(err, 'revoke').message)
    } finally {
      setBusy(null)
    }
  }

  const unassign = async () => {
    if (busy) return
    if (!confirm(`${candidateName} «${p.examTitle}» imtihonidan chiqarilsinmi?`)) return
    setBusy('unassign')
    try {
      await removeParticipant(p.examId, p.participantId)
      onChanged('Biriktirish bekor qilindi')
    } catch (err) {
      onError(candidateError(err, 'unassign').message)
    } finally {
      setBusy(null)
    }
  }

  const period =
    p.opensAt || p.closesAt
      ? `${formatWallClock(p.opensAt)} — ${formatWallClock(p.closesAt)}`
      : formatDay(p.examDate)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-slate-800" title={p.examTitle}>
            {p.examTitle}
          </p>
          <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs">
            <span className={cn('rounded-full px-2 py-0.5 font-medium', examStatusTone(p.examStatus))}>
              Imtihon: {examStatusLabel(p.examStatus)}
            </span>
            <span className={cn('rounded-full px-2 py-0.5 font-medium', participantStatusTone(p.status))}>
              {participantStatusLabel(p.status)}
            </span>
          </p>
        </div>
      </div>

      <dl className="divide-y divide-slate-50">
        <Field label="Sinf">{gradeLabel(p.grade)}</Field>
        <Field label="Muddat">{period}</Field>
        <Field label="Vaqt">{p.timeLimitMin ? `${p.timeLimitMin} daqiqa` : DASH}</Field>
        <Field label="Biriktirilgan">{formatWallClock(p.createdAt)}</Field>
      </dl>

      <div className="rounded-xl border border-slate-100 bg-slate-50/60 p-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="text-sm font-semibold text-slate-800">Havola</p>
          <p className={cn('text-sm font-medium', linkStateTone(linkState))}>{linkStateLabel(linkState)}</p>
        </div>
        {invitation ? (
          <dl className="mt-1 divide-y divide-slate-100/70">
            <Field label="Belgisi">
              <span className="font-mono text-xs">…{invitation.tokenHint}</span>
            </Field>
            <Field label="Amal qiladi">
              {formatWallClock(invitation.validFrom)} — {formatWallClock(invitation.validUntil)}
            </Field>
            <Field label="Chiqarilgan">{formatWallClock(invitation.issuedAt)}</Field>
            <Field label="Ochilgan">
              {invitation.firstOpenedAt ? formatWallClock(invitation.firstOpenedAt) : 'Hali ochilmagan'}
            </Field>
            {invitation.revokedAt && (
              <Field label="Bekor qilingan">{formatWallClock(invitation.revokedAt)}</Field>
            )}
          </dl>
        ) : (
          <p className="mt-1 text-sm text-slate-500">
            {p.delivery === 'online'
              ? "Havola hali chiqarilmagan. Chiqarilgan havola faqat bir marta ko'rsatiladi."
              : "Bu imtihon qog'ozda o'tkaziladi — havola kerak emas."}
          </p>
        )}

        {(showIssue || showRevoke) && (
          <div className="mt-3 flex flex-wrap gap-2">
            {showIssue && (
              <Button variant={liveLink ? 'secondary' : 'primary'} onClick={() => void issue()} disabled={busy !== null}>
                {busy === 'issue' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />}
                {invitation ? 'Havolani qayta chiqarish' : 'Havola chiqarish'}
              </Button>
            )}
            {showRevoke && (
              <Button variant="ghost" onClick={() => void revoke()} disabled={busy !== null}>
                {busy === 'revoke' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2Off className="h-4 w-4" />}
                Havolani bekor qilish
              </Button>
            )}
          </div>
        )}
      </div>

      {showUnassign && (
        <div className="flex justify-end">
          <Button
            variant="ghost"
            onClick={() => void unassign()}
            disabled={busy !== null}
            className="text-red-600 hover:bg-red-50 hover:text-red-700"
          >
            {busy === 'unassign' ? <Loader2 className="h-4 w-4 animate-spin" /> : <XCircle className="h-4 w-4" />}
            Biriktirishni bekor qilish
          </Button>
        </div>
      )}
    </div>
  )
}
