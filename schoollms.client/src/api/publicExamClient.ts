/**
 * Public entrance test — the four anonymous endpoints of
 * `docs/modules/admission-and-testing.md` §6.5, used only by `/qabul-test/:token`.
 *
 *   GET  /api/public/exam/state   ?token                        → PublicStateDto
 *   POST /api/public/exam/start   { token }                     → PublicPaperDto
 *   POST /api/public/exam/answer  { token, questionId, optionId } → { deadlineAt, serverNow, answeredCount }
 *   POST /api/public/exam/finish  { token }                     → PublicResultDto
 *
 * Decisions carried over from `api/services/publicSurvey.ts`, and why they matter
 * more here:
 *
 * 1. Its own axios instance, no interceptors (§7.8). The shared `api` client
 *    attaches the stored admin bearer token and, on a 401, wipes localStorage and
 *    sends the browser to /login. A candidate hitting that loses their paper; an
 *    admin testing on the same machine is logged out. This file must never import
 *    `api/client`.
 * 2. `withCredentials: true` — the attempt is bound to one browser by an HttpOnly
 *    cookie the server sets on `start` (§7.4). That cookie is the whole of device
 *    binding on the client: nothing is fingerprinted, nothing is written to
 *    localStorage, no header identifies the device.
 * 3. Calls resolve to a tagged result instead of throwing, because the page has to
 *    branch on the §7.5 `code` and a thrown error would flatten it.
 * 4. No mock branch — a fake paper would only hide a misconfigured base URL.
 *
 * Every response is projected by hand onto the types below. The server must never
 * send an answer key (§7.7), and if it ever did by mistake, the projection drops
 * every field it does not name — an `isCorrect` on an option never reaches React
 * state. The only place a correct option can appear is `PublicResult.questions`,
 * which the server fills solely when the school has switched
 * `AdmissionShowAnswersToCandidate` on (§7.7, §13 Q1).
 */
import axios from 'axios'

// ---------------------------------------------------------------------------
//  Types — §6.5
// ---------------------------------------------------------------------------

export type PublicExamStateName = 'lobby' | 'in_progress' | 'finished' | 'blocked' | 'not_assigned'

/** `exam_attempts.finish_reason`. */
export type PublicFinishReason = 'manual' | 'timer' | 'admin'

export interface PublicCandidate {
  fullName: string
  /** 0–11; 0 is a real grade (nol sinf). */
  grade: number | null
}

export interface PublicExamSubject {
  id: string
  name: string
}

/** What the lobby shows before anything is drawn. */
export interface PublicExamInfo {
  title: string
  subjects: PublicExamSubject[]
  questionCount: number
  timeLimitMin: number
}

/** `{ id, text }` and nothing else — §7.7. */
export interface PublicExamOption {
  id: string
  text: string
}

export interface PublicExamQuestion {
  id: string
  subjectId: string
  order: number
  text: string
  imageUrl: string | null
  options: PublicExamOption[]
  selectedOptionId: string | null
}

export interface PublicPaper {
  deadlineAt: string
  serverNow: string
  subjects: PublicExamSubject[]
  /** Sorted by `order`: consecutive per subject, in section order (§8.2). */
  questions: PublicExamQuestion[]
}

export interface PublicSubjectResult {
  subjectId: string
  name: string
  correct: number
  total: number
  points: number
  maxPoints: number
}

/**
 * One row of the optional per-question review. §6.5 leaves the item shape open
 * (`questions: [ … ] | null`); this is the paper question plus the one field
 * EduSchool's review carries (§7.1 fact 9). It only ever arrives when the school
 * has enabled the review; by default the server sends `null` and does not even
 * project the questions (§7.7).
 */
export interface PublicReviewQuestion extends PublicExamQuestion {
  correctOptionId: string | null
}

export interface PublicResult {
  correctCount: number
  totalCount: number
  totalPoints: number
  maxPoints: number
  percent: number
  finishReason: PublicFinishReason | null
  finishedAt: string | null
  perSubject: PublicSubjectResult[]
  /** null unless the school enabled the review (§7.7). */
  questions: PublicReviewQuestion[] | null
}

/**
 * `PublicStateDto`, narrowed per state: each variant carries exactly the part the
 * state promises, so the page never has to null-check `exam` in the lobby or
 * `paper` in progress. A response whose state and payload disagree is malformed.
 */
