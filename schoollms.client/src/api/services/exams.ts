/**
 * IMTIHONLAR — exams, exam types, manual result entry and the cross-exam
 * result register (`docs/modules/admission-and-testing.md` §6.3, unit C3).
 *
 * CONTRACT (server: `ExamsController`, `ExamTypesController`,
 * `ExamResultsController`, all `[Authorize] [AdminPerm("exams")]`, reads not
 * gated — §4.4):
 *
 *   GET    /api/admin/exams/types                              → ExamType[]
 *   POST   /api/admin/exams/types                  {name,description}
 *   PUT    /api/admin/exams/types/{id}             {name,description,isActive}
 *   DELETE /api/admin/exams/types/{id}                         → 204 | 409 (in use)
 *   GET    /api/admin/exams             page,limit,search,kind,status,examTypeId,from,to
 *   POST   /api/admin/exams                        ExamUpsert  → Exam
 *   GET    /api/admin/exams/{id}                               → Exam (+ sections, counts)
 *   PUT    /api/admin/exams/{id}                   ExamUpsert  → Exam | 409 (sections frozen)
 *   POST   /api/admin/exams/{id}/publish                       → Exam | 409 (§8.1 reason)
 *   POST   /api/admin/exams/{id}/cancel            {reason}    → Exam
 *   GET    /api/admin/exams/{id}/participants      page,limit,search,status
 *   POST   /api/admin/exams/{id}/participants      {leadIds?,studentIds?,classIds?}
 *   DELETE /api/admin/exams/{id}/participants/{pid}            → 204 | 409 (attempt exists)
 *   GET    /api/admin/exams/{id}/entry-table                   → EntryTable | 409 (> 500 rows)
 *   POST   /api/admin/exams/{id}/entry-table       {rows}      → {saved} | 400 (out of range)
 *   GET    /api/admin/exams/{id}/entry-table/template          → natijalar_shablon.xlsx
 *   POST   /api/admin/exams/{id}/entry-table/import  multipart file, dryRun
 *   GET    /api/admin/exams/results     page,limit,search,examId,classId,subjectId,status
 *   GET    /api/admin/exams/results/export         same query  → .xlsx
 *   GET    /api/admin/exams/participants/{pid}/review          → AttemptReview
 *   POST   /api/admin/exams/participants/{pid}/force-finish {reason}
 *   POST   /api/admin/exams/participants/{pid}/reset-attempt {reason} (exams AND admission)
 *
 * WHAT THE SPEC NAMES BUT DOES NOT SHAPE. §6.3 spells out `ExamUpsertDto`,
 * `EntryTableDto` and `AttemptReviewDto`; `ExamTypeDto`, `ExamRowDto`,
 * `ExamDto`, `ParticipantRowDto`, `ResultRowDto` and `ResultImportResultDto`
 * are only named. Their shapes below are the §5.4–§5.8 columns in camelCase
 * (the server's JSON naming everywhere else) plus the joined display names a
 * list needs. The count fields the spec calls "counts" are optional: a missing
 * count renders as a dash rather than a wrong zero.
 *
 * NO GRADING HERE. Every total, maximum and percent is the server's
 * (§8.3/§8.4). The browser validates that a typed cell is a number inside
 * `[0, maxScore]` — input hygiene — and never adds points up.
 *
 * NO MOCK BRANCH. Like the submissions register, this module exists only
 * against the real backend; fake exams would hide a misconfigured API URL.
 */
import axios from 'axios'
import { api } from '../client'

// ---------------------------------------------------------------------------
//  Enumerations (§5.5, §5.7, §5.10)
// ---------------------------------------------------------------------------

/** `exams.kind` — immutable after create. */
export type ExamKind = 'admission' | 'block'

/** `exams.delivery` — immutable after create. Block test is `manual` (§13 Q4). */
export type ExamDelivery = 'online' | 'manual'

/** `exams.status` — `draft → published → closed`, `draft|published → cancelled`. */
export type ExamStatus = 'draft' | 'published' | 'closed' | 'cancelled'

export type ParticipantKind = 'lead' | 'student'

