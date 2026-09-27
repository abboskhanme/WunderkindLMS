import type {
  LaAnalytics,
  LaCriterion,
  LaCriterionCreateInput,
  LaCriterionPatchInput,
  LaDashboard,
  LaObservation,
  LaObservationInput,
  LaObservationListItem,
  LaRatingBoard,
  LaSection,
  LaTeacher,
  LaTeacherLesson,
  LaTeacherSummary,
  ObservationStatus,
} from '@/types/lessonAnalysis'
import { api } from '../client'

/**
 * Dars tahlili — API (docs/modules/lesson-analysis.md §5).
 *
 * Staff (`lessonAnalysis` ruxsati, admin/superadmin) — `/admin/lesson-analysis`,
 * o'qituvchi (faqat o'zining yakunlangan tahlillari) — `/teacher/lesson-analysis`.
 * Javob shakllari `@/types/lessonAnalysis` da — backend DTO'lari ham o'shandan.
 */

const ADMIN = '/admin/lesson-analysis'
const TEACHER = '/teacher/lesson-analysis'

/** Bo'sh qiymatlarni so'rovga qo'shmaymiz (`?teacherId=` bo'sh satr filtr emas). */
function clean(params: Record<string, string | number | boolean | null | undefined>) {
  const out: Record<string, string | number | boolean> = {}
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue
    out[k] = v
  }
  return out
}

/* ------------------------------------------------------------------ xatolar */

interface HttpErrorShape {
  response?: { status?: number; data?: unknown }
  message?: string
}

function bodyMessage(data: unknown): string | null {
  if (typeof data === 'string') return data.trim() || null
  if (typeof data !== 'object' || data === null) return null
  const body = data as { message?: unknown; detail?: unknown; title?: unknown; errors?: unknown }
  if (typeof body.message === 'string' && body.message) return body.message
  if (typeof body.detail === 'string' && body.detail) return body.detail
  // ASP.NET validation ProblemDetails: { errors: { Field: ["..."] } }
  if (body.errors && typeof body.errors === 'object') {
    const first = Object.values(body.errors as Record<string, unknown>)[0]
    if (Array.isArray(first) && typeof first[0] === 'string') return first[0]
  }
  if (typeof body.title === 'string' && body.title) return body.title
  return null
}

/** HTTP status (tarmoq uzilsa — undefined). */
export function laErrorStatus(err: unknown): number | undefined {
  return typeof err === 'object' && err !== null ? (err as HttpErrorShape).response?.status : undefined
}

/** Foydalanuvchiga ko'rsatiladigan o'zbekcha xato matni. */
export function laErrorMessage(err: unknown, fallback = 'Kutilmagan xatolik'): string {
  if (typeof err !== 'object' || err === null) return fallback
  const e = err as HttpErrorShape
  const fromBody = bodyMessage(e.response?.data)
  if (fromBody) return fromBody
  const status = e.response?.status
  if (status === 403) return "Bu amal uchun ruxsatingiz yo'q"
  if (status === 404) return 'Topilmadi'
  if (status && status >= 500) return `Server xatosi (${status})`
  if (!e.response && e.message === 'Network Error') return "Server bilan aloqa yo'q"
  return err instanceof Error && err.message ? err.message : fallback
}

/* ------------------------------------------------------------ fayl yuklash */

/**
 * Himoyalangan faylni (PDF / XLSX) axios orqali oladi — JWT sarlavhasi bilan —
 * va brauzerda yuklab beradi. Fayl nomi `Content-Disposition` dan, bo'lmasa `fallbackName`.
 */
async function downloadFile(
  path: string,
  params: Record<string, string | undefined>,
  fallbackName: string,
): Promise<void> {
  let res
  try {
    res = await api.get<Blob>(path, { params: clean(params), responseType: 'blob' })
  } catch (err) {
    // `responseType: 'blob'` xato javobini ham blob qiladi — matnini o'qib, xabarni chiqaramiz.
    const data = (err as HttpErrorShape).response?.data
    if (data instanceof Blob) {
      let text = ''
      try {
        text = await data.text()
      } catch {
        /* o'qib bo'lmadi — umumiy xabar */
      }
      let parsed: unknown = text
      try {
        parsed = JSON.parse(text)
      } catch {
        /* JSON emas */
      }
      throw new Error(bodyMessage(parsed) ?? laErrorMessage(err, "Faylni yuklab bo'lmadi"), { cause: err })
    }
    throw err
  }
  const blob = res.data
  if (blob.type.includes('application/json')) throw new Error("Faylni yuklab bo'lmadi")

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const star = cd.match(/filename\*=UTF-8''([^;]+)/i)
  const plain = cd.match(/filename="?([^";]+)"?/i)
  a.download = star ? decodeURIComponent(star[1]) : (plain?.[1] ?? fallbackName)
  document.body.appendChild(a)
  a.click()
  a.remove()
  // Darrov bekor qilinsa ba'zi brauzerlarda yuklash uziladi.
  window.setTimeout(() => URL.revokeObjectURL(url), 30_000)
}

