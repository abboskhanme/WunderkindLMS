/**
 * MAVSUMIY BAHOLASH — seasonal assessment (monthly / quarterly / yearly, 0–100).
 * Contract: `docs/modules/admission-and-testing.md` §6.6. This file codes
 * against that table and nothing else.
 *
 *   GET    /api/admin/seasonal-marks                  paged SeasonalMarkRowDto
 *   GET    /api/admin/seasonal-marks/scope            ScopeDto (bare)
 *   GET    /api/admin/seasonal-marks/students         SeasonalEntryRowDto[] (bare)
 *   POST   /api/admin/seasonal-marks/bulk             { created, updated, deleted }
 *   PUT    /api/admin/seasonal-marks/{id}             SeasonalMarkRowDto
 *   DELETE /api/admin/seasonal-marks/{id}             204
 *   GET    /api/admin/seasonal-marks/export           .xlsx
 *   GET    /api/admin/seasonal-marks/by-subjects      paged pivot
 *   GET    /api/admin/seasonal-marks/by-subjects/export .xlsx
 *   GET    /api/admin/seasonal-marks/coverage         paged CoverageRowDto
 *   GET    /api/admin/seasonal-marks/coverage/detail  paged pupil rows
 *   GET    /api/admin/seasonal-marks/coverage/export  .xlsx
 *   GET    /api/teacher/seasonal-marks/scope          ScopeDto — own pairs only
 *   GET    /api/teacher/seasonal-marks/students       SeasonalEntryRowDto[]
 *   POST   /api/teacher/seasonal-marks/bulk           { created, updated, deleted }
 *
 * Admin routes sit behind `[AdminPerm("seasonalMarks")]`; teacher routes behind
 * the teacher permission of the same name plus "teaches that (class, subject)".
 *
 * SCALE. The score is 0–100 with two decimals (§5.12, §13 Q5). It is never
 * converted to or from the 1–5 journal scale anywhere in this module.
 *
 * NUMBERS COME FROM THE SERVER. Coverage counts, percentages, the pivot and
 * `periodLabel` are all computed server-side; this module only carries them.
 *
 * ARRAY PARAMETERS (`classIds`, `subjectIds`, `teacherIds`) are sent as
 * repeated keys — `classIds=a&classIds=b` — which ASP.NET Core binds to a
 * `string[]` parameter without any custom parsing.
 *
 * NO MOCK BRANCH. Like `surveySubmissions.ts`, this module only exists against
 * the real backend; a faked list would hide a misconfigured API base URL.
 */
import axios from 'axios'
import { api } from '../client'

// ---------------------------------------------------------------------------
//  Shapes — §6.6
// ---------------------------------------------------------------------------

/** §5.12 `period_kind`. */
export type PeriodKind = 'monthly' | 'quarterly' | 'yearly'

/**
 * A period as the server keys it: `(periodKind, year, month | quarter)`.
 * `month` is sent only with `monthly`, `quarter` only with `quarterly`.
 */
export interface PeriodQuery {
  periodKind: PeriodKind
  year: number
  /** 1–12, `monthly` only. */
  month?: number
  /** 1–4, `quarterly` only. A number, never a `quarters.id` (§5.12). */
  quarter?: number
}

/** §6 pagination envelope, shared by every new paged endpoint. */
export interface Paged<T> {
  items: T[]
  total: number
  page: number
  limit: number
}

/** One stored mark — the list row (§6.6 `SeasonalMarkRowDto`). */
export interface SeasonalMarkRowDto {
  id: string
  student: { id: string; fullName: string }
  /** The class snapshot taken at entry time (§8.6), not the pupil's class today. */
  class: { id: string; name: string }
  subject: { id: string; name: string }
  periodKind: PeriodKind
  year: number
  month: number | null
  quarter: number | null
  /** Server-formatted Uzbek label: "Mart 2026" | "2 - chorak 2026" | "2026". */
  periodLabel: string
  /** 0–100, two decimals. null = comment-only mark. */
  score: number | null
  comment: string | null
  /** "yyyy-MM-ddTHH:mm:ss", Tashkent wall clock, no offset (§6). */
  updatedAt: string
  createdByName: string | null
}