/** `exam_participants.status`. */
export type ParticipantStatus = 'assigned' | 'in_progress' | 'finished' | 'absent' | 'cancelled'

/** `exam_attempts.finish_reason`. */
export type FinishReason = 'manual' | 'timer' | 'admin'

// ---------------------------------------------------------------------------
//  Paging envelope (§6, "Pagination envelope")
// ---------------------------------------------------------------------------

/** `{ items, total, page, limit }` — the one envelope every new paged list uses. */
export interface Paged<T> {
  items: T[]
  total: number
  page: number
  limit: number
}

/** §6: `limit ≤ 200`, default 50. */
export const PAGE_LIMIT_MAX = 200
export const PAGE_LIMIT_DEFAULT = 50

// ---------------------------------------------------------------------------
//  Exam types (§5.4)
// ---------------------------------------------------------------------------

/** `ExamTypeDto` — the §5.4 row. */
export interface ExamType {
  id: string
  name: string
  description: string
  isActive: boolean
  /** `yyyy-MM-ddTHH:mm:ss`, Tashkent wall clock, no offset (§6). */
  createdAt: string
}

export interface ExamTypeCreate {
  name: string
  description: string
}

export interface ExamTypeUpdate extends ExamTypeCreate {
  isActive: boolean
}

// ---------------------------------------------------------------------------
//  Exams (§5.5, §5.6)
// ---------------------------------------------------------------------------

/** `ExamRowDto` — one line of the exam register. */
export interface ExamRow {
  id: string
  title: string
  kind: ExamKind
  delivery: ExamDelivery
  status: ExamStatus
  examTypeId: string | null
  examTypeName: string | null
  /** 0–11, admission only (§5.5). */
  grade: number | null
  /** `YYYY-MM-DD`, manual exams. */
  examDate: string | null
  /** Online window, `yyyy-MM-ddTHH:mm:ss` without offset. */
  opensAt: string | null
  closesAt: string | null
  timeLimitMin: number | null
  createdAt: string
  sectionCount?: number
  participantCount?: number
  finishedCount?: number
  absentCount?: number
}

/** One `exam_sections` row — one subject of the sitting. */
export interface ExamSection {
  id: string
  subjectId: string
  subjectName: string
  /** Online only. */
  bankId: string | null
  questionCount: number | null
  /** Online: copied from the bank at publish. */
  pointsPerCorrect: number | null
  /** Manual: the entry-grid ceiling. Online: written at publish. */
  maxScore: number | null
  order: number
}

/** `ExamDto` — the row plus its sections. */
export interface Exam extends ExamRow {
  sections: ExamSection[]
}

/** One section of `ExamUpsertDto`. */
export interface ExamSectionInput {
  subjectId: string
  bankId?: string | null
  questionCount?: number | null
  maxScore?: number | null
  order: number
}

/** `ExamUpsertDto`, verbatim from §6.3. */
export interface ExamUpsert {
  title: string
  kind: ExamKind
  delivery: ExamDelivery
  examTypeId?: string | null
  grade?: number | null
  examDate?: string | null
  opensAt?: string | null
  closesAt?: string | null
  timeLimitMin?: number | null
  sections: ExamSectionInput[]
}

export interface ExamListQuery {
  page?: number
  limit?: number
  search?: string
  kind?: ExamKind
  status?: ExamStatus
  examTypeId?: string
  /** `YYYY-MM-DD`. */
  from?: string
  to?: string
}

// ---------------------------------------------------------------------------
//  Participants (§5.7)
// ---------------------------------------------------------------------------

/** `ParticipantRowDto`. */
export interface ParticipantRow {
  id: string
  participantKind: ParticipantKind
  leadId: string | null
  studentId: string | null
  fullName: string
  classId: string | null
  className: string | null
  status: ParticipantStatus
  totalPoints: number | null
  maxPoints: number | null
  percent: number | null
  scoredAt: string | null
}

export interface ParticipantListQuery {
  page?: number
  limit?: number
  search?: string
  status?: ParticipantStatus
}

