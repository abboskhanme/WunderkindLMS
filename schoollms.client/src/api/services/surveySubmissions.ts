/**
 * SURVEY SUBMISSIONS REGISTER — what parents typed into the public enrolment
 * form (`docs/modules/sales-marketing.md` §5.3, task SM-9).
 *
 * CONTRACT (server: `SchoolLms.Server/Controllers/SurveySubmissionsController.cs`,
 * `[Authorize] [AdminPerm("marketing")]`):
 *
 *   GET /api/admin/survey-submissions          ?surveyId&from&to&status&q&page&pageSize
 *                                              → { total, rows: SubmissionDto[] }
 *   GET /api/admin/survey-submissions/{id}     → SubmissionDetailDto (adds ip, userAgent,
 *                                                every raw value)
 *   GET /api/admin/survey-submissions/export   same filters → .xlsx (ExcelExport.cs)
 *
 * THE EXPORT IS SERVER-SIDE. The file arrives as an `.xlsx` blob built by
 * `ExcelExport.cs`; this module only saves it. It is NOT a CSV assembled in the
 * browser (`lib/utils.ts` `exportToCsv`), because the export covers the WHOLE
 * filter and the browser only holds one page of rows. `ip` and `userAgent` are
 * deliberately absent from that file (§4.2) — they exist for abuse triage and
 * are shown in the detail drawer only.
 *
 * THIS FILE DOES NOT TOUCH THE LEADS BOARD. A submission carries `leadId`, and
 * the page links to `/admin/leads`; `pages/admin/leads/*` is design-frozen
 * (`CLAUDE.md`) and nothing here imports from it.
 *
 * NO MOCK BRANCH. This register only exists against the real backend; a faked
 * list of submissions would hide a misconfigured API base URL, and there is
 * nothing to design against — the screen is built from the §5.3 contract.
 */
import { api } from '../client'

/** §4.2 `status` — `lead` created a lead, `duplicate` reused an existing one (§2.6 step 4). */
export type SubmissionStatus = 'lead' | 'duplicate'

/** §4.2 `student_gender` — two values, as on the board (Rule T, §2.4). */
export type SubmissionGender = 'male' | 'female'

/** One register row — §5.3 `SubmissionDto`. */
export interface Submission {
  id: string
  /** ISO-8601 with an offset (`timestamptz`, §4.2). */
  createdAt: string
  surveyId: string
  surveyName: string
  status: SubmissionStatus
  parentFullName: string
  parentPhone: string
  /** null when the survey does not ask for the pupil's name (§2.4). */
  studentFullName: string | null
  /** 0..11 — `0` is a real grade (nol sinf), never "unknown" (§2.1). */
  studentGrade: number | null
  studentGender: SubmissionGender | null
  studentPhone: string | null
  /** null when the lead was deleted from the board afterwards (§5.3). */
  leadId: string | null
  leadStageTitle: string | null
}

/**
 * The drawer row — §5.3 `SubmissionDetailDto`: the list row plus every raw
 * column of `survey_submissions` (§4.2) that the list folds together.
 */
export interface SubmissionDetail extends Submission {
  parentFirstName: string
  parentLastName: string | null
  studentFirstName: string | null
  studentLastName: string | null
  /** Only ever rendered in the drawer, never in the list or the export (§4.2). */
  ip: string | null
  userAgent: string | null
}

/** §5.3 query string. Everything is optional; filtering happens on the server. */
export interface SubmissionFilters {
  surveyId?: string
  /** "YYYY-MM-DD" — the same shape `DatePicker` produces. */
  from?: string
  to?: string
  status?: SubmissionStatus
  /** Free text over the parent name and the phone. */
  q?: string
  /** 1-based (§5.3). */
  page?: number
  /** Server default 50, capped at 200 (§5.3). */
  pageSize?: number
}

/** §5.3 list body. `page`/`pageSize` are not echoed back — the caller owns them. */
export interface SubmissionPage {
  total: number
  rows: Submission[]
}

/**
 * The survey dropdown of the filter bar.
 *
 * Read from `GET /api/admin/surveys` (§5.2) — the same `marketing` gate. Only
 * the three fields the filter needs are declared: the rest of `SurveyDto`
 * belongs to the survey register (SM-8) and is not this screen's business.
 */
export interface SurveyOption {
  id: string
  name: string
  isActive: boolean
}

const BASE = '/admin/survey-submissions'

