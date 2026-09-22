/**
 * Public enrolment form — the two anonymous endpoints of
 * `docs/modules/sales-marketing.md` §5.1 (task SM-7).
 *
 *   GET  /api/public/surveys/{slug}   — what to render
 *   POST /api/public/surveys/{slug}   — the submission
 *
 * Three things here are deliberate:
 *
 * 1. Its own axios instance. The shared `api` client (`api/client.ts`) attaches
 *    the Authorization header from localStorage and, on a 401, wipes the session
 *    and redirects to /login. Neither belongs on a page a parent opens from a
 *    Telegram link: the page carries no session (§6.1), and a stale token must
 *    never be able to throw a half-filled form away.
 * 2. No mock branch. This page has one job in production and a fake survey
 *    would only hide a misconfigured API base URL.
 * 3. The calls resolve with a tagged result instead of throwing. The page has to
 *    branch on the §5.6 error *code* (404 survey_not_found, 400 validation,
 *    429), and `useAsync` flattens a thrown error down to its message string,
 *    which would lose exactly that.
 */
import axios from 'axios'

/** Which pupil fields this survey asks for — §5.1 `fields`, §2.4 the toggles. */
export interface PublicSurveyFields {
  studentFirstName: boolean
  studentLastName: boolean
  studentPhone: boolean
  /** Always true — Rule T pins it (§2.4). */
  studentGrade: boolean
  /** Always true — Rule T pins it (§2.4). */
  studentGender: boolean
}

/** The §5.1 GET body. Everything the page renders comes from here. */
export interface PublicSurvey {
  slug: string
  name: string
  subtitle: string | null
  imageUrl: string | null
  offerUrl: string | null
  /** null → the page's own default thank-you text. */
  thankYouText: string | null
  fields: PublicSurveyFields
  /** Server clock, ISO-8601. Echoed back on submit (§2.5). */
  servedAt: string
}

export type StudentGender = 'male' | 'female'

/** The §5.1 POST body. Optional keys are omitted when empty or switched off. */
export interface PublicSurveySubmission {
  parentFirstName: string
  parentLastName?: string
  parentPhone: string
  studentFirstName?: string
  studentLastName?: string
  studentPhone?: string
  studentGrade?: number
  studentGender?: StudentGender
  servedAt: string
  /** Honeypot (§2.5) — a human leaves it empty. */
  website: string
}

/** Every form field the server may report an error against (§5.1 `errors`). */
export type SurveyFieldName =
  | 'parentFirstName'
  | 'parentLastName'
  | 'parentPhone'
  | 'studentFirstName'
  | 'studentLastName'
  | 'studentPhone'
  | 'studentGrade'
  | 'studentGender'

export type SurveyFieldErrors = Partial<Record<SurveyFieldName, string>>

/** Result of the read call. `not_found` covers "unknown slug" and "closed" alike (§5.1). */
export type PublicSurveyLoad =
  | { kind: 'ok'; survey: PublicSurvey }
  | { kind: 'not_found'; message: string }
  | { kind: 'rate_limited'; message: string }
  | { kind: 'failed'; message: string }

/** Result of the submit call. The server answers 200 identically for a real
 *  submission, a duplicate, a honeypot hit and a time-trap hit (§5.1) — so does this. */
export type PublicSurveySubmitResult =
  | { kind: 'ok'; thankYou: string | null }
  | { kind: 'validation'; message: string; errors: SurveyFieldErrors }
  | { kind: 'not_found'; message: string }
  | { kind: 'rate_limited'; message: string }
  | { kind: 'failed'; message: string }

const NOT_FOUND_MESSAGE = 'Bu ariza topilmadi yoki yopilgan'
const RATE_LIMITED_MESSAGE =
  "Juda ko'p urinish bo'ldi. Iltimos, 10 daqiqadan so'ng qayta urinib ko'ring."
const VALIDATION_MESSAGE = "Ma'lumotlarni tekshiring"
const OFFLINE_MESSAGE = "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
const SERVER_MESSAGE = "Serverda xatolik. Iltimos, birozdan so'ng qayta urinib ko'ring."

const publicApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
})

interface ApiErrorBody {
  code?: string
  message?: string
  errors?: Record<string, unknown>
}

function statusOf(err: unknown): number | undefined {
  return axios.isAxiosError(err) ? err.response?.status : undefined
}

function bodyOf(err: unknown): ApiErrorBody {
  if (!axios.isAxiosError(err)) return {}
  const data: unknown = err.response?.data
  return typeof data === 'object' && data !== null ? (data as ApiErrorBody) : {}
}

/** The server's own Uzbek sentence when it sent one, our fallback otherwise. */
function messageOf(err: unknown, fallback: string): string {
  const message = bodyOf(err).message
  return typeof message === 'string' && message.trim() ? message : fallback
}

function failure(err: unknown): { kind: 'failed'; message: string } {
  const status = statusOf(err)
  if (status === undefined) return { kind: 'failed', message: OFFLINE_MESSAGE }
  return { kind: 'failed', message: messageOf(err, SERVER_MESSAGE) }
}

const FIELD_NAMES: readonly SurveyFieldName[] = [
  'parentFirstName',
  'parentLastName',
  'parentPhone',
  'studentFirstName',
  'studentLastName',
  'studentPhone',
  'studentGrade',
  'studentGender',
]

/** Keep only the keys this form actually renders; ignore anything else the server sends. */
function readFieldErrors(raw: Record<string, unknown> | undefined): SurveyFieldErrors {
  const errors: SurveyFieldErrors = {}
  if (!raw) return errors
  for (const name of FIELD_NAMES) {
    const value = raw[name]
    if (typeof value === 'string' && value.trim()) errors[name] = value
  }
  return errors
}

/** `GET /api/public/surveys/{slug}` (§5.1). */
export async function loadPublicSurvey(slug: string): Promise<PublicSurveyLoad> {
  if (!slug.trim()) return { kind: 'not_found', message: NOT_FOUND_MESSAGE }
  try {
    const { data } = await publicApi.get<PublicSurvey>(
      `/public/surveys/${encodeURIComponent(slug)}`,
    )
    return { kind: 'ok', survey: data }
  } catch (err) {
    const status = statusOf(err)
    if (status === 404) return { kind: 'not_found', message: messageOf(err, NOT_FOUND_MESSAGE) }
    if (status === 429) return { kind: 'rate_limited', message: RATE_LIMITED_MESSAGE }
    return failure(err)
  }
}

/** `POST /api/public/surveys/{slug}` (§5.1). */
export async function submitPublicSurvey(
  slug: string,
  payload: PublicSurveySubmission,
): Promise<PublicSurveySubmitResult> {
  try {
    const { data } = await publicApi.post<{ ok: boolean; thankYou?: string }>(
      `/public/surveys/${encodeURIComponent(slug)}`,
      payload,
    )
    const thankYou = typeof data.thankYou === 'string' && data.thankYou.trim() ? data.thankYou : null
    return { kind: 'ok', thankYou }
  } catch (err) {
    const status = statusOf(err)
    if (status === 404) return { kind: 'not_found', message: messageOf(err, NOT_FOUND_MESSAGE) }
    if (status === 429) return { kind: 'rate_limited', message: RATE_LIMITED_MESSAGE }
    if (status === 400) {
      const body = bodyOf(err)
      return {
        kind: 'validation',
        message: messageOf(err, VALIDATION_MESSAGE),
        errors: readFieldErrors(body.errors),
      }
    }
    return failure(err)
  }
}
