/**
 * Test bazasi — the admission question bank, admin side.
 * `docs/modules/admission-and-testing.md` §6.2, unit C1.
 *
 *   GET    /api/admin/admission/banks                      ?page&limit&search&grade&subjectId
 *   POST   /api/admin/admission/banks
 *   GET    /api/admin/admission/banks/{id}
 *   PUT    /api/admin/admission/banks/{id}
 *   DELETE /api/admin/admission/banks/{id}                 409 when an exam section uses it
 *   GET    /api/admin/admission/banks/{id}/questions       ?page&limit&search
 *   POST   /api/admin/admission/questions
 *   PUT    /api/admin/admission/questions/{id}
 *   DELETE /api/admin/admission/questions/{id}             409 when answered / bank feeds a published exam
 *   GET    /api/admin/admission/questions/import/template  ?bankId  → savollar_shablon.xlsx
 *   POST   /api/admin/admission/questions/import           multipart file, bankId, dryRun
 *
 * PERMISSION (§4.3, §4.4). Every endpoint sits behind
 * `[AdminPerm("admission", GatedRead = true)]`: unlike the rest of the admin
 * API, READS are gated too, because the question list carries the answer key
 * of a live entrance exam. A staff account without `admission` gets 403 on the
 * list itself, not only on the buttons.
 *
 * THE ANSWER KEY (§7.7). `options[].isCorrect` exists on THIS admin client
 * only. The public exam page has its own axios instance and its own DTOs that
 * never carry it; nothing here may be imported from `pages/public/**`.
 *
 * DERIVED, NOT STORED (§10 "Reports, not storage"). `questionsCount` and the
 * bank `state` come from the server. The client renders them and never
 * recomputes the ready / notEnough / unconfigured rule.
 */
import axios from 'axios'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

const BANKS = '/admin/admission/banks'
const QUESTIONS = '/admin/admission/questions'

/** §6 pagination: the server default is 50, the cap is 200. */
export const DEFAULT_PAGE_LIMIT = 50
export const MAX_PAGE_LIMIT = 200

/** §5.3: between 2 and 6 options, lettered A–F by `order`. */
export const MIN_OPTIONS = 2
export const MAX_OPTIONS = 6

// ---------------------------------------------------------------------------
//  Shapes — §5.1–§5.3, §6.2
// ---------------------------------------------------------------------------

/** §6 pagination envelope, shared by every new paged endpoint. */
export interface Paged<T> {
  items: T[]
  total: number
  page: number
  limit: number
}

/**
 * §3.1 screen 3: derived on the server from `questionsPerTest` against
 * `questionsCount` — grey / amber / green.
 */
export type BankState = 'unconfigured' | 'notEnough' | 'ready'

/**
 * `BankRowDto` (list) and `BankDto` (single) — the same fields: the detail
 * screen needs the count and the state for its warning banner, so both
 * carry them.
 */
export interface QuestionBank {
  id: string
  /** 0–11; 0 is the preparatory grade ("nol sinf"), not "unknown". */
  grade: number
  subjectId: string
  subjectName: string
  /** `count(*)` over `questions` — computed by the server, never stored. */
  questionsCount: number
  /** How many questions one sitting draws. `null` — not configured yet. */
  questionsPerTest: number | null
  /** Minutes for this bank's block, 1–600. */
  timeLimitMin: number | null
  /** `numeric(6,2)`, > 0. */
  pointsPerCorrect: number | null
  state: BankState
}

/** `PUT /banks/{id}` body — the settings strip. `null` clears a value. */
export interface BankSettings {
  questionsPerTest: number | null
  timeLimitMin: number | null
  pointsPerCorrect: number | null
}

/** `POST /banks` body. Grade and subject are fixed after creation. */
export interface BankCreateRequest extends BankSettings {
  grade: number
  subjectId: string
}

export interface BankListQuery {
  page?: number
  limit?: number
  search?: string
  grade?: number
  subjectId?: string
}

export interface QuestionOption {
  id: string
  text: string
  /** The answer key. Admin-only — see the file header. */
  isCorrect: boolean
  /** 0 → A … 5 → F. */
  order: number
}