export interface ScopeClassDto {
  id: string
  name: string
  grade: number
}

/** One (class, subject, teacher) triple taken from the schedule. */
export interface ScopePairDto {
  classId: string
  subjectId: string
  subjectName: string
  teacherId: string
  teacherFullName: string
}

/** §6.6 `ScopeDto` — which subjects are taught in which class, and by whom. */
export interface ScopeDto {
  classes: ScopeClassDto[]
  pairs: ScopePairDto[]
}

/** One pupil in the bulk-entry grid (§6.6 `SeasonalEntryRowDto`). */
export interface SeasonalEntryRowDto {
  studentId: string
  fullName: string
  score: number | null
  comment: string | null
  /** null = nothing stored yet for this pupil and period. */
  markId: string | null
}

/** Query of `/students` — the grid for one (class, subject, period). */
export interface SeasonalStudentsQuery extends PeriodQuery {
  classId: string
  subjectId: string
}

/**
 * One row of `POST /bulk`. Score AND comment both null deletes the stored
 * mark for that key — that is how a mark is taken back (§6.6).
 */
export interface SeasonalBulkRow {
  studentId: string
  score: number | null
  comment: string | null
}

export interface SeasonalBulkRequest extends SeasonalStudentsQuery {
  rows: SeasonalBulkRow[]
}

export interface SeasonalBulkResult {
  created: number
  updated: number
  deleted: number
}

/**
 * `PUT /{id}` body. The page always sends BOTH fields with the full intended
 * state, so the call is correct whether the server reads an absent field as
 * "unchanged" or as "clear".
 */
export interface SeasonalMarkUpdate {
  score: number | null
  comment: string | null
}

/** Query of the list and of its export. Everything optional. */
export interface SeasonalListFilters {
  search?: string
  periodKind?: PeriodKind
  year?: number
  month?: number
  quarter?: number
  classId?: string
  subjectId?: string
  studentId?: string
  page?: number
  /** Server default 50, capped at 200 (§6). */
  limit?: number
}

/** `/by-subjects` query. The screen only queries once all four are chosen. */
export interface PivotQuery extends PeriodQuery {
  classIds: string[]
  subjectIds: string[]
  page?: number
  limit?: number
}

export interface PivotColumn {
  subjectId: string
  name: string
}

export interface PivotRow {
  studentId: string
  fullName: string
  className: string
  /** subjectId → score. A missing key or null means "not marked", never 0. */
  scores: Record<string, number | null>
}

/** §6.6 pivot response — the paged envelope plus the column descriptions. */
export interface PivotPage extends Paged<PivotRow> {
  columns: PivotColumn[]
}

export interface CoverageQuery extends PeriodQuery {
  /** Empty or absent = every teacher. */
  teacherIds?: string[]
  page?: number
  limit?: number
}

/**
 * §6.6 `CoverageRowDto`. `percent` is the server's
 * `min(marked / totalStudents × 100, 100)`, already rounded to a whole number.
 */
export interface CoverageRowDto {
  teacherId: string
  fullName: string
  totalStudents: number
  marked: number
  unmarked: number
  percent: number
}

export interface CoverageDetailQuery extends PeriodQuery {
  teacherId: string
  /** true = only marked, false = only unmarked, absent = everyone. */
  hasMark?: boolean
  page?: number
  limit?: number
}

/**
 * One row of the coverage drill-down. §6.6 only says "paged pupil rows"; this
 * is the minimal shape the screen needs — one pupil in one (class, subject)
 * pair the teacher teaches, with the mark if there is one.
 */
export interface CoverageDetailRowDto {
  studentId: string
  fullName: string
  className: string
  subjectId: string
  subjectName: string
  hasMark: boolean
  score: number | null
}

// ---------------------------------------------------------------------------
//  Query-string building
// ---------------------------------------------------------------------------

type ParamValue = string | number | boolean | string[] | null | undefined

/** Drops empty values; arrays become repeated keys. */
function toParams(values: Record<string, ParamValue>): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) {
      for (const item of value) if (item) params.append(key, item)
      continue
    }
    params.set(key, String(value))
  }
  return params
}

