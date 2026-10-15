/**
 * The public poll page `/sorovnoma/:token` — its two anonymous endpoints
 * (docs/modules/surveys-forms.md):
 *
 *   GET  /api/public/polls/{token}   — what to render (`accepting: false` → closed)
 *   POST /api/public/polls/{token}   — one answer sheet
 *
 * Built like `publicSurvey.ts`, for the same reasons: its own axios instance (no
 * session header, no 401 redirect to /login on a page opened from a Telegram post),
 * and tagged results instead of throws, because the page branches on the server's
 * error `code` (poll_closed, already_answered, validation).
 */
import axios from 'axios'
import type { PollQuestion } from './polls'

export interface PublicPoll {
  title: string
  description: string | null
  /** false — closed or not opened yet; the page says so and shows nothing else. */
  accepting: boolean
  anonymous: boolean
  /** Ask for a name and a phone before the questions. */
  collectContact: boolean
  closesOn: string | null
  /** School questions only — per-person ones stay in the Mini App. */
  questions: PollQuestion[]
}

/** One answer, the server's `PollAnswerInput`. */
export interface PollAnswerInput {
  questionId: string
  subjectId: null
  score: number | null
  choice: string | null
  text: string | null
  choices: string[] | null
  number: number | null
  date: string | null
}

export interface PublicPollSubmission {
  answers: PollAnswerInput[]
  respondentToken: string
  contactName?: string
  contactPhone?: string
}

export type PublicPollLoad =
  | { kind: 'ok'; poll: PublicPoll }
  | { kind: 'not_found'; message: string }
  | { kind: 'rate_limited'; message: string }
  | { kind: 'failed'; message: string }

export type PublicPollSubmitResult =
  | { kind: 'ok' }
  | { kind: 'closed'; message: string }
  | { kind: 'answered'; message: string }
  | { kind: 'not_found'; message: string }
  | { kind: 'validation'; message: string }
  | { kind: 'rate_limited'; message: string }
  | { kind: 'failed'; message: string }

const NOT_FOUND_MESSAGE = "So'rovnoma topilmadi"
const RATE_LIMITED_MESSAGE = "Juda ko'p urinish bo'ldi. Iltimos, birozdan so'ng qayta urinib ko'ring."
const OFFLINE_MESSAGE = "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
const SERVER_MESSAGE = "Serverda xatolik. Iltimos, birozdan so'ng qayta urinib ko'ring."

const publicApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
})

interface ErrorBody {
  code?: string
  message?: string
}

function bodyOf(err: unknown): ErrorBody {
  if (!axios.isAxiosError(err)) return {}
  const data: unknown = err.response?.data
  return typeof data === 'object' && data !== null ? (data as ErrorBody) : {}
}

function messageOf(err: unknown, fallback: string): string {
  const message = bodyOf(err).message
  return typeof message === 'string' && message.trim() ? message : fallback
}

function failure(err: unknown): { kind: 'failed'; message: string } {
  const status = axios.isAxiosError(err) ? err.response?.status : undefined
  return { kind: 'failed', message: status === undefined ? OFFLINE_MESSAGE : messageOf(err, SERVER_MESSAGE) }
}

export async function loadPublicPoll(token: string): Promise<PublicPollLoad> {
  try {
    const { data } = await publicApi.get<PublicPoll>(`/public/polls/${encodeURIComponent(token)}`)
    return { kind: 'ok', poll: data }
  } catch (err) {
    const status = axios.isAxiosError(err) ? err.response?.status : undefined
    if (status === 404) return { kind: 'not_found', message: messageOf(err, NOT_FOUND_MESSAGE) }
    if (status === 429) return { kind: 'rate_limited', message: RATE_LIMITED_MESSAGE }
    return failure(err)
  }
}

export async function submitPublicPoll(token: string, body: PublicPollSubmission): Promise<PublicPollSubmitResult> {
  try {
    await publicApi.post(`/public/polls/${encodeURIComponent(token)}`, body)
    return { kind: 'ok' }
  } catch (err) {
    const status = axios.isAxiosError(err) ? err.response?.status : undefined
    const code = bodyOf(err).code
    if (status === 404) return { kind: 'not_found', message: messageOf(err, NOT_FOUND_MESSAGE) }
    if (status === 429) return { kind: 'rate_limited', message: RATE_LIMITED_MESSAGE }
    if (code === 'poll_closed') return { kind: 'closed', message: messageOf(err, "So'rovnoma yopilgan") }
    if (code === 'already_answered') return { kind: 'answered', message: messageOf(err, 'Siz bu so\'rovnomaga javob bergansiz') }
    if (status === 400) return { kind: 'validation', message: messageOf(err, "Javoblarni tekshiring") }
    return failure(err)
  }
}

const RESPONDENT_KEY = 'poll-respondent'
const answeredKey = (token: string) => `poll-answered:${token}`

/**
 * This browser's random id — one answer per browser. The server stores only its hash
 * (with the poll id), so the same id on two polls links nothing. Without localStorage
 * (private mode on some phones) a fresh id per page load still works, just without the guard.
 */
export function respondentToken(): string {
  try {
    const saved = localStorage.getItem(RESPONDENT_KEY)
    if (saved && /^[A-Za-z0-9_-]{16,64}$/.test(saved)) return saved
    const fresh = crypto.randomUUID()
    localStorage.setItem(RESPONDENT_KEY, fresh)
    return fresh
  } catch {
    return crypto.randomUUID()
  }
}

/** The page remembers it was answered here, so a reopened link shows "already answered" at once. */
export function wasAnswered(token: string): boolean {
  try {
    return localStorage.getItem(answeredKey(token)) === '1'
  } catch {
    return false
  }
}

export function markAnswered(token: string): void {
  try {
    localStorage.setItem(answeredKey(token), '1')
  } catch {
    /* private mode — the server still refuses a second answer from this browser's id */
  }
}
