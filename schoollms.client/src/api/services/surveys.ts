/**
 * Arizalar (public enrolment forms) — admin CRUD.
 * `docs/modules/sales-marketing.md` §5.2, task SM-8.
 *
 *   GET    /api/admin/surveys[?includeInactive=true]
 *   GET    /api/admin/surveys/{id}
 *   GET    /api/admin/surveys/slug-available?slug=…&excludeId=…
 *   POST   /api/admin/surveys
 *   PUT    /api/admin/surveys/{id}
 *   PATCH  /api/admin/surveys/{id}/active
 *   DELETE /api/admin/surveys/{id}
 *
 * Hammasi `[Authorize] [AdminPerm("marketing")]` ostida: xodim O'QIY oladi,
 * YOZISH uchun `marketing` ruxsati kerak (admin/superadmin — cheklovsiz).
 *
 * `publicUrl` SERVERDA yig'iladi (§5.2): admin `test.` subdomenida o'tiradi,
 * havola esa maktab reklama qiladigan domenniki bo'lishi kerak. Klient uni
 * hech qachon o'zi yasamaydi — faqat ko'rsatadi va nusxalaydi.
 */
import axios from 'axios'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/** §2.4 dagi beshta o'quvchi maydoni kalitlari — DTO bilan bir xil nomda. */
export interface SurveyFieldToggles {
  showStudentFirstNameInput: boolean
  showStudentLastNameInput: boolean
  showStudentPhoneNumberInput: boolean
  /** Rule T (§2.4): o'chirib bo'lmaydi — `ck_surveys_required_toggles`. */
  showStudentGradeInput: boolean
  /** Rule T (§2.4): o'chirib bo'lmaydi — `ck_surveys_required_toggles`. */
  showStudentGenderInput: boolean
}

export type SurveyToggleKey = keyof SurveyFieldToggles

/** Rule T qat'iy bog'lab qo'ygan ikkita kalit. */
export const PINNED_TOGGLES: readonly SurveyToggleKey[] = [
  'showStudentGenderInput',
  'showStudentGradeInput',
]

/** Xodimga ko'rsatiladigan nomlar (xato matnlarida ham shular ishlatiladi). */
export const toggleLabels: Record<SurveyToggleKey, string> = {
  showStudentFirstNameInput: "O'quvchining ismi",
  showStudentLastNameInput: "O'quvchining familiyasi",
  showStudentPhoneNumberInput: "O'quvchining telefoni",
  showStudentGradeInput: 'Sinf',
  showStudentGenderInput: 'Jinsi',
}

/** §5.2 `SurveySaveRequest`. */
export interface SurveySaveRequest extends SurveyFieldToggles {
  name: string
  slug: string
  subtitle: string | null
  /** `/uploads/...` — `POST /admin/uploads` qaytargan yo'l. */
  imageUrl: string | null
  /** `/uploads/...` — taklif hujjati (pdf/doc). */
  offerUrl: string | null
  thankYouText: string | null
  /** Lid tushadigan ustun. `null` — Rule S 2-qadami (server o'zi tanlaydi). */
  stageId: string | null
}

/** §5.2 `SurveyDto` = so'rov maydonlari + serverda hisoblanganlari. */
export interface Survey extends SurveySaveRequest {
  id: string
  isActive: boolean
  /** To'liq ommaviy havola — serverda yig'iladi, §5.2. */
  publicUrl: string
  submissionCount: number
  leadCount: number
  /** ISO-8601 yoki null (hali birorta ariza tushmagan). */
  lastSubmissionAt: string | null
  createdAt: string
  updatedAt: string | null
}

/** Yangi ariza uchun boshlang'ich qiymatlar — §4.1 `default`lari bilan bir xil. */
export const DEFAULT_TOGGLES: SurveyFieldToggles = {
  showStudentFirstNameInput: true,
  showStudentLastNameInput: true,
  showStudentPhoneNumberInput: false,
  showStudentGradeInput: true,
  showStudentGenderInput: true,
}

// ---------------------------------------------------------------------------
//  Slug
// ---------------------------------------------------------------------------

/** `ck_surveys_slug` bilan bir xil qoida (§4.1). */
const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/
export const SLUG_MIN_LENGTH = 3
export const SLUG_MAX_LENGTH = 60

/**
 * Nomdan slug taklif qilish (§5.2): kichik harf; `'` va `ʻ` tashlanadi;
 * `a-z0-9` dan tashqari har qanday ketma-ketlik bitta `-` ga siqiladi;
 * chetdagi `-` lar olib tashlanadi. Xodim uni qo'lda o'zgartira oladi.
 */