/** Fayl nomi uchun xavfsiz qism: bo'sh joy → "-", taqiqlangan belgilar olib tashlanadi. */
function slug(value: string): string {
  return value.trim().replace(/[\\/:*?"<>|]+/g, '').replace(/\s+/g, '-') || 'tahlil'
}

/* ------------------------------------------------------------------- staff */

export interface ObservationFilters {
  teacherId?: string
  status?: ObservationStatus | ''
  dateFrom?: string
  dateTo?: string
}

export interface DateRange {
  dateFrom?: string
  dateTo?: string
}

export async function getCriteria(includeInactive = false): Promise<LaSection[]> {
  const { data } = await api.get<LaSection[]>(`${ADMIN}/criteria`, {
    params: clean({ includeInactive: includeInactive || undefined }),
  })
  return data
}

export async function createCriterion(input: LaCriterionCreateInput): Promise<LaCriterion> {
  const { data } = await api.post<LaCriterion>(`${ADMIN}/criteria`, input)
  return data
}

export async function updateCriterion(id: string, input: LaCriterionPatchInput): Promise<LaCriterion> {
  const { data } = await api.patch<LaCriterion>(`${ADMIN}/criteria/${id}`, input)
  return data
}

export async function getTeachers(q?: string): Promise<LaTeacherSummary[]> {
  const { data } = await api.get<LaTeacherSummary[]>(`${ADMIN}/teachers`, { params: clean({ q }) })
  return data
}

export async function getRandomTeacher(): Promise<LaTeacher> {
  const { data } = await api.get<LaTeacher>(`${ADMIN}/teachers/random`)
  return data
}

export async function getTeacher(id: string): Promise<LaTeacherSummary> {
  const { data } = await api.get<LaTeacherSummary>(`${ADMIN}/teachers/${id}`)
  return data
}

/** O'qituvchining shu sanadagi darslari (jadvaldan) — formani to'ldirish uchun. */
export async function getTeacherLessons(id: string, date: string): Promise<LaTeacherLesson[]> {
  const { data } = await api.get<LaTeacherLesson[]>(`${ADMIN}/teachers/${id}/lessons`, {
    params: { date },
  })
  return data
}

const PAGE_SIZE = 500

/** Ro'yxatni oxirigacha o'qiydi — server bir so'rovda ko'pi bilan `limit` qator beradi. */
export async function getObservations(filters: ObservationFilters = {}): Promise<LaObservationListItem[]> {
  const rows: LaObservationListItem[] = []
  for (let offset = 0; ; offset += PAGE_SIZE) {
    const { data } = await api.get<LaObservationListItem[]>(`${ADMIN}/observations`, {
      params: clean({ ...filters, limit: PAGE_SIZE, offset }),
    })
    rows.push(...data)
    if (data.length < PAGE_SIZE) return rows
  }
}

export async function getObservation(id: string): Promise<LaObservation> {
  const { data } = await api.get<LaObservation>(`${ADMIN}/observations/${id}`)
  return data
}

/** Qoralama yaratadi (mezon javoblari bilan). */
export async function createObservation(input: LaObservationInput): Promise<LaObservation> {
  const { data } = await api.post<LaObservation>(`${ADMIN}/observations`, input)
  return data
}

export async function updateObservation(id: string, input: LaObservationInput): Promise<LaObservation> {
  const { data } = await api.patch<LaObservation>(`${ADMIN}/observations/${id}`, input)
  return data
}

/** Yakunlaydi va AI xulosani so'raydi (AI xatosi yakunlashni to'xtatmaydi — `aiError`). */
export async function completeObservation(id: string): Promise<LaObservation> {
  const { data } = await api.post<LaObservation>(`${ADMIN}/observations/${id}/complete`)
  return data
}

export async function regenerateAiSummary(id: string): Promise<LaObservation> {
  const { data } = await api.post<LaObservation>(`${ADMIN}/observations/${id}/ai-summary`)
  return data
}

/** Faqat admin/superadmin. */
export async function deleteObservation(id: string): Promise<void> {
  await api.delete(`${ADMIN}/observations/${id}`)
}

export async function downloadObservationPdf(o: Pick<LaObservation, 'id' | 'teacherName' | 'lessonDate'>): Promise<void> {
  await downloadFile(
    `${ADMIN}/observations/${o.id}/report.pdf`,
    {},
    `dars-tahlili-${slug(o.teacherName)}-${o.lessonDate}.pdf`,
  )
}

export async function getRatingBoard(range: DateRange = {}): Promise<LaRatingBoard> {
  const { data } = await api.get<LaRatingBoard>(`${ADMIN}/rating`, { params: clean({ ...range }) })
  return data
}

export async function downloadRatingXlsx(range: DateRange = {}): Promise<void> {
  await downloadFile(`${ADMIN}/rating.xlsx`, { ...range }, 'wunderkind-reyting.xlsx')
}

export async function getDashboard(): Promise<LaDashboard> {
  const { data } = await api.get<LaDashboard>(`${ADMIN}/dashboard`)
  return data
}

export async function getAnalytics(params: DateRange & { teacherId?: string } = {}): Promise<LaAnalytics> {
  const { data } = await api.get<LaAnalytics>(`${ADMIN}/analytics`, { params: clean({ ...params }) })
  return data
}

/* ----------------------------------------------------------------- teacher */

/** O'qituvchining o'z YAKUNLANGAN tahlillari. Sana oralig'i server qo'llamasa ham mijozda qo'llanadi. */
export async function getMyObservations(range: DateRange = {}): Promise<LaObservationListItem[]> {
  const { data } = await api.get<LaObservationListItem[]>(`${TEACHER}/observations`, {
    params: clean({ ...range }),
  })
  return data
}

export async function getMyObservation(id: string): Promise<LaObservation> {
  const { data } = await api.get<LaObservation>(`${TEACHER}/observations/${id}`)
  return data
}

export async function downloadMyObservationPdf(o: Pick<LaObservation, 'id' | 'teacherName' | 'lessonDate'>): Promise<void> {
  await downloadFile(
    `${TEACHER}/observations/${o.id}/report.pdf`,
    {},
    `dars-tahlili-${slug(o.teacherName)}-${o.lessonDate}.pdf`,
  )
}

export async function getMyAnalytics(range: DateRange = {}): Promise<LaAnalytics> {
  const { data } = await api.get<LaAnalytics>(`${TEACHER}/analytics`, { params: clean({ ...range }) })
  return data
}