export type PublicExamState =
  | { state: 'lobby'; candidate: PublicCandidate | null; exam: PublicExamInfo }
  | { state: 'in_progress'; candidate: PublicCandidate | null; paper: PublicPaper }
  | { state: 'finished'; candidate: PublicCandidate | null; result: PublicResult }
  | { state: 'blocked'; candidate: PublicCandidate | null; deviceLabel: string | null }
  | { state: 'not_assigned'; candidate: PublicCandidate | null }

/** The `answer` response. `answeredCount` counts accepted calls (§5.10), not questions. */
export interface PublicAnswerAck {
  deadlineAt: string
  serverNow: string
  answeredCount: number
}

/**
 * `start` answers with the paper. §5.10 also lets a second `start` on a finished
 * attempt answer with the result, so both shapes are accepted.
 */
export type PublicStartOutcome =
  | { kind: 'paper'; paper: PublicPaper }
  | { kind: 'finished'; result: PublicResult }

/**
 * Every way a call can fail, already reduced to what the page does about it
 * (§7.5). `not_found` absorbs 404 and every other terminal code on purpose — the
 * page shows one message and cannot tell unknown, revoked, expired and cancelled
 * apart, because the server does not tell it either.
 */
export type PublicExamFailure =
  | { kind: 'not_found' }
  | { kind: 'blocked'; deviceLabel: string | null }
  | { kind: 'time_up' }
  | { kind: 'not_in_progress' }
  | { kind: 'rate_limited' }
  | { kind: 'unavailable'; reason: 'offline' | 'server' }

export type PublicExamCall<T> = { kind: 'ok'; data: T } | PublicExamFailure

// ---------------------------------------------------------------------------
//  Messages
// ---------------------------------------------------------------------------

/**
 * The single §7.5 sentence. Deliberately a constant, never the server's
 * `message`: even if a server bug ever produced different texts for "expired"
 * and "never existed", this page would still show one.
 */
export const LINK_NOT_FOUND_MESSAGE = "Havola topilmadi yoki muddati o'tgan"

const RATE_LIMITED_MESSAGE = "Juda ko'p so'rov yuborildi. Bir daqiqadan so'ng qayta urinib ko'ring."
const OFFLINE_MESSAGE = "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
const SERVER_MESSAGE = "Serverda xatolik. Birozdan so'ng qayta urinib ko'ring."

/** The Uzbek sentence for a failure that is worth retrying. */
export function retryableMessage(failure: { kind: 'rate_limited' } | { kind: 'unavailable'; reason: 'offline' | 'server' }): string {
  if (failure.kind === 'rate_limited') return RATE_LIMITED_MESSAGE
  return failure.reason === 'offline' ? OFFLINE_MESSAGE : SERVER_MESSAGE
}

// ---------------------------------------------------------------------------
//  Clock — §7.1 fact 4, §7.8
// ---------------------------------------------------------------------------

/**
 * The deadline on this browser's clock. The client clock is never trusted for
 * the absolute time, only for elapsed time: `Date.now() + (deadlineAt − serverNow)`
 * is computed once per server pair and re-computed on every `answer` response.
 */
export interface ExamClock {
  /** Local epoch ms at which the server's deadline falls. */
  deadlineMs: number
  /** Local epoch ms at which this pair was read. */
  syncedAt: number
}

export function examClock(pair: { deadlineAt: string; serverNow: string }): ExamClock {
  const syncedAt = Date.now()
  return {
    deadlineMs: syncedAt + (Date.parse(pair.deadlineAt) - Date.parse(pair.serverNow)),
    syncedAt,
  }
}

// ---------------------------------------------------------------------------
//  Transport
// ---------------------------------------------------------------------------

const examApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  withCredentials: true,
  // A hung `answer` must not hold `finish` hostage for minutes.
  timeout: 20_000,
  headers: { 'Content-Type': 'application/json' },
})

type JsonObject = Record<string, unknown>

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

function readNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

/** An ISO timestamp the browser can actually parse — the timer depends on it. */
function readTime(value: unknown): string | null {
  const text = readString(value)
  return text !== null && !Number.isNaN(Date.parse(text)) ? text : null
}

/** A list whose every item parses, or nothing. */
function readList<T>(value: unknown, read: (item: unknown) => T | null): T[] | null {
  if (!Array.isArray(value)) return null
  const items: T[] = []
  for (const raw of value) {
    const item = read(raw)
    if (item === null) return null
    items.push(item)
  }
  return items
}

/**
 * Question images are `/uploads/<guid>.<ext>` (§7.7). Anything that is not a
 * same-origin path or an http(s) URL is not rendered.
 */
function readImageUrl(value: unknown): string | null {
  const url = readString(value)?.trim()
  if (!url) return null
  if (url.startsWith('/') && !url.startsWith('//')) return url
  return /^https?:\/\//i.test(url) ? url : null
}