/** Drops empty values so the query string carries only real filters. */
function clean(filters: SubmissionFilters): Record<string, string | number> {
  const out: Record<string, string | number> = {}
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || value === null || value === '') continue
    out[key] = value as string | number
  }
  return out
}

/** One page of the register. */
export async function listSubmissions(filters: SubmissionFilters = {}): Promise<SubmissionPage> {
  const { data } = await api.get<SubmissionPage>(BASE, { params: clean(filters) })
  return data
}

/** One submission with its raw values, ip and user agent — the drawer. */
export async function getSubmission(id: string): Promise<SubmissionDetail> {
  const { data } = await api.get<SubmissionDetail>(`${BASE}/${id}`)
  return data
}

/**
 * The whole filter as .xlsx, not the visible page — the same pattern as
 * `invoices.ts` `downloadInvoices` and `certificates.ts` `exportCertificates`:
 * blob in, file name from `Content-Disposition`, the download triggered by hand.
 */
export async function downloadSubmissions(filters: SubmissionFilters = {}): Promise<void> {
  const res = await api.get(`${BASE}/export`, {
    params: clean({ ...filters, page: undefined, pageSize: undefined }),
    responseType: 'blob',
  })

  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const match = cd.match(/filename="?([^"]+)"?/)
  a.download = match?.[1] ?? `topshirilgan-arizalar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/**
 * Surveys, for the filter dropdown — closed ones included, because their
 * submissions stay in the register forever.
 *
 * Called here rather than through the survey register's own service: SM-8 owns
 * `api/services/surveys.ts` and this screen must not depend on a file another
 * task is still writing.
 */
export async function listSurveyOptions(): Promise<SurveyOption[]> {
  const { data } = await api.get<SurveyOption[]>('/admin/surveys', {
    params: { includeInactive: true },
  })
  return data
}

/* ------------------------------------------------------------------ */
/*  Error reading                                                      */
/* ------------------------------------------------------------------ */

interface ApiErrorBody {
  code?: string
  message?: string
}

interface HttpErrorShape {
  response?: { status?: number; data?: ApiErrorBody | string | null }
  message?: string
}

function asHttpError(err: unknown): HttpErrorShape {
  return typeof err === 'object' && err !== null ? (err as HttpErrorShape) : {}
}

function body(err: unknown): ApiErrorBody | null {
  const data = asHttpError(err).response?.data
  return typeof data === 'object' && data !== null ? data : null
}

/**
 * Every machine code the module can answer with (§5.6), each with its own
 * Uzbek sentence. The submissions endpoints themselves only ever fail on the
 * transport or the gate, but the map is complete on purpose: a client that
 * branches on a code it does not know prints "Xatolik yuz berdi" and tells the
 * user nothing.
 */
const CODE_MESSAGES: Record<string, string> = {
  survey_not_found: 'Bu ariza topilmadi yoki yopilgan',
  validation: "Ma'lumotlarni tekshiring",
  slug_taken: 'Bu havola band — boshqa nom tanlang',
  survey_fields_required: "Jins va sinf maydonlarini o'chirib bo'lmaydi",
  survey_in_use: "Bu arizada topshirilgan so'rovlar bor — uni o'chirib bo'lmaydi",
  news_published: "Yangilik e'lon qilingan — auditoriyasini o'zgartirib bo'lmaydi",
  news_already_published: "Bu yangilik allaqachon e'lon qilingan",
}

/** The machine code, for logic that must not read the sentence. */
export function submissionsErrorCode(err: unknown): string | undefined {
  return body(err)?.code
}

/** The sentence shown to the user: the server's own when it sent one, ours otherwise. */
export function submissionsErrorMessage(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  const data = body(err)

  const serverMessage = data?.message
  if (serverMessage && serverMessage.trim()) return serverMessage

  const code = data?.code
  if (code && CODE_MESSAGES[code]) return CODE_MESSAGES[code]

  const status = asHttpError(err).response?.status
  if (status === undefined) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
  if (status === 401) return 'Sessiya tugagan — qaytadan kiring.'
  if (status === 403) return "Bu bo'limga ruxsatingiz yo'q."
  if (status === 404) return 'Bu ariza topilmadi — u o\'chirilgan bo\'lishi mumkin.'
  if (status === 429) return "Juda ko'p so'rov yuborildi. Birozdan so'ng qayta urinib ko'ring."
  if (status >= 500) return "Serverda xatolik. Keyinroq urinib ko'ring."

  return fallback
}
