/**
 * Words, paths and permission checks for the Nomzodlar screens
 * (`docs/modules/admission-and-testing.md` §3.1 screens 1–2, §3.7, §8.5; unit C2).
 *
 * The service (`api/services/candidates.ts`) carries the contract; this file
 * carries the Uzbek, so the register, the card and the modals never spell a
 * status two ways. Exam-side words (participant status, finish reason, score
 * and date formatting) are the Imtihonlar screens' own (`../exams/examLabels`)
 * and are re-used, not re-spelled — a candidate's "Baholangan" must read the
 * same as a pupil's.
 *
 * No components here: a file that exports both trips `react-refresh`.
 */
import type {
  AdmissionStatus,
  CandidateParticipation,
  CandidateRow,
  CandidateStatus,
  InvitationState,
} from '@/api/services/candidates'
import type { ParticipantStatus } from '@/api/services/exams'
import { hasPerm } from '../exams/examLabels'

// ---------------------------------------------------------------------------
//  Paths (§3.1)
// ---------------------------------------------------------------------------

export const CANDIDATES_PATH = '/admin/admission/candidates'

export function candidatePath(leadId: string): string {
  return `${CANDIDATES_PATH}/${leadId}`
}

// ---------------------------------------------------------------------------
//  Permissions (§3.7, §4.4) — a control that always 403s is not rendered
// ---------------------------------------------------------------------------

export interface CandidatePerms {
  /** Issue / re-issue / revoke a link, unlock a device, accept / reject — `admission`. */
  invite: boolean
  decide: boolean
  /**
   * Assign to an exam (`POST /exams/{id}/participants`): the endpoint is
   * `exams`, and an admission exam also needs `admission` (§6.3).
   */
  assign: boolean
  /** Cancel an assignment (`DELETE /exams/{id}/participants/{pid}`) — `exams`. */
  unassign: boolean
  /** Grade an abandoned attempt now (`force-finish`) — `exams`. */
  forceFinish: boolean
  /** Destroy the attempt (`reset-attempt`) — `exams` AND `admission` (§8.3). */
  reset: boolean
  /** "O'quvchi qilib ro'yxatga olish" — `admission` AND `students` (§3.7, §13 Q6). */
  enrol: boolean
  /** The Lidlar board — only linked for a user it would let in. */
  openLeads: boolean
}

/** Admin and superadmin carry no `permissions` list — `hasPerm` lets them through. */
export function candidatePerms(permissions: string[] | undefined): CandidatePerms {
  const admission = hasPerm(permissions, 'admission')
  const exams = hasPerm(permissions, 'exams')
  return {
    invite: admission,
    decide: admission,
    assign: admission && exams,
    unassign: admission && exams,
    forceFinish: admission && exams,
    reset: admission && exams,
    enrol: admission && hasPerm(permissions, 'students'),
    openLeads: hasPerm(permissions, 'leads'),
  }
}

// ---------------------------------------------------------------------------
//  Admission status (§8.5)
// ---------------------------------------------------------------------------

/** Filter order = lifecycle order. `none` is never a candidate; `enrolled` is never stored. */
export const CANDIDATE_STATUSES: readonly CandidateStatus[] = [
  'invited',
  'testing',
  'tested',
  'accepted',
  'rejected',
]

const STATUS_LABELS: Record<AdmissionStatus, string> = {
  none: 'Nomzod emas',
  invited: 'Taklif qilingan',
  testing: 'Test topshirmoqda',
  tested: 'Test topshirgan',
  accepted: 'Qabul qilingan',
  rejected: 'Rad etilgan',
}

const STATUS_TONES: Record<AdmissionStatus, string> = {
  none: 'bg-slate-100 text-slate-400',
  invited: 'bg-slate-100 text-slate-600',
  testing: 'bg-amber-50 text-amber-700',
  tested: 'bg-brand-50 text-brand-700',
  accepted: 'bg-emerald-50 text-emerald-700',
  rejected: 'bg-red-50 text-red-600',
}