function readCandidate(value: unknown): PublicCandidate | null {
  if (!isObject(value)) return null
  const fullName = readString(value.fullName)
  if (fullName === null) return null
  const grade = readNumber(value.grade)
  return { fullName, grade: grade !== null && grade >= 0 && grade <= 11 ? grade : null }
}

function readSubject(value: unknown): PublicExamSubject | null {
  if (!isObject(value)) return null
  const id = readString(value.id)
  const name = readString(value.name)
  return id && name !== null ? { id, name } : null
}

function readExamInfo(value: unknown): PublicExamInfo | null {
  if (!isObject(value)) return null
  const title = readString(value.title)
  const subjects = readList(value.subjects, readSubject)
  const questionCount = readNumber(value.questionCount)
  const timeLimitMin = readNumber(value.timeLimitMin)
  if (title === null || subjects === null || questionCount === null || timeLimitMin === null) return null
  return { title, subjects, questionCount, timeLimitMin }
}

/** Projects `{ id, text }` by name; any other key on the wire is dropped here. */
function readOption(value: unknown): PublicExamOption | null {
  if (!isObject(value)) return null
  const id = readString(value.id)
  const text = readString(value.text)
  return id && text !== null ? { id, text } : null
}

function readQuestion(value: unknown): PublicExamQuestion | null {
  if (!isObject(value)) return null
  const id = readString(value.id)
  const subjectId = readString(value.subjectId)
  const order = readNumber(value.order)
  const text = readString(value.text)
  const options = readList(value.options, readOption)
  if (!id || !subjectId || order === null || text === null || options === null) return null
  const selected = readString(value.selectedOptionId)
  return {
    id,
    subjectId,
    order,
    text,
    imageUrl: readImageUrl(value.imageUrl),
    options,
    // A selection that is not one of this question's options is no selection.
    selectedOptionId: selected && options.some((o) => o.id === selected) ? selected : null,
  }
}

function byOrder<T extends { order: number }>(items: T[]): T[] {
  return [...items].sort((a, b) => a.order - b.order)
}

function readPaper(value: unknown): PublicPaper | null {
  if (!isObject(value)) return null
  const deadlineAt = readTime(value.deadlineAt)
  const serverNow = readTime(value.serverNow)
  const subjects = readList(value.subjects, readSubject)
  const questions = readList(value.questions, readQuestion)
  if (deadlineAt === null || serverNow === null || subjects === null || questions === null) return null
  return { deadlineAt, serverNow, subjects, questions: byOrder(questions) }
}

function readSubjectResult(value: unknown): PublicSubjectResult | null {
  if (!isObject(value)) return null
  const subjectId = readString(value.subjectId)
  const name = readString(value.name)
  const correct = readNumber(value.correct)
  const total = readNumber(value.total)
  const points = readNumber(value.points)
  const maxPoints = readNumber(value.maxPoints)
  if (!subjectId || name === null || correct === null || total === null || points === null || maxPoints === null) {
    return null
  }
  return { subjectId, name, correct, total, points, maxPoints }
}

function readReviewQuestion(value: unknown): PublicReviewQuestion | null {
  const question = readQuestion(value)
  if (question === null || !isObject(value)) return null
  const correct = readString(value.correctOptionId)
  return {
    ...question,
    correctOptionId: correct && question.options.some((o) => o.id === correct) ? correct : null,
  }
}

function readFinishReason(value: unknown): PublicFinishReason | null {
  return value === 'manual' || value === 'timer' || value === 'admin' ? value : null
}

function readResult(value: unknown): PublicResult | null {
  if (!isObject(value)) return null
  const correctCount = readNumber(value.correctCount)
  const totalCount = readNumber(value.totalCount)
  const totalPoints = readNumber(value.totalPoints)
  const maxPoints = readNumber(value.maxPoints)
  const percent = readNumber(value.percent)
  const perSubject = readList(value.perSubject, readSubjectResult)
  if (
    correctCount === null ||
    totalCount === null ||
    totalPoints === null ||
    maxPoints === null ||
    percent === null ||
    perSubject === null
  ) {
    return null
  }
  // The review is optional; a malformed one degrades to "score only" rather
  // than costing the candidate their result screen.
  const review = value.questions == null ? null : readList(value.questions, readReviewQuestion)
  return {
    correctCount,
    totalCount,
    totalPoints,
    maxPoints,
    percent,
    finishReason: readFinishReason(value.finishReason),
    finishedAt: readTime(value.finishedAt),
    perSubject,
    questions: review === null ? null : byOrder(review),
  }
}