export interface Question {
  id: string
  bankId: string
  text: string
  /** `/uploads/<guid>.<ext>` from `POST /api/admin/uploads`, or null. */
  imageUrl: string | null
  order: number
  options: QuestionOption[]
}

/**
 * One option in a save request. The array position is the option's order
 * (index 0 is A). On update, an option that keeps its `id` is edited in
 * place; one sent without an `id` is new; one left out is removed.
 */
export interface QuestionOptionInput {
  id?: string
  text: string
  isCorrect: boolean
}

export interface QuestionCreateRequest {
  bankId: string
  text: string
  imageUrl: string | null
  options: QuestionOptionInput[]
}

export interface QuestionUpdateRequest {
  text: string
  imageUrl: string | null
  options: QuestionOptionInput[]
}

export interface QuestionListQuery {
  page?: number
  limit?: number
  search?: string
}

/** §6.2 — the closed set of seven import error reasons. */
export type QuestionImportReason =
  | 'emptyText'
  | 'tooFewOptions'
  | 'tooManyOptions'
  | 'noCorrect'
  | 'badCorrect'
  | 'duplicateOption'
  | 'badImageUrl'

export interface QuestionImportError {
  reason: QuestionImportReason
  /** 1-based sheet rows INCLUDING the header — what the user sees in Excel. */
  rows: number[]
}

/** `QuestionImportResultDto`. `imported` is 0 on a dry run. */
export interface QuestionImportResult {
  fileName: string
  totalRows: number
  validCount: number
  errorCount: number
  imported: number
  errors: QuestionImportError[]
}

// ---------------------------------------------------------------------------
//  Errors — one reader for every call in this module
// ---------------------------------------------------------------------------

/** Which call failed — it decides the fallback sentence for a bare status. */
export type BankAction =
  | 'list'
  | 'open'
  | 'questions'
  | 'createBank'
  | 'updateBank'
  | 'deleteBank'
  | 'saveQuestion'
  | 'deleteQuestion'
  | 'template'
  | 'import'

export type BankErrorKind =
  | 'validation'
  | 'session'
  | 'forbidden'
  | 'not_found'
  | 'conflict'
  | 'too_large'
  | 'rate_limited'
  | 'offline'
  | 'server'

export interface BankErrorInfo {
  kind: BankErrorKind
  message: string
}

/** §6.2 / §13 Q9 — the server's own sentence for a non-.xlsx upload. */
export const XLSX_ONLY_MESSAGE = 'Faqat .xlsx (Excel) fayl qabul qilinadi'

const READ_ACTIONS: readonly BankAction[] = ['list', 'open', 'questions', 'template']

const FALLBACK: Record<BankAction, string> = {
  list: "Test bazasini yuklab bo'lmadi",
  open: "Bazani ochib bo'lmadi",
  questions: "Savollarni yuklab bo'lmadi",
  createBank: "Bazani yaratib bo'lmadi",
  updateBank: "Sozlamalarni saqlab bo'lmadi",
  deleteBank: "Bazani o'chirib bo'lmadi",
  saveQuestion: "Savolni saqlab bo'lmadi",
  deleteQuestion: "Savolni o'chirib bo'lmadi",
  template: "Shablonni yuklab bo'lmadi",
  import: "Faylni yuklab bo'lmadi",
}

const BANK_GONE = "Baza topilmadi — u o'chirilgan bo'lishi mumkin"

const NOT_FOUND: Record<BankAction, string> = {
  list: BANK_GONE,
  open: BANK_GONE,
  questions: BANK_GONE,
  createBank: "Tanlangan fan topilmadi — fanlar ro'yxatini yangilang",
  updateBank: BANK_GONE,
  deleteBank: BANK_GONE,
  saveQuestion: "Savol yoki uning bazasi topilmadi — sahifani yangilang",
  deleteQuestion: "Savol topilmadi — u allaqachon o'chirilgan bo'lishi mumkin",
  template: BANK_GONE,
  import: BANK_GONE,
}

/** 409 — the three the contract names, then a generic one. */
const CONFLICT: Partial<Record<BankAction, string>> = {
  createBank: 'Bu sinf va fan uchun baza allaqachon bor',
  deleteBank: "Bu baza imtihonda ishlatilgan — uni o'chirib bo'lmaydi",
  deleteQuestion:
    "Bu savolni o'chirib bo'lmaydi: unga javob berilgan yoki baza e'lon qilingan imtihonda ishlatilmoqda",
}

