/**
 * NOMZODLAR — admission candidates
 * (`docs/modules/admission-and-testing.md` §2.2, §3.1 screens 1–2, §6.1, §6.4,
 * §8.5; unit C2).
 *
 * A CANDIDATE IS A LEAD (§2.2). There is no candidates table: a candidate is a
 * `leads` row whose `admission_status <> 'none'`, joined to its latest
 * `exam_participants` row. Enrolling one (`POST /api/admin/leads/{id}/enrol`,
 * already built, used through `LeadEnrolModal`) DELETES the lead — so
 * `enrolled` is never a stored status and an enrolled candidate simply stops
 * appearing here (2026-09-22 note in §2.2 and §6.1).
 *
 * CONTRACT (server: `LeadsController`, additive endpoints, perm `admission`,
 * reads not gated — §4.4). Built later by unit B5; this file is the client
 * side of §6.1 and must not drift from it:
 *
 *   GET   /api/admin/leads/candidates        page,limit,search,admissionStatus,grade,examId
 *                                            → Paged<CandidateRowDto>
 *   GET   /api/admin/leads/{id}/admission    → CandidateCardDto
 *   PATCH /api/admin/leads/{id}/admission-status  { status }  → 204
 *
 * Invitations (§6.4, perm `admission`) — the admin side of the public token.
 * Their only screen is the candidate card, so they live here, not in
 * `exams.ts`:
 *
 *   POST   /api/admin/exams/participants/{pid}/invitation     {validFrom?,validUntil?}
 *          → { url, tokenHint, validFrom, validUntil }   — the ONLY time `url` exists
 *   DELETE /api/admin/exams/participants/{pid}/invitation     → 204 (revoke)
 *   POST   /api/admin/exams/participants/{pid}/unlock-device  { reason } → 204
 *
 * Everything else a candidate needs is the exams API, reused, not copied:
 * assigning (`addParticipants` with `leadIds`), cancelling an assignment
 * (`removeParticipant`), the result and per-question review
 * (`getAttemptReview`), `force-finish` and `reset-attempt` — all in
 * `api/services/exams.ts`, whose types this file imports.
 *
 * WHAT §6.1 NAMES BUT DOES NOT SHAPE. `CandidateRowDto` is spelled out and is
 * reproduced verbatim below. `CandidateCardDto` is only named; its shape here
 * is the row (the card is the row, opened) plus the lead fields the left pane
 * shows (§3.1 screen 2: "lead data, read-only") plus every participation of
 * this lead, newest first, each with its latest invitation. The attempt's
 * device, IP and the per-subject / per-question result are NOT repeated on the
 * card: they come from `AttemptReviewDto` (§6.3), one source for one fact.
 *
 * NO MOCK BRANCH — like `exams.ts`: fake candidates would hide a misconfigured
 * API address.
 */
import axios from 'axios'
import type { Gender } from '@/types'
import { api } from '../client'
import type {
  ExamDelivery,
  ExamStatus,
  Paged,
  ParticipantStatus,
} from './exams'

// ---------------------------------------------------------------------------
//  Enumerations (§2.2, §6.1, §8.5)
// ---------------------------------------------------------------------------

/**
 * `leads.admission_status` as it can be stored. The spec's `enrolled` is left
 * out on purpose: enrolment deletes the lead, and the database CHECK rejects
 * the value (`LeadAdmissionStatus` in `SchoolLms.Domain/Exams.cs`).
 */
export type AdmissionStatus = 'none' | 'invited' | 'testing' | 'tested' | 'accepted' | 'rejected'

/** The statuses a candidate row can carry — the list is `admission_status <> 'none'`. */
export type CandidateStatus = Exclude<AdmissionStatus, 'none'>

/**
 * What `PATCH /admission-status` is used for from the card: the two human
 * decisions (§8.5 — "never automatic"). `invited`, `testing` and `tested` are
 * set by the engine; `enrolled` does not exist as a stored value.
 */
export type AdmissionDecision = 'accepted' | 'rejected'