/** An unknown value from a newer server still prints as something readable. */
export function admissionStatusLabel(status: AdmissionStatus): string {
  return STATUS_LABELS[status] ?? status
}

export function admissionStatusTone(status: AdmissionStatus): string {
  return STATUS_TONES[status] ?? 'bg-slate-100 text-slate-600'
}

// ---------------------------------------------------------------------------
//  Link state (§3.1: issued / opened / in progress / finished / revoked / expired)
// ---------------------------------------------------------------------------

/**
 * One word for "where is the link", as §3.1 lists it. The invitation row knows
 * issued / opened / revoked / expired; "in progress" and "finished" are the
 * participation's — once the candidate has started, that is the news, not
 * whether the link was opened.
 */
export type LinkState = InvitationState | 'in_progress' | 'finished'

export function linkStateOf(
  invitationState: InvitationState,
  participantStatus: ParticipantStatus | null,
): LinkState {
  if (participantStatus === 'in_progress') return 'in_progress'
  if (participantStatus === 'finished') return 'finished'
  return invitationState
}

export function rowLinkState(row: CandidateRow): LinkState {
  return linkStateOf(row.invitationState, row.participantStatus)
}

export function participationLinkState(p: CandidateParticipation): LinkState {
  return linkStateOf(p.invitation?.state ?? 'none', p.status)
}

const LINK_LABELS: Record<LinkState, string> = {
  none: "Havola yo'q",
  issued: 'Berilgan',
  opened: 'Ochilgan',
  in_progress: 'Topshirilmoqda',
  finished: 'Yakunlangan',
  revoked: 'Bekor qilingan',
  expired: "Muddati o'tgan",
}

const LINK_TONES: Record<LinkState, string> = {
  none: 'text-slate-400',
  issued: 'text-brand-700',
  opened: 'text-brand-700',
  in_progress: 'text-amber-700',
  finished: 'text-emerald-700',
  revoked: 'text-slate-400',
  expired: 'text-slate-400',
}

export function linkStateLabel(state: LinkState): string {
  return LINK_LABELS[state] ?? state
}

export function linkStateTone(state: LinkState): string {
  return LINK_TONES[state] ?? 'text-slate-500'
}

// ---------------------------------------------------------------------------
//  What a participation still allows — mirrors the server, never replaces it
// ---------------------------------------------------------------------------

/** A link makes sense only before the candidate has finished, on a live exam. */
export function canIssueLink(p: Pick<CandidateParticipation, 'status' | 'examStatus' | 'delivery'>): boolean {
  return (
    p.delivery === 'online' &&
    (p.examStatus === 'draft' || p.examStatus === 'published') &&
    (p.status === 'assigned' || p.status === 'in_progress')
  )
}

/**
 * §6.3: `DELETE participant` is refused once an attempt exists, so the control
 * is not offered for a sitting that has started or been graded.
 */
export function canUnassign(status: ParticipantStatus | null): boolean {
  return status !== null && status !== 'in_progress' && status !== 'finished'
}

/** Exams a candidate can still be added to (§5.5: not closed, not cancelled). */
export function isOpenExamStatus(status: string): boolean {
  return status === 'draft' || status === 'published'
}

// ---------------------------------------------------------------------------
//  Navigation state — a sentence carried to the list after a card action
// ---------------------------------------------------------------------------

export interface CandidateNavState {
  notice: string
}

export function noticeFromState(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('notice' in state)) return null
  return typeof state.notice === 'string' && state.notice ? state.notice : null
}

/** "Havola faqat hozir ko'rsatiladi…" — the sentence §7.2 requires next to the link. */
export const LINK_ONCE_WARNING =
  "Havola faqat hozir ko'rsatiladi. Yo'qotsangiz — qayta chiqaring, eskisi ishlamay qoladi."