export interface AddParticipantsRequest {
  leadIds?: string[]
  studentIds?: string[]
  /** Expands to every non-archived pupil of those classes; idempotent. */
  classIds?: string[]
}

export interface AddParticipantsResult {
  added: number
  skipped: number
}

// ---------------------------------------------------------------------------
//  Entry table (§6.3 `EntryTableDto`, §8.4)
// ---------------------------------------------------------------------------

export interface EntryColumn {
  sectionId: string
  subjectId: string
  name: string
  maxScore: number
}

export interface EntryRow {
  participantId: string
  fullName: string
  className: string | null
  status: ParticipantStatus
  /** `sectionId → points`, `null` when nothing has been entered yet. */
  scores: Record<string, number | null>
  totalPoints: number | null
  maxPoints: number | null
  percent: number | null
}

export interface EntryTable {
  examId: string
  title: string
  examDate: string | null
  columns: EntryColumn[]
  rows: EntryRow[]
}

export interface EntrySaveScore {
  sectionId: string
  points: number
}

export interface EntrySaveRow {
  participantId: string
  scores: EntrySaveScore[]
  /** `true` sets the participant `absent` and clears their scores (§8.4). */
  absent: boolean
}

export interface EntrySaveResult {
  saved: number
}

/**
 * One problem in an imported sheet. Shaped like `QuestionImportResultDto`
 * (§6.2) — the only import report the spec spells out — so both import
 * dialogs read the same way: one chip per reason with the offending rows.
 */
export interface ResultImportError {
  reason: string
  /** A server-written Uzbek sentence, when it sends one. */
  message?: string | null
  /** 1-based sheet rows, header included — what the user sees in Excel. */
  rows: number[]
}

/** `ResultImportResultDto`. */
export interface ResultImportResult {
  fileName: string
  totalRows: number
  validCount: number
  errorCount: number
  /** 0 on a dry run. */
  imported: number
  errors: ResultImportError[]
}

// ---------------------------------------------------------------------------
//  Results register (screen 8)
// ---------------------------------------------------------------------------

/** One subject of one result row. */
export interface ResultSubjectScore {
  sectionId: string
  subjectId: string
  name: string
  points: number | null
  maxPoints: number
}

/** `ResultRowDto` — one participant of one exam. */
export interface ResultRow {
  participantId: string
  examId: string
  examTitle: string
  examDate: string | null
  kind: ExamKind
  delivery: ExamDelivery
  participantKind: ParticipantKind
  fullName: string
  classId: string | null
  className: string | null
  status: ParticipantStatus
  totalPoints: number | null
  maxPoints: number | null
  percent: number | null
  scoredAt: string | null
  /** Per-subject breakdown, when the server includes it. */
  scores?: ResultSubjectScore[]
}

export interface ResultListQuery {
  page?: number
  limit?: number
  search?: string
  examId?: string
  classId?: string
  subjectId?: string
  status?: ParticipantStatus
}

// ---------------------------------------------------------------------------
//  Attempt review (§6.3 `AttemptReviewDto`) — admin only, carries the key
// ---------------------------------------------------------------------------

export interface AttemptReview {
  participant: {
    id: string
    fullName: string
    kind: ParticipantKind
    grade: number | null
    className: string | null
  }
  exam: {
    id: string
    title: string
    kind: ExamKind
    examDate: string | null
  }
  /** `null` for manual exams. */
  attempt: {
    startedAt: string
    finishedAt: string | null
    finishReason: FinishReason | null
    deviceLabel: string | null
    firstIp: string | null
    answeredCount: number
    abuseFlagged: boolean
  } | null
  summary: {
    correctCount: number | null
    totalCount: number | null
    totalPoints: number | null
    maxPoints: number | null
    percent: number | null
  }
  perSubject: {
    subjectId: string
    name: string
    correct: number | null
    total: number | null
    points: number | null
    maxPoints: number | null
  }[]
  /** `null` for manual exams. */
  questions:
    | {
        id: string
        subjectId: string
        order: number
        text: string
        imageUrl: string | null
        options: { id: string; text: string }[]
        selectedOptionId: string | null
        correctOptionId: string
        isCorrect: boolean
      }[]
    | null
}