/** Never lets a `month` travel with `quarterly` or a `quarter` with `monthly`. */
function periodParams(q: PeriodQuery): Record<string, ParamValue> {
  return {
    periodKind: q.periodKind,
    year: q.year,
    month: q.periodKind === 'monthly' ? q.month : undefined,
    quarter: q.periodKind === 'quarterly' ? q.quarter : undefined,
  }
}

function listParams(f: SeasonalListFilters): Record<string, ParamValue> {
  return {
    search: f.search?.trim(),
    periodKind: f.periodKind,
    year: f.year,
    // A month or quarter filter only means something under its own kind.
    month: f.periodKind === 'monthly' ? f.month : undefined,
    quarter: f.periodKind === 'quarterly' ? f.quarter : undefined,
    classId: f.classId,
    subjectId: f.subjectId,
    studentId: f.studentId,
  }
}

function bulkBody(body: SeasonalBulkRequest): SeasonalBulkRequest {
  return {
    classId: body.classId,
    subjectId: body.subjectId,
    periodKind: body.periodKind,
    year: body.year,
    month: body.periodKind === 'monthly' ? body.month : undefined,
    quarter: body.periodKind === 'quarterly' ? body.quarter : undefined,
    rows: body.rows,
  }
}

// ---------------------------------------------------------------------------
//  Excel downloads
// ---------------------------------------------------------------------------