/** Coarse UA label (§7.4.3) — shown as text, capped so it cannot flood the card. */
function readDeviceLabel(value: unknown): string | null {
  const label = readString(value)?.trim()
  return label ? label.slice(0, 80) : null
}

function readState(value: unknown): PublicExamState | null {
  if (!isObject(value)) return null
  const candidate = readCandidate(value.candidate)
  switch (value.state) {
    case 'lobby': {
      const exam = readExamInfo(value.exam)
      return exam ? { state: 'lobby', candidate, exam } : null
    }
    case 'in_progress': {
      const paper = readPaper(value.paper)
      return paper ? { state: 'in_progress', candidate, paper } : null
    }
    case 'finished': {
      const result = readResult(value.result)
      return result ? { state: 'finished', candidate, result } : null
    }
    case 'blocked':
      return { state: 'blocked', candidate, deviceLabel: readDeviceLabel(value.deviceLabel) }
    case 'not_assigned':
      return { state: 'not_assigned', candidate }
    default:
      return null
  }
}

function readAnswerAck(value: unknown): PublicAnswerAck | null {
  if (!isObject(value)) return null
  const deadlineAt = readTime(value.deadlineAt)
  const serverNow = readTime(value.serverNow)
  if (deadlineAt === null || serverNow === null) return null
  return { deadlineAt, serverNow, answeredCount: readNumber(value.answeredCount) ?? 0 }
}

function readStartOutcome(value: unknown): PublicStartOutcome | null {
  const paper = readPaper(value)
  if (paper) return { kind: 'paper', paper }
  const result = readResult(value)
  return result ? { kind: 'finished', result } : null
}

/**
 * §7.5 → what the page does. `time_up`, `not_in_progress` and `blocked` have
 * their own handling; 429 and 5xx are transient; every other status and code —
 * 404, `not_published`, `bad_question`, `bad_option`, anything unknown — is the
 * not-found card.
 */
function classify(err: unknown): PublicExamFailure {
  if (!axios.isAxiosError(err)) return { kind: 'unavailable', reason: 'server' }
  const response = err.response
  if (!response) return { kind: 'unavailable', reason: 'offline' }
  const { status } = response
  if (status === 429) return { kind: 'rate_limited' }
  if (status >= 500) return { kind: 'unavailable', reason: 'server' }
  const body: unknown = response.data
  const code = isObject(body) ? body.code : undefined
  if (status === 409) {
    if (code === 'blocked') {
      return { kind: 'blocked', deviceLabel: isObject(body) ? readDeviceLabel(body.deviceLabel) : null }
    }
    if (code === 'time_up') return { kind: 'time_up' }
    if (code === 'not_in_progress') return { kind: 'not_in_progress' }
  }
  return { kind: 'not_found' }
}

const MALFORMED: PublicExamFailure = { kind: 'unavailable', reason: 'server' }

async function call<T>(request: () => Promise<{ data: unknown }>, read: (data: unknown) => T | null): Promise<PublicExamCall<T>> {
  try {
    const { data } = await request()
    const parsed = read(data)
    return parsed === null ? MALFORMED : { kind: 'ok', data: parsed }
  } catch (err) {
    return classify(err)
  }
}

// ---------------------------------------------------------------------------
//  The four calls — and only these four (§7.1)
// ---------------------------------------------------------------------------

/** `GET /api/public/exam/state?token=` */
export function fetchExamState(token: string): Promise<PublicExamCall<PublicExamState>> {
  return call(() => examApi.get<unknown>('/public/exam/state', { params: { token } }), readState)
}

/** `POST /api/public/exam/start` — idempotent on the server (§7.3). */
export function startExam(token: string): Promise<PublicExamCall<PublicStartOutcome>> {
  return call(() => examApi.post<unknown>('/public/exam/start', { token }), readStartOutcome)
}

/**
 * `POST /api/public/exam/answer`. The body is these three keys and nothing else
 * (§7.6): every other value is derived on the server from the token.
 */
export function saveExamAnswer(
  token: string,
  questionId: string,
  optionId: string,
): Promise<PublicExamCall<PublicAnswerAck>> {
  return call(() => examApi.post<unknown>('/public/exam/answer', { token, questionId, optionId }), readAnswerAck)
}

/** `POST /api/public/exam/finish` — idempotent: a finished attempt returns its result (§7.3). */
export function finishExam(token: string): Promise<PublicExamCall<PublicResult>> {
  return call(() => examApi.post<unknown>('/public/exam/finish', { token }), readResult)
}