export function proposeSlug(name: string): string {
  return name
    .toLowerCase()
    .replace(/['‘’ʻʼ´`]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, SLUG_MAX_LENGTH)
    .replace(/-+$/, '')
}

/** Slug shakli to'g'rimi — bo'sh joy bo'lsa sababini o'zbekcha qaytaradi. */
export function slugProblem(slug: string): string | null {
  if (!slug) return 'Havola manzilini kiriting'
  if (slug.length < SLUG_MIN_LENGTH) return `Kamida ${SLUG_MIN_LENGTH} ta belgi bo'lsin`
  if (slug.length > SLUG_MAX_LENGTH) return `Ko'pi bilan ${SLUG_MAX_LENGTH} ta belgi`
  if (!SLUG_PATTERN.test(slug)) {
    return "Faqat kichik lotin harflari, raqam va '-' ishlatiladi (masalan: qabul-2027)"
  }
  return null
}

// ---------------------------------------------------------------------------
//  Xatolar — §5.6, bir joyda
// ---------------------------------------------------------------------------

export type SurveyErrorCode =
  | 'validation'
  | 'slug_taken'
  | 'survey_fields_required'
  | 'survey_in_use'
  | 'survey_not_found'
  | 'forbidden'
  | 'rate_limited'
  | 'offline'
  | 'server'

export interface SurveyErrorInfo {
  code: SurveyErrorCode
  message: string
  /** Faqat `survey_fields_required` da to'ladi (§2.4). */
  fields: SurveyToggleKey[]
}

const FALLBACK_MESSAGES: Record<SurveyErrorCode, string> = {
  validation: "Ma'lumotlarni tekshiring",
  slug_taken: 'Bu havola manzili band — boshqasini tanlang',
  survey_fields_required: "Bu maydonlarni o'chirib bo'lmaydi: jins, sinf",
  survey_in_use:
    "Bu arizada topshirilgan so'rovlar bor — uni o'chirib bo'lmaydi, faol emas qilib qo'ying",
  survey_not_found: 'Ariza topilmadi — ro‘yxat eskirgan bo‘lishi mumkin',
  forbidden: "Bu amal uchun 'Sotuv va marketing' ruxsati yo'q",
  rate_limited: "Juda ko'p urinish bo'ldi. Birozdan so'ng qayta urinib ko'ring.",
  offline: "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring.",
  server: "Serverda xatolik. Iltimos, birozdan so'ng qayta urinib ko'ring.",
}

interface ApiErrorBody {
  code?: string
  message?: string
  fields?: unknown
}

function bodyOf(err: unknown): ApiErrorBody {
  if (!axios.isAxiosError(err)) return {}
  const data: unknown = err.response?.data
  return typeof data === 'object' && data !== null ? (data as ApiErrorBody) : {}
}

const TOGGLE_KEYS: readonly SurveyToggleKey[] = [
  'showStudentFirstNameInput',
  'showStudentLastNameInput',
  'showStudentPhoneNumberInput',
  'showStudentGradeInput',
  'showStudentGenderInput',
]

/** Serverning `fields` ro'yxatidan faqat bizga tanish kalitlarni oladi. */
function readToggleFields(raw: unknown): SurveyToggleKey[] {
  if (!Array.isArray(raw)) return []
  return TOGGLE_KEYS.filter((key) => raw.includes(key))
}

/** Javob kodini §5.6 ro'yxatidagi kalitga aylantiradi. */
function codeOf(err: unknown, body: ApiErrorBody): SurveyErrorCode {
  switch (body.code) {
    case 'validation':
    case 'slug_taken':
    case 'survey_fields_required':
    case 'survey_in_use':
    case 'survey_not_found':
      return body.code
  }
  if (!axios.isAxiosError(err)) return 'server'
  const status = err.response?.status
  if (status === undefined) return 'offline'
  // Kod kelmagan holat uchun zaxira — status bo'yicha.
  if (status === 400) return 'validation'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'survey_not_found'
  if (status === 409) return 'slug_taken'
  if (status === 429) return 'rate_limited'
  return 'server'
}

/**
 * Har qanday xatoni o'zbekcha bitta gapga keltiradi (§5.6).
 * Server o'z jumlasini yuborgan bo'lsa — u ustun: u aniqroq ("jins, sinf").
 */
export function surveyError(err: unknown): SurveyErrorInfo {
  const body = bodyOf(err)
  const code = codeOf(err, body)
  const message =
    typeof body.message === 'string' && body.message.trim()
      ? body.message
      : FALLBACK_MESSAGES[code]
  return { code, message, fields: readToggleFields(body.fields) }
}

// ---------------------------------------------------------------------------
//  So'rovlar
// ---------------------------------------------------------------------------

/**
 * Ro'yxat, yangisi birinchi. `includeInactive` — yopilganlar ham ko'rinsin.
 * Mock rejimda bo'sh: soxta ariza faqat noto'g'ri sozlangan API manzilini
 * yashirardi.
 */
export async function getSurveys(includeInactive = false): Promise<Survey[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Survey[]>(
    `/admin/surveys${includeInactive ? '?includeInactive=true' : ''}`,
  )
  return data
}

export async function getSurvey(id: string): Promise<Survey> {
  const { data } = await api.get<Survey>(`/admin/surveys/${id}`)
  return data
}

/**
 * Slug bandmi. Yozayotganda chaqiriladi — haqiqiy hakam baribir baza
 * (`ux_surveys_slug`), poyga holatida saqlash 409 `slug_taken` qaytaradi.
 */
export async function isSlugAvailable(slug: string, excludeId?: string): Promise<boolean> {
  if (USE_MOCK) {
    await delay(150)
    return true
  }
  const params = new URLSearchParams({ slug })
  if (excludeId) params.set('excludeId', excludeId)
  const { data } = await api.get<{ available: boolean }>(
    `/admin/surveys/slug-available?${params.toString()}`,
  )
  return data.available
}

export async function createSurvey(payload: SurveySaveRequest): Promise<Survey> {
  const { data } = await api.post<Survey>('/admin/surveys', payload)
  return data
}

export async function updateSurvey(id: string, payload: SurveySaveRequest): Promise<Survey> {
  const { data } = await api.put<Survey>(`/admin/surveys/${id}`, payload)
  return data
}

/** Faol/yopiq tugmasi. Yopiq arizaning ommaviy sahifasi 404 beradi (D8). */
export async function setSurveyActive(id: string, isActive: boolean): Promise<void> {
  await api.patch(`/admin/surveys/${id}/active`, { isActive })
}

/** Topshirilgan arizasi bo'lsa server 409 `survey_in_use` qaytaradi (D7). */
export async function deleteSurvey(id: string): Promise<void> {
  await api.delete(`/admin/surveys/${id}`)
}