/** "yyyy-MM-dd" in local time — `toISOString()` would give yesterday before 05:00 in Tashkent. */
function localDate(): string {
  const d = new Date()
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

/**
 * With `responseType: 'blob'` an error body arrives as a Blob too. Turn a JSON
 * error body back into an object so `seasonalError` can read its `message`.
 */
async function unwrapBlobError(err: unknown): Promise<unknown> {
  if (axios.isAxiosError(err) && err.response && err.response.data instanceof Blob) {
    try {
      err.response.data = JSON.parse(await err.response.data.text()) as unknown
    } catch {
      err.response.data = null
    }
  }
  return err
}

/**
 * The whole filter as `.xlsx`, built by the server (`ExcelExport.Build`), never
 * assembled in the browser — the browser only holds one page of rows.
 * File name from `Content-Disposition`, falling back to the §6.3 convention.
 */
async function downloadXlsx(path: string, params: URLSearchParams, fallbackName: string): Promise<void> {
  let blob: Blob
  let disposition: string
  try {
    const res = await api.get<Blob>(path, { params, responseType: 'blob' })
    blob = res.data
    disposition = String(res.headers['content-disposition'] ?? '')
  } catch (err: unknown) {
    throw await unwrapBlobError(err)
  }

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  const match = disposition.match(/filename="?([^";]+)"?/)
  a.download = match?.[1] ?? fallbackName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

// ---------------------------------------------------------------------------
//  Admin endpoints
// ---------------------------------------------------------------------------

const ADMIN = '/admin/seasonal-marks'
const TEACHER = '/teacher/seasonal-marks'

/** One page of stored marks. */
export async function listSeasonalMarks(
  filters: SeasonalListFilters = {},
): Promise<Paged<SeasonalMarkRowDto>> {
  const { data } = await api.get<Paged<SeasonalMarkRowDto>>(ADMIN, {
    params: toParams({ ...listParams(filters), page: filters.page, limit: filters.limit }),
  })
  return data
}

/** Classes and the (class, subject, teacher) pairs of the current schedule. */
export async function getSeasonalScope(classId?: string): Promise<ScopeDto> {
  const { data } = await api.get<ScopeDto>(`${ADMIN}/scope`, {
    params: toParams({ classId }),
  })
  return data
}

/** Every pupil of the class with the stored mark, if any, prefilled. */
export async function getSeasonalStudents(q: SeasonalStudentsQuery): Promise<SeasonalEntryRowDto[]> {
  const { data } = await api.get<SeasonalEntryRowDto[]>(`${ADMIN}/students`, {
    params: toParams({ classId: q.classId, subjectId: q.subjectId, ...periodParams(q) }),
  })
  return data
}

/** One request writes the whole grid — upsert per row, empty row deletes. */
export async function saveSeasonalBulk(body: SeasonalBulkRequest): Promise<SeasonalBulkResult> {
  const { data } = await api.post<SeasonalBulkResult>(`${ADMIN}/bulk`, bulkBody(body))
  return data
}

/** Inline edit of one list row. Returns the row as the server now has it. */
export async function updateSeasonalMark(
  id: string,
  patch: SeasonalMarkUpdate,
): Promise<SeasonalMarkRowDto> {
  const { data } = await api.put<SeasonalMarkRowDto>(`${ADMIN}/${id}`, patch)
  return data
}

export async function deleteSeasonalMark(id: string): Promise<void> {
  await api.delete(`${ADMIN}/${id}`)
}

/** The whole list filter as .xlsx — not the visible page. */
export async function downloadSeasonalMarks(filters: SeasonalListFilters = {}): Promise<void> {
  await downloadXlsx(
    `${ADMIN}/export`,
    toParams(listParams(filters)),
    `mavsumiy_baholash_${localDate()}.xlsx`,
  )
}

function pivotParams(q: PivotQuery): Record<string, ParamValue> {
  return { ...periodParams(q), classIds: q.classIds, subjectIds: q.subjectIds }
}

/** Pupils × selected subjects for one period. */
export async function getBySubjects(q: PivotQuery): Promise<PivotPage> {
  const { data } = await api.get<PivotPage>(`${ADMIN}/by-subjects`, {
    params: toParams({ ...pivotParams(q), page: q.page, limit: q.limit }),
  })
  return data
}

export async function downloadBySubjects(q: PivotQuery): Promise<void> {
  await downloadXlsx(
    `${ADMIN}/by-subjects/export`,
    toParams(pivotParams(q)),
    `mavsumiy_baholash_fanlar_${localDate()}.xlsx`,
  )
}

function coverageParams(q: CoverageQuery): Record<string, ParamValue> {
  return { ...periodParams(q), teacherIds: q.teacherIds }
}

/** Per-teacher coverage for one period. */
export async function getCoverage(q: CoverageQuery): Promise<Paged<CoverageRowDto>> {
  const { data } = await api.get<Paged<CoverageRowDto>>(`${ADMIN}/coverage`, {
    params: toParams({ ...coverageParams(q), page: q.page, limit: q.limit }),
  })
  return data
}

/** The pupils behind one coverage number. */
export async function getCoverageDetail(
  q: CoverageDetailQuery,
): Promise<Paged<CoverageDetailRowDto>> {
  const { data } = await api.get<Paged<CoverageDetailRowDto>>(`${ADMIN}/coverage/detail`, {
    params: toParams({
      teacherId: q.teacherId,
      ...periodParams(q),
      hasMark: q.hasMark,
      page: q.page,
      limit: q.limit,
    }),
  })
  return data
}

export async function downloadCoverage(q: CoverageQuery): Promise<void> {
  await downloadXlsx(
    `${ADMIN}/coverage/export`,
    toParams(coverageParams(q)),
    `mavsumiy_baholash_hisoboti_${localDate()}.xlsx`,
  )
}

// ---------------------------------------------------------------------------
//  Teacher endpoints — own (class, subject) pairs only
// ---------------------------------------------------------------------------

export async function getTeacherSeasonalScope(): Promise<ScopeDto> {
  const { data } = await api.get<ScopeDto>(`${TEACHER}/scope`)
  return data
}

export async function getTeacherSeasonalStudents(
  q: SeasonalStudentsQuery,
): Promise<SeasonalEntryRowDto[]> {
  const { data } = await api.get<SeasonalEntryRowDto[]>(`${TEACHER}/students`, {
    params: toParams({ classId: q.classId, subjectId: q.subjectId, ...periodParams(q) }),
  })
  return data
}

export async function saveTeacherSeasonalBulk(body: SeasonalBulkRequest): Promise<SeasonalBulkResult> {
  const { data } = await api.post<SeasonalBulkResult>(`${TEACHER}/bulk`, bulkBody(body))
  return data
}

/**
 * The three calls the bulk-entry workspace needs. The admin screen and the
 * teacher panel (unit C5) render the same workspace with a different surface.
 */
export interface SeasonalEntryApi {
  /** Admin: every class. Teacher: only the pairs the caller teaches. */
  scope: () => Promise<ScopeDto>
  students: (q: SeasonalStudentsQuery) => Promise<SeasonalEntryRowDto[]>
  bulk: (body: SeasonalBulkRequest) => Promise<SeasonalBulkResult>
}

export const adminSeasonalEntryApi: SeasonalEntryApi = {
  scope: () => getSeasonalScope(),
  students: getSeasonalStudents,
  bulk: saveSeasonalBulk,
}

export const teacherSeasonalEntryApi: SeasonalEntryApi = {
  scope: getTeacherSeasonalScope,
  students: getTeacherSeasonalStudents,
  bulk: saveTeacherSeasonalBulk,
}

// ---------------------------------------------------------------------------
//  Errors — one place, every outcome in Uzbek
// ---------------------------------------------------------------------------

/**
 * Every way a §6.6 call can fail. Admin endpoints answer `{ message }` with a
 * status (§6); the kind is read from a `code` if the server ever sends one,
 * otherwise from the status.
 */
export type SeasonalErrorKind =
  | 'validation'
  | 'quarter_not_configured'
  | 'session'
  | 'forbidden'
  | 'not_found'
  | 'conflict'
  | 'rate_limited'
  | 'offline'
  | 'server'

export interface SeasonalErrorInfo {
  kind: SeasonalErrorKind
  message: string
}

const FALLBACK_MESSAGES: Record<SeasonalErrorKind, string> = {
  validation:
    "Ma'lumotlarni tekshiring: ball 0 dan 100 gacha, izoh esa kamida 3 ta belgidan iborat bo'lsin.",
  quarter_not_configured:
    "Bunday chorak sozlanmagan. Avval Sozlamalar → Choraklar bo'limida chorak sanalarini kiriting.",
  session: 'Sessiya tugagan — qaytadan kiring.',
  forbidden: "Bu amal uchun “Mavsumiy baholash” ruxsati yo'q.",
  not_found: "Yozuv topilmadi — u o'chirilgan bo'lishi mumkin. Ro'yxatni yangilang.",
  conflict:
    "Bu baho shu orada boshqa foydalanuvchi tomonidan o'zgartirildi. Ro'yxatni yangilab, qayta urinib ko'ring.",
  rate_limited: "Juda ko'p so'rov yuborildi. Birozdan so'ng qayta urinib ko'ring.",
  offline: "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring.",
  server: "Serverda xatolik. Birozdan so'ng qayta urinib ko'ring.",
}

const KNOWN_KINDS: readonly SeasonalErrorKind[] = [
  'validation',
  'quarter_not_configured',
  'session',
  'forbidden',
  'not_found',
  'conflict',
  'rate_limited',
  'offline',
  'server',
]

interface ApiErrorBody {
  code?: unknown
  message?: unknown
}

function bodyOf(err: unknown): ApiErrorBody {
  if (!axios.isAxiosError(err)) return {}
  const data: unknown = err.response?.data
  return typeof data === 'object' && data !== null && !(data instanceof Blob)
    ? (data as ApiErrorBody)
    : {}
}

/** §8.6: the server refuses an unconfigured quarter with exactly this sentence. */
const QUARTER_SENTENCE = 'chorak sozlanmagan'

function kindOf(err: unknown, body: ApiErrorBody, message: string | null): SeasonalErrorKind {
  const code = typeof body.code === 'string' ? body.code : ''
  const known = KNOWN_KINDS.find((k) => k === code)
  if (known) return known
  if (message && message.toLowerCase().includes(QUARTER_SENTENCE)) return 'quarter_not_configured'
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

/**
 * Any failure → one Uzbek sentence. The server's own `message` wins when it
 * sent one (it is Uzbek by contract and more precise); ASP.NET's English
 * validation `title` is never shown.
 */
export function seasonalError(err: unknown): SeasonalErrorInfo {
  const body = bodyOf(err)
  const serverMessage =
    typeof body.message === 'string' && body.message.trim() ? body.message.trim() : null
  const kind = kindOf(err, body, serverMessage)
  // The quarter case carries a hint about where to fix it; ours is more useful.
  const message =
    kind === 'quarter_not_configured' || !serverMessage ? FALLBACK_MESSAGES[kind] : serverMessage
  return { kind, message }
}

export function seasonalErrorMessage(err: unknown): string {
  return seasonalError(err).message
}