/** `CandidateRowDto.invitationState` (§6.1). */
export type InvitationState = 'none' | 'issued' | 'opened' | 'revoked' | 'expired'

// ---------------------------------------------------------------------------
//  Rows and card
// ---------------------------------------------------------------------------

/** `CandidateRowDto`, verbatim from §6.1. The exam fields describe the LATEST participation. */
export interface CandidateRow {
  leadId: string
  fullName: string
  parentPhone: string
  /** 0–11 — `leads.target_grade`. */
  targetGrade: number
  admissionStatus: AdmissionStatus
  /** Latest participation; `null` when the lead has none. */
  examId: string | null
  examTitle: string | null
  participantStatus: ParticipantStatus | null
  invitationState: InvitationState
  /** `null` until the participation is scored. */
  totalPoints: number | null
  maxPoints: number | null
  percent: number | null
  /**
   * In §6.1 "null until enrolled". Since 2026-09-22 an enrolled lead is
   * deleted, so no row this endpoint returns can carry it; kept for contract
   * fidelity and never read.
   */
  studentId: string | null
}

/** The latest invitation row of one participation (§5.9) — never the token itself. */
export interface CandidateInvitation {
  state: Exclude<InvitationState, 'none'>
  /** Last 6 characters of the token, for support ("…7fQ2xA") — §7.2. */
  tokenHint: string
  /** `yyyy-MM-ddTHH:mm:ss`, Tashkent wall clock, no offset (§6). */
  validFrom: string
  validUntil: string
  issuedAt: string
  firstOpenedAt: string | null
  revokedAt: string | null
}

/** One `exam_participants` row of this lead, with its exam and latest invitation. */
export interface CandidateParticipation {
  participantId: string
  examId: string
  examTitle: string
  examStatus: ExamStatus
  delivery: ExamDelivery
  /** The exam's grade (admission exams carry one, §5.5). */
  grade: number | null
  examDate: string | null
  opensAt: string | null
  closesAt: string | null
  timeLimitMin: number | null
  status: ParticipantStatus
  totalPoints: number | null
  maxPoints: number | null
  percent: number | null
  scoredAt: string | null
  createdAt: string
  /** `null` when no link was ever issued for this participation. */
  invitation: CandidateInvitation | null
}

/**
 * `CandidateCardDto` — the row plus the read-only lead data and the whole
 * participation history. `participations[0]` is the participation the row
 * summarises.
 */
export interface CandidateCard extends CandidateRow {
  gender: Gender
  /** `YYYY-MM-DD`, may be empty for a lead created with no birth date. */
  birthDate: string
  parentFullName: string
  note: string | null
  /** `lead_stages.id` — the board column the lead sits in. */
  stage: string
  stageName: string | null
  participations: CandidateParticipation[]
}

export interface CandidateListQuery {
  page?: number
  limit?: number
  search?: string
  admissionStatus?: CandidateStatus
  grade?: number
  examId?: string
}

/** §6.4 — returned by the issue call, and by nothing else, ever. */
export interface IssuedInvitation {
  url: string
  tokenHint: string
  validFrom: string
  validUntil: string
}

export interface InvitationWindow {
  /** Defaults to the exam's `opens_at` / `closes_at` on the server. */
  validFrom?: string
  validUntil?: string
}

// ---------------------------------------------------------------------------
//  Requests
// ---------------------------------------------------------------------------

const LEADS = '/admin/leads'
const PARTICIPANTS = '/admin/exams/participants'

/** §6: `limit ≤ 200`, default 50. */
export const CANDIDATE_PAGE_LIMIT = 50

/** Drops empty values so the query string carries only real filters. */
function clean<T extends object>(query: T): Record<string, string | number> {
  const out: Record<string, string | number> = {}
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (typeof value === 'string' || typeof value === 'number') out[key] = value
  }
  return out
}