interface ApiErrorBody {
  code?: string
  message?: string
}

function bodyOf(err: unknown): ApiErrorBody {
  if (!axios.isAxiosError(err)) return {}
  const data: unknown = err.response?.data
  return typeof data === 'object' && data !== null && !(data instanceof Blob)
    ? (data as ApiErrorBody)
    : {}
}

function kindOf(err: unknown): BankErrorKind {
  if (!axios.isAxiosError(err)) return 'server'
  const status = err.response?.status
  if (status === undefined) return 'offline'
  if (status === 400 || status === 415 || status === 422) return 'validation'
  if (status === 401) return 'session'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 409) return 'conflict'
  if (status === 413) return 'too_large'
  if (status === 429) return 'rate_limited'
  return 'server'
}

function fallbackFor(kind: BankErrorKind, action: BankAction): string {
  switch (kind) {
    case 'validation':
      return action === 'import'
        ? "Faylni o'qib bo'lmadi — shablon bo'yicha to'ldirilgan .xlsx fayl tanlang"
        : "Ma'lumotlarni tekshiring"
    case 'session':
      return 'Sessiya tugagan — qaytadan kiring'
    case 'forbidden':
      return READ_ACTIONS.includes(action)
        ? "Test bazasini ko'rish uchun «Qabul» ruxsati kerak"
        : "Bu amal uchun «Qabul» ruxsati kerak"
    case 'not_found':
      return NOT_FOUND[action]
    case 'conflict':
      return CONFLICT[action] ?? "Ma'lumot boshqa joyda o'zgargan — sahifani yangilang"
    case 'too_large':
      return 'Fayl juda katta'
    case 'rate_limited':
      return "Juda ko'p so'rov yuborildi. Birozdan so'ng qayta urinib ko'ring"
    case 'offline':
      return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring"
    case 'server':
      return FALLBACK[action]
  }
}

/**
 * Any failure → one Uzbek sentence. The server's own `message` wins when it
 * sent one (§6 "Errors": `{ message: "<Uzbek text>" }`) — it knows which exam
 * holds the bank. Otherwise the status picks a sentence for this action.
 */
export function admissionBankError(err: unknown, action: BankAction): BankErrorInfo {
  const kind = kindOf(err)
  const serverMessage = bodyOf(err).message
  const message =
    typeof serverMessage === 'string' && serverMessage.trim()
      ? serverMessage.trim()
      : fallbackFor(kind, action)
  return { kind, message }
}

/**
 * A `responseType: 'blob'` request carries its JSON error body as a Blob.
 * Turn it back into an object so `admissionBankError` can read `message`.
 */
async function unwrapBlobError(err: unknown): Promise<never> {
  if (axios.isAxiosError(err) && err.response && err.response.data instanceof Blob) {
    try {
      const parsed: unknown = JSON.parse(await err.response.data.text())
      err.response.data = parsed
    } catch {
      err.response.data = null
    }
  }
  throw err
}

// ---------------------------------------------------------------------------
//  Banks
// ---------------------------------------------------------------------------

function emptyPage<T>(query: { page?: number; limit?: number }): Paged<T> {
  return { items: [], total: 0, page: query.page ?? 1, limit: query.limit ?? DEFAULT_PAGE_LIMIT }
}

/**
 * The bank register. In mock mode it is empty on purpose — a fake bank would
 * only hide a misconfigured API address (the `surveys.ts` rule).
 */
export async function listBanks(query: BankListQuery = {}): Promise<Paged<QuestionBank>> {
  if (USE_MOCK) {
    await delay()
    return emptyPage(query)
  }
  const { data } = await api.get<Paged<QuestionBank>>(BANKS, {
    params: {
      page: query.page ?? 1,
      limit: query.limit ?? DEFAULT_PAGE_LIMIT,
      search: query.search?.trim() || undefined,
      grade: query.grade,
      subjectId: query.subjectId || undefined,
    },
  })
  return data
}