// ---------------------------------------------------------------------------
//  Question banks — only what the exam form's bank picker needs (§6.2)
// ---------------------------------------------------------------------------

/** §3.1 screen 3: derived, not stored. */
export type BankState = 'unconfigured' | 'notEnough' | 'ready'

/**
 * The fields of `BankRowDto` the section picker reads. The bank register (C1)
 * owns the full type; this screen must not depend on a file another unit is
 * still writing, so the request is made here.
 */
export interface BankOption {
  id: string
  grade: number
  subjectId: string
  subjectName: string
  questionsCount: number
  questionsPerTest: number | null
  timeLimitMin: number | null
  pointsPerCorrect: number | null
  state: BankState
  isArchived?: boolean
}

// ---------------------------------------------------------------------------
//  Requests
// ---------------------------------------------------------------------------

const BASE = '/admin/exams'

/** Drops empty values so the query string carries only real filters. */
function clean<T extends object>(query: T): Record<string, string | number> {
  const out: Record<string, string | number> = {}
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (typeof value === 'string' || typeof value === 'number') out[key] = value
  }
  return out
}

// ---- types ----

export async function listExamTypes(): Promise<ExamType[]> {
  const { data } = await api.get<ExamType[]>(`${BASE}/types`)
  return data
}

export async function createExamType(payload: ExamTypeCreate): Promise<ExamType> {
  const { data } = await api.post<ExamType>(`${BASE}/types`, payload)
  return data
}

export async function updateExamType(id: string, payload: ExamTypeUpdate): Promise<ExamType> {
  const { data } = await api.put<ExamType>(`${BASE}/types/${id}`, payload)
  return data
}

/** 409 when an exam uses the type — deactivate it instead. */
export async function deleteExamType(id: string): Promise<void> {
  await api.delete(`${BASE}/types/${id}`)
}

// ---- exams ----

export async function listExams(query: ExamListQuery = {}): Promise<Paged<ExamRow>> {
  const { data } = await api.get<Paged<ExamRow>>(BASE, { params: clean(query) })
  return data
}

export async function getExam(id: string): Promise<Exam> {
  const { data } = await api.get<Exam>(`${BASE}/${id}`)
  return data
}

export async function createExam(payload: ExamUpsert): Promise<Exam> {
  const { data } = await api.post<Exam>(BASE, payload)
  return data
}

/** 409 when the exam is past `draft` and the sections differ (§5.5 freeze). */
export async function updateExam(id: string, payload: ExamUpsert): Promise<Exam> {
  const { data } = await api.put<Exam>(`${BASE}/${id}`, payload)
  return data
}

/** 409 with the §8.1 reason in `message` when the exam cannot be published. */
export async function publishExam(id: string): Promise<Exam> {
  const { data } = await api.post<Exam>(`${BASE}/${id}/publish`)
  return data
}

export async function cancelExam(id: string, reason: string): Promise<Exam> {
  const { data } = await api.post<Exam>(`${BASE}/${id}/cancel`, { reason })
  return data
}

// ---- participants ----

export async function listParticipants(
  examId: string,
  query: ParticipantListQuery = {},
): Promise<Paged<ParticipantRow>> {
  const { data } = await api.get<Paged<ParticipantRow>>(`${BASE}/${examId}/participants`, {
    params: clean(query),
  })
  return data
}

/** Idempotent: a pupil already on the exam counts as `skipped`. */
export async function addParticipants(
  examId: string,
  payload: AddParticipantsRequest,
): Promise<AddParticipantsResult> {
  const { data } = await api.post<AddParticipantsResult>(
    `${BASE}/${examId}/participants`,
    payload,
  )
  return data
}

/** 409 once an attempt exists. */
export async function removeParticipant(examId: string, participantId: string): Promise<void> {
  await api.delete(`${BASE}/${examId}/participants/${participantId}`)
}

// ---- entry table ----