export async function listCandidates(query: CandidateListQuery = {}): Promise<Paged<CandidateRow>> {
  const { data } = await api.get<Paged<CandidateRow>>(`${LEADS}/candidates`, {
    params: clean({
      ...query,
      page: query.page ?? 1,
      limit: query.limit ?? CANDIDATE_PAGE_LIMIT,
      search: query.search?.trim(),
    }),
  })
  return data
}

export async function getCandidate(leadId: string): Promise<CandidateCard> {
  const { data } = await api.get<CandidateCard>(`${LEADS}/${leadId}/admission`)
  return data
}

/** The human decision (§8.5). The server enforces which moves are legal. */
export async function setAdmissionStatus(leadId: string, status: AdmissionDecision): Promise<void> {
  await api.patch(`${LEADS}/${leadId}/admission-status`, { status })
}

/**
 * Issue a link — or re-issue, which revokes the live one (§7.2). The URL in
 * the answer is shown once and never again: the server keeps only its hash.
 */
export async function issueInvitation(
  participantId: string,
  validity: InvitationWindow = {},
): Promise<IssuedInvitation> {
  const { data } = await api.post<IssuedInvitation>(
    `${PARTICIPANTS}/${participantId}/invitation`,
    clean(validity),
  )
  return data
}

export async function revokeInvitation(participantId: string): Promise<void> {
  await api.delete(`${PARTICIPANTS}/${participantId}/invitation`)
}

/** Clears the device lock and keeps the saved answers (§7.4 point 5). Not `reset-attempt`. */
export async function unlockDevice(participantId: string, reason: string): Promise<void> {
  await api.post(`${PARTICIPANTS}/${participantId}/unlock-device`, { reason })
}

/**
 * The participation a row's exam refers to. §6.1's row carries `examId` but
 * no participant id, so a row action that needs one (re-issue the link,
 * cancel the assignment) reads the card first — one extra GET, and the row
 * DTO stays exactly as the contract spells it.
 */
export async function resolveParticipation(
  leadId: string,
  examId: string,
): Promise<CandidateParticipation | null> {
  const card = await getCandidate(leadId)
  return card.participations.find((p) => p.examId === examId) ?? null
}

// ---------------------------------------------------------------------------
//  Errors — one reader for every call in this module
// ---------------------------------------------------------------------------

/** Which call failed — it decides the sentence for a bare status. */
export type CandidateAction =
  | 'list'
  | 'open'
  | 'decide'
  | 'assign'
  | 'unassign'
  | 'issue'
  | 'revoke'
  | 'unlock'
  | 'forceFinish'
  | 'reset'
  | 'review'
  | 'lookup'
  | 'enrolLookup'

export type CandidateErrorKind =
  | 'validation'
  | 'session'
  | 'forbidden'
  | 'not_found'
  | 'conflict'
  | 'rate_limited'
  | 'offline'
  | 'server'

export interface CandidateErrorInfo {
  kind: CandidateErrorKind
  message: string
}

const FALLBACK: Record<CandidateAction, string> = {
  list: "Nomzodlarni yuklab bo'lmadi",
  open: "Nomzod kartochkasini ochib bo'lmadi",
  decide: "Qarorni saqlab bo'lmadi",
  assign: "Imtihonga biriktirib bo'lmadi",
  unassign: "Biriktirishni bekor qilib bo'lmadi",
  issue: "Havolani chiqarib bo'lmadi",
  revoke: "Havolani bekor qilib bo'lmadi",
  unlock: "Qurilma qulfini ochib bo'lmadi",
  forceFinish: "Urinishni yakunlab bo'lmadi",
  reset: "Urinishni bekor qilib bo'lmadi",
  review: "Natijani yuklab bo'lmadi",
  lookup: "Ma'lumotnomalarni yuklab bo'lmadi",
  enrolLookup: "Lid ma'lumotlarini yuklab bo'lmadi",
}

const CANDIDATE_GONE =
  "Nomzod topilmadi — u o'quvchiga aylantirilgan yoki lidlar doskasidan o'chirilgan bo'lishi mumkin"