export async function getBank(id: string): Promise<QuestionBank> {
  const { data } = await api.get<QuestionBank>(`${BANKS}/${id}`)
  return data
}

export async function createBank(payload: BankCreateRequest): Promise<QuestionBank> {
  const { data } = await api.post<QuestionBank>(BANKS, payload)
  return data
}

export async function updateBankSettings(id: string, payload: BankSettings): Promise<QuestionBank> {
  const { data } = await api.put<QuestionBank>(`${BANKS}/${id}`, payload)
  return data
}

/** 409 when any exam section points at the bank (§6.2). */
export async function deleteBank(id: string): Promise<void> {
  await api.delete(`${BANKS}/${id}`)
}

// ---------------------------------------------------------------------------
//  Questions
// ---------------------------------------------------------------------------

/** One page of a bank's questions, answer key included (§6.2). */
export async function listQuestions(
  bankId: string,
  query: QuestionListQuery = {},
): Promise<Paged<Question>> {
  if (USE_MOCK) {
    await delay()
    return emptyPage(query)
  }
  const { data } = await api.get<Paged<Question>>(`${BANKS}/${bankId}/questions`, {
    params: {
      page: query.page ?? 1,
      limit: query.limit ?? DEFAULT_PAGE_LIMIT,
      search: query.search?.trim() || undefined,
    },
  })
  return data
}

/** A runaway guard: 50 pages × 200 = 10 000 questions, far past any real bank. */
const MAX_PAGES = 50

/**
 * Every question of one bank, walking the pages at the 200 cap.
 *
 * §3.1 screen 4 filters by question text in the browser, so the detail
 * screen needs the whole bank, not one page of it. A bank is a few hundred
 * rows (§5.1), i.e. one to three requests.
 */
export async function listAllQuestions(bankId: string): Promise<Question[]> {
  const all: Question[] = []
  for (let page = 1; page <= MAX_PAGES; page += 1) {
    const chunk = await listQuestions(bankId, { page, limit: MAX_PAGE_LIMIT })
    all.push(...chunk.items)
    if (chunk.items.length === 0 || all.length >= chunk.total) break
  }
  return all
}

export async function createQuestion(payload: QuestionCreateRequest): Promise<Question> {
  const { data } = await api.post<Question>(QUESTIONS, payload)
  return data
}

export async function updateQuestion(id: string, payload: QuestionUpdateRequest): Promise<Question> {
  const { data } = await api.put<Question>(`${QUESTIONS}/${id}`, payload)
  return data
}

/** 409 when answered, or when its bank feeds a published exam (§6.2, §8.2). */
export async function deleteQuestion(id: string): Promise<void> {
  await api.delete(`${QUESTIONS}/${id}`)
}

// ---------------------------------------------------------------------------
//  Excel import — one endpoint with a dry run (§6.2)
// ---------------------------------------------------------------------------

/** §13 Q9 — `.xlsx` only; checked here too so the user is told before an upload. */
export function isXlsx(file: File): boolean {
  return file.name.toLowerCase().endsWith('.xlsx')
}

/** `savollar_shablon.xlsx` — sheet `Savollar` plus the `Yo'riqnoma` rules sheet. */
export async function downloadQuestionTemplate(bankId: string): Promise<void> {
  const res = await api
    .get<Blob>(`${QUESTIONS}/import/template`, { params: { bankId }, responseType: 'blob' })
    .catch(unwrapBlobError)

  const url = URL.createObjectURL(res.data)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const match = cd.match(/filename="?([^";]+)"?/)
  a.download = match?.[1] ?? 'savollar_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/**
 * `dryRun = true` validates and reports, writes nothing.
 * `dryRun = false` validates again and writes the valid rows; a row with any
 * error is skipped whole. The browser posts the same file both times — there
 * is no staging token and no "session expired" (§6.2).
 */
export async function importQuestions(
  bankId: string,
  file: File,
  dryRun: boolean,
): Promise<QuestionImportResult> {
  const form = new FormData()
  form.append('file', file)
  form.append('bankId', bankId)
  form.append('dryRun', dryRun ? 'true' : 'false')
  const { data } = await api.post<QuestionImportResult>(`${QUESTIONS}/import`, form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}