/** Bare, capped at 500 rows; 409 asks the user to split the exam. */
export async function getEntryTable(examId: string): Promise<EntryTable> {
  const { data } = await api.get<EntryTable>(`${BASE}/${examId}/entry-table`)
  return data
}

/** One request for the whole grid. 400 names the row and subject that is out of range. */
export async function saveEntryTable(examId: string, rows: EntrySaveRow[]): Promise<EntrySaveResult> {
  const { data } = await api.post<EntrySaveResult>(`${BASE}/${examId}/entry-table`, { rows })
  return data
}

export async function downloadEntryTemplate(examId: string): Promise<void> {
  await downloadBlob(`${BASE}/${examId}/entry-table/template`, {}, 'natijalar_shablon.xlsx')
}

/**
 * `dryRun = true` validates and reports; `false` validates and writes the
 * valid rows. The browser posts the file twice — no staging token (§6.2).
 */
export async function importEntryTable(
  examId: string,
  file: File,
  dryRun: boolean,
): Promise<ResultImportResult> {
  const fd = new FormData()
  fd.append('file', file)
  fd.append('dryRun', String(dryRun))
  const { data } = await api.post<ResultImportResult>(
    `${BASE}/${examId}/entry-table/import`,
    fd,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}

// ---- results ----

export async function listResults(query: ResultListQuery = {}): Promise<Paged<ResultRow>> {
  const { data } = await api.get<Paged<ResultRow>>(`${BASE}/results`, { params: clean(query) })
  return data
}

/** The whole filter as `.xlsx`, built by `ExcelExport` on the server — not the visible page. */
export async function exportResults(query: ResultListQuery = {}): Promise<void> {
  const filters: ResultListQuery = { ...query, page: undefined, limit: undefined }
  const today = new Date().toISOString().slice(0, 10)
  const short = filters.examId ? filters.examId.slice(0, 8) : 'hammasi'
  await downloadBlob(`${BASE}/results/export`, clean(filters), `natijalar_${short}_${today}.xlsx`)
}

// ---- attempt review ----

export async function getAttemptReview(participantId: string): Promise<AttemptReview> {
  const { data } = await api.get<AttemptReview>(`${BASE}/participants/${participantId}/review`)
  return data
}

/** Grade an online attempt now, `finish_reason = 'admin'` (§8.3). */
export async function forceFinishAttempt(participantId: string, reason: string): Promise<AttemptReview> {
  const { data } = await api.post<AttemptReview>(
    `${BASE}/participants/${participantId}/force-finish`,
    { reason },
  )
  return data
}

/** Destroys the attempt and its score (§8.3). Needs `exams` AND `admission`. */
export async function resetAttempt(participantId: string, reason: string): Promise<void> {
  await api.post(`${BASE}/participants/${participantId}/reset-attempt`, { reason })
}

// ---- question banks (for the online section picker) ----

/** Live banks of one grade, for the admission exam form. Read is gated by `admission`. */
export async function listBankOptions(grade: number): Promise<BankOption[]> {
  const { data } = await api.get<Paged<BankOption>>('/admin/admission/banks', {
    params: { grade, page: 1, limit: PAGE_LIMIT_MAX },
  })
  return data.items.filter((b) => !b.isArchived)
}

// ---------------------------------------------------------------------------
//  File download
// ---------------------------------------------------------------------------

/**
 * Blob in, file name from `Content-Disposition`, download triggered by hand —
 * the `surveySubmissions.ts` / `classes.ts` pattern. On failure the error body
 * arrives as a Blob too; it is decoded back to JSON so `examsErrorMessage`
 * can read the server's Uzbek sentence.
 */
async function downloadBlob(
  url: string,
  params: Record<string, string | number>,
  fallbackName: string,
): Promise<void> {
  let res
  try {
    res = await api.get<Blob>(url, { params, responseType: 'blob' })
  } catch (err) {
    throw await decodeBlobError(err)
  }
  const href = URL.createObjectURL(res.data)
  const a = document.createElement('a')
  a.href = href
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const match = cd.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
  a.download = match?.[1] ? decodeURIComponent(match[1]) : fallbackName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(href)
}

async function decodeBlobError(err: unknown): Promise<unknown> {
  if (!axios.isAxiosError(err) || !err.response) return err
  const data: unknown = err.response.data
  if (!(data instanceof Blob)) return err
  try {
    err.response.data = JSON.parse(await data.text()) as unknown
  } catch {
    err.response.data = null
  }
  return err
}

// ---------------------------------------------------------------------------
//  Errors — every failure the §6.3 contract names, in Uzbek
// ---------------------------------------------------------------------------

/**
 * Which call failed. Admin endpoints answer `{ message }` with a status and no
 * machine code (§6, "Errors"), so the same status means different things on
 * different calls — a 409 on a type delete is "in use", on publish it is the
 * §8.1 reason, on the entry table it is the 500-row cap.
 */
export type ExamOp =
  | 'types.load'
  | 'types.save'
  | 'types.delete'
  | 'exams.load'
  | 'exam.load'
  | 'exam.save'
  | 'exam.publish'
  | 'exam.cancel'
  | 'participants.add'
  | 'participants.remove'
  | 'entry.load'
  | 'entry.save'
  | 'entry.template'
  | 'entry.import'
  | 'results.load'
  | 'results.export'
  | 'review.load'
  | 'review.forceFinish'
  | 'review.reset'
  | 'banks.load'
  | 'lookup.load'

interface OpMessages {
  fallback: string
  400?: string
  403?: string
  404?: string
  409?: string
}

const OP_MESSAGES: Record<ExamOp, OpMessages> = {
  'types.load': { fallback: "Imtihon turlarini yuklab bo'lmadi" },
  'types.save': {
    fallback: "Imtihon turini saqlab bo'lmadi",
    400: 'Nomini kiriting',
    404: "Bu imtihon turi topilmadi — u o'chirilgan bo'lishi mumkin",
    409: 'Bu nomli imtihon turi allaqachon bor',
  },
  'types.delete': {
    fallback: "Imtihon turini o'chirib bo'lmadi",
    404: "Bu imtihon turi allaqachon o'chirilgan",
    409: "Bu tur imtihonlarda ishlatilgan — o'chirib bo'lmaydi. Uni faolsizlantiring.",
  },
  'exams.load': { fallback: "Imtihonlarni yuklab bo'lmadi" },
  'exam.load': {
    fallback: "Imtihonni ochib bo'lmadi",
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
  },
  'exam.save': {
    fallback: "Imtihonni saqlab bo'lmadi",
    400: "Ma'lumotlarni tekshiring: nomi, sana va har bir fanning maksimal balli",
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
    409: "Imtihon e'lon qilingan — fanlar ro'yxatini endi o'zgartirib bo'lmaydi",
  },
  'exam.publish': {
    fallback: "Imtihonni e'lon qilib bo'lmadi",
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
    409:
      "Imtihonni e'lon qilib bo'lmaydi: kamida bitta fan bo'lishi, test bazalari tayyor " +
      "bo'lishi, savollar 200 tadan oshmasligi va vaqt oralig'i kelajakda bo'lishi kerak",
  },
  'exam.cancel': {
    fallback: "Imtihonni bekor qilib bo'lmadi",
    400: 'Bekor qilish sababini yozing',
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
    409: "Bu imtihon yopilgan yoki allaqachon bekor qilingan",
  },
  'participants.add': {
    fallback: "Sinflarni imtihonga qo'shib bo'lmadi",
    404: 'Imtihon yoki tanlangan sinf topilmadi',
    409: "Bekor qilingan yoki yopilgan imtihonga o'quvchi qo'shib bo'lmaydi",
  },
  'participants.remove': {
    fallback: "O'quvchini imtihondan chiqarib bo'lmadi",
    404: "Bu o'quvchi imtihon ro'yxatida allaqachon yo'q",
    409: "O'quvchi imtihonni boshlagan — uni ro'yxatdan chiqarib bo'lmaydi",
  },
  'entry.load': {
    fallback: "Natijalar jadvalini yuklab bo'lmadi",
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
    409:
      "Imtihonda 500 dan ortiq ishtirokchi bor — jadvalni ochib bo'lmaydi. " +
      "Imtihonni sinflar bo'yicha bir nechta imtihonga bo'ling.",
  },
  'entry.save': {
    fallback: "Natijalarni saqlab bo'lmadi",
    400: "Ball fanning maksimal ballidan oshmasligi va manfiy bo'lmasligi kerak",
    404: "Imtihon yoki ishtirokchi topilmadi — sahifani yangilang",
    409: "Bu imtihon holatida natija kiritib bo'lmaydi",
  },
  'entry.template': {
    fallback: "Shablonni yuklab bo'lmadi",
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
  },
  'entry.import': {
    fallback: "Faylni yuklab bo'lmadi",
    400: 'Faqat .xlsx (Excel) fayl qabul qilinadi',
    404: "Imtihon topilmadi — u o'chirilgan bo'lishi mumkin",
    409: "Bu imtihon holatida natija kiritib bo'lmaydi",
  },
  'results.load': { fallback: "Natijalarni yuklab bo'lmadi" },
  'results.export': { fallback: "Eksport qilib bo'lmadi" },
  'review.load': {
    fallback: "Natija tafsilotini yuklab bo'lmadi",
    404: "Bu natija topilmadi — ishtirokchi o'chirilgan bo'lishi mumkin",
  },
  'review.forceFinish': {
    fallback: "Urinishni yakunlab bo'lmadi",
    400: 'Sababini yozing',
    409: 'Urinish allaqachon yakunlangan yoki hali boshlanmagan',
  },
  'review.reset': {
    fallback: "Urinishni bekor qilib bo'lmadi",
    400: 'Sababini yozing',
    403: "Urinishni bekor qilish uchun 'Imtihonlar' va 'Qabul' ruxsatlari birga kerak",
    409: "Bu ishtirokchida bekor qilinadigan urinish yo'q",
  },
  'banks.load': {
    fallback: "Test bazalarini yuklab bo'lmadi",
    403: "Test bazalarini ko'rish uchun 'Qabul' ruxsati kerak",
  },
  'lookup.load': { fallback: "Ma'lumotnomalarni yuklab bo'lmadi" },
}

interface ApiErrorBody {
  message?: unknown
}

function statusOf(err: unknown): number | undefined {
  return axios.isAxiosError(err) ? err.response?.status : undefined
}

function serverMessage(err: unknown): string | null {
  if (!axios.isAxiosError(err)) return null
  const data: unknown = err.response?.data
  if (typeof data !== 'object' || data === null) return null
  const message = (data as ApiErrorBody).message
  return typeof message === 'string' && message.trim() ? message.trim() : null
}

/** HTTP status of a failed call, for logic that must branch on it. */
export function examsErrorStatus(err: unknown): number | undefined {
  return statusOf(err)
}

/**
 * The sentence shown to the user. The server's own Uzbek message wins — it is
 * the precise one ("3-qator, Matematika: 55 > 50"; the §8.1 publish reason).
 * Otherwise the call-specific sentence for that status, then a generic one.
 */
export function examsErrorMessage(err: unknown, op: ExamOp): string {
  const fromServer = serverMessage(err)
  if (fromServer) return fromServer

  const messages = OP_MESSAGES[op]
  if (!axios.isAxiosError(err)) return messages.fallback

  const status = err.response?.status
  if (status === undefined) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
  if (status === 400 && messages[400]) return messages[400]
  if (status === 403) return messages[403] ?? "Bu amal uchun 'Imtihonlar' ruxsati yo'q."
  if (status === 404 && messages[404]) return messages[404]
  if (status === 409 && messages[409]) return messages[409]
  if (status === 401) return 'Sessiya tugagan — qaytadan kiring.'
  if (status === 413) return 'Fayl juda katta.'
  if (status === 429) return "Juda ko'p so'rov yuborildi. Birozdan so'ng qayta urinib ko'ring."
  if (status >= 500) return "Serverda xatolik. Keyinroq urinib ko'ring."
  return messages.fallback
}