const NOT_FOUND: Partial<Record<CandidateAction, string>> = {
  open: CANDIDATE_GONE,
  decide: CANDIDATE_GONE,
  assign: 'Imtihon yoki nomzod topilmadi — sahifani yangilang',
  unassign: "Bu biriktirish allaqachon bekor qilingan",
  issue: 'Biriktirish topilmadi — sahifani yangilang',
  revoke: "Faol havola yo'q — u allaqachon bekor qilingan yoki muddati o'tgan",
  unlock: 'Urinish topilmadi — sahifani yangilang',
  review: "Natija topilmadi — biriktirish bekor qilingan bo'lishi mumkin",
  enrolLookup: CANDIDATE_GONE,
}

const CONFLICT: Partial<Record<CandidateAction, string>> = {
  decide: "Nomzodning holati bu qarorga mos emas — sahifani yangilang",
  assign: "Bekor qilingan yoki yopilgan imtihonga nomzod biriktirib bo'lmaydi",
  unassign: "Nomzod testni boshlagan — biriktirishni bekor qilib bo'lmaydi",
  issue: "Bu biriktirish uchun havola chiqarib bo'lmaydi: imtihon bekor qilingan, yopilgan yoki nomzod testni tugatgan",
  unlock: "Urinish yakunlangan — qurilma qulfi endi ahamiyatsiz",
  forceFinish: 'Urinish allaqachon yakunlangan yoki hali boshlanmagan',
  reset: "Bu biriktirishda bekor qilinadigan urinish yo'q",
}

/** Which permission a 403 on this action is about — §3.7, §4.4. */
const FORBIDDEN: Partial<Record<CandidateAction, string>> = {
  assign: "Imtihonga biriktirish uchun «Qabul» va «Imtihonlar» ruxsatlari kerak",
  unassign: "Biriktirishni bekor qilish uchun «Imtihonlar» ruxsati kerak",
  forceFinish: "Urinishni yakunlash uchun «Imtihonlar» ruxsati kerak",
  reset: "Urinishni bekor qilish uchun «Imtihonlar» va «Qabul» ruxsatlari birga kerak",
}

interface ApiErrorBody {
  message?: unknown
}

function kindOf(err: unknown): CandidateErrorKind {
  if (!axios.isAxiosError(err)) return 'server'
  const status = err.response?.status
  if (status === undefined) return 'offline'
  if (status === 400 || status === 422) return 'validation'
  if (status === 401) return 'session'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 409) return 'conflict'
  if (status === 429) return 'rate_limited'
  return 'server'
}

function serverMessage(err: unknown): string | null {
  if (!axios.isAxiosError(err)) return null
  const data: unknown = err.response?.data
  if (typeof data !== 'object' || data === null) return null
  const message = (data as ApiErrorBody).message
  return typeof message === 'string' && message.trim() ? message.trim() : null
}

function fallbackFor(kind: CandidateErrorKind, action: CandidateAction): string {
  switch (kind) {
    case 'validation':
      return "Ma'lumotlarni tekshiring"
    case 'session':
      return 'Sessiya tugagan — qaytadan kiring'
    case 'forbidden':
      return FORBIDDEN[action] ?? "Bu amal uchun «Qabul» ruxsati kerak"
    case 'not_found':
      return NOT_FOUND[action] ?? FALLBACK[action]
    case 'conflict':
      return CONFLICT[action] ?? "Ma'lumot boshqa joyda o'zgargan — sahifani yangilang"
    case 'rate_limited':
      return "Juda ko'p so'rov yuborildi. Birozdan so'ng qayta urinib ko'ring"
    case 'offline':
      return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring"
    case 'server':
      return FALLBACK[action]
  }
}

/**
 * Any failure → one Uzbek sentence. The server's own `message` wins (§6
 * "Errors": `{ message: "<Uzbek text>" }`) — it knows the precise reason.
 */
export function candidateError(err: unknown, action: CandidateAction): CandidateErrorInfo {
  const kind = kindOf(err)
  return { kind, message: serverMessage(err) ?? fallbackFor(kind, action) }
}
