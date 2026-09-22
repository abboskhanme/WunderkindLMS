/**
 * Yangiliklar — admin API (SM-10, `docs/modules/sales-marketing.md` §5.4).
 *
 * `[Authorize] [AdminPerm("marketing")]`, `NewsController`,
 * `api/admin/news`:
 *
 *   GET    /api/admin/news              — ro'yxat (`state`, `page`, `pageSize`)
 *   GET    /api/admin/news/{id}         — bitta yozuv
 *   POST   /api/admin/news              — yangi qoralama
 *   PUT    /api/admin/news/{id}         — tahrirlash
 *   POST   /api/admin/news/{id}/publish — e'lon qilish (+ Telegram fan-out)
 *   POST   /api/admin/news/{id}/unpublish
 *   DELETE /api/admin/news/{id}         — yumshoq o'chirish (`deleted_at`)
 *
 * IKKI NARSA ATAYLAB SHUNDAY:
 *
 * 1. `body` — ODDIY MATN (§3.3 N2). Bu yerda ham, ekranda ham hech qanday
 *    HTML/markdown ishlovchisi yo'q: matn serverga qanday ketgan bo'lsa,
 *    shunday qaytadi va `whitespace-pre-line` bilan chiziladi. Sanitizer +
 *    uchta renderer = saqlangan XSS uchun uchta imkoniyat.
 * 2. Xato kodlari (§5.6) matnga emas, KODGA qarab ajratiladi
 *    (`newsErrorCode`), foydalanuvchiga esa o'zbekcha jumla ko'rsatiladi
 *    (`newsErrorMessage`) — `billingError.ts` dagi kabi.
 */
import { api, USE_MOCK } from '../client'

/** Kimga mo'ljallangan — API'da massiv, bazada uchta boolean (§3.3 N5). */
export type NewsAudience = 'employee' | 'parent' | 'student'

/** Ro'yxat filtri (§5.4). `archived` — yumshoq o'chirilganlar (N8). */
export type NewsState = 'all' | 'draft' | 'published' | 'archived'

/** `NewsAdminDto` — §5.4. `publishedAt === null` — qoralama. */
export interface NewsAdminDto {
  id: string
  title: string
  body: string
  imageUrl: string | null
  audience: NewsAudience[]
  publishedAt: string | null
  authorName: string
  telegramSentAt: string | null
  telegramRecipientCount: number
  telegramSentCount: number
  createdAt: string
  updatedAt: string | null
}

/** `NewsSaveRequest` — §5.4. `audience` bo'sh bo'lmasligi kerak. */
export interface NewsSaveRequest {
  title: string
  body: string
  imageUrl: string | null
  audience: NewsAudience[]
}

export interface NewsListResult {
  total: number
  rows: NewsAdminDto[]
}

export interface NewsListQuery {
  state: NewsState
  /** 1 dan boshlanadi. */
  page: number
  pageSize: number
}

/** `GET /api/admin/news` */
export async function listNews(query: NewsListQuery): Promise<NewsListResult> {
  if (USE_MOCK) return { total: 0, rows: [] }
  const { data } = await api.get<NewsListResult>('/admin/news', {
    params: { state: query.state, page: query.page, pageSize: query.pageSize },
  })
  return data
}

/**
 * `GET /api/admin/news/{id}`
 *
 * Tahrirlash ochilganda ro'yxatdagi nusxa emas, SERVERDAGI holat olinadi:
 * ro'yxat ochiq turganda boshqa xodim uni e'lon qilib yoki o'chirib
 * yuborgan bo'lishi mumkin, va shakl eskirgan holatni saqlab yubormasligi
 * kerak.
 */
export async function getNews(id: string): Promise<NewsAdminDto> {
  const { data } = await api.get<NewsAdminDto>(`/admin/news/${id}`)
  return data
}

/** `POST /api/admin/news` — har doim qoralama qaytadi. */
export async function createNews(req: NewsSaveRequest): Promise<NewsAdminDto> {
  const { data } = await api.post<NewsAdminDto>('/admin/news', req)
  return data
}

/**
 * `PUT /api/admin/news/{id}`
 *
 * E'lon qilingan yangilikda QATNASHUVCHI o'zgartirilsa server 409
 * `news_published` qaytaradi: birinchi auditoriyaga ketgan nusxa joyida
 * qolar edi.
 */
export async function updateNews(id: string, req: NewsSaveRequest): Promise<NewsAdminDto> {
  const { data } = await api.put<NewsAdminDto>(`/admin/news/${id}`, req)
  return data
}

/**
 * `POST /api/admin/news/{id}/publish`
 *
 * Ikki natija, bitta amal (§3.3 N4): lentada qoladigan yozuv va Telegram
 * xabari. Qaytgan DTO'da uchta hisoblagich to'ladi. Ikkinchi marta chaqirilsa
 * server 409 `news_already_published` beradi — xabar ikki marta ketmaydi.
 */
export async function publishNews(id: string, sendTelegram: boolean): Promise<NewsAdminDto> {
  const { data } = await api.post<NewsAdminDto>(`/admin/news/${id}/publish`, { sendTelegram })
  return data
}

/**
 * `POST /api/admin/news/{id}/unpublish`
 *
 * Lentadan yashiradi. KETGAN Telegram xabarini qaytarib bo'lmaydi — ekran
 * buni tasdiqdan oldin aytadi (§3.3 N3).
 */
export async function unpublishNews(id: string): Promise<NewsAdminDto> {
  const { data } = await api.post<NewsAdminDto>(`/admin/news/${id}/unpublish`)
  return data
}

/** `DELETE /api/admin/news/{id}` — yumshoq: `deleted_at` qo'yiladi (N8). */
export async function deleteNews(id: string): Promise<void> {
  await api.delete(`/admin/news/${id}`)
}

/* ------------------------------------------------------------------ */
/*  Xatolar — §5.6                                                     */
/* ------------------------------------------------------------------ */

interface NewsErrorBody {
  code?: string
  message?: string
}

interface HttpErrorShape {
  response?: { status?: number; data?: NewsErrorBody | string | null }
  message?: string
}

function asHttpError(err: unknown): HttpErrorShape {
  return typeof err === 'object' && err !== null ? (err as HttpErrorShape) : {}
}

function body(err: unknown): NewsErrorBody | null {
  const data = asHttpError(err).response?.data
  return typeof data === 'object' && data !== null ? data : null
}

/** HTTP holati (tarmoq uzilsa — undefined). */
export function newsErrorStatus(err: unknown): number | undefined {
  return asHttpError(err).response?.status
}

/**
 * Mashina o'qiydigan kod (§5.6): `validation`, `news_published`,
 * `news_already_published`. Shartli mantiq FAQAT shunga qaraydi.
 */
export function newsErrorCode(err: unknown): string | undefined {
  return body(err)?.code
}

/** §5.6 dagi har bir kod uchun o'zbekcha jumla. */
const CODE_MESSAGES: Record<string, string> = {
  validation: "Ma'lumotlar to'liq emas: sarlavha, matn va kamida bitta qatnashuvchi bo'lishi shart.",
  news_published:
    "Yangilik allaqachon e'lon qilingan — kimga ko'rinishini o'zgartirish uchun avval e'londan qaytaring.",
  news_already_published:
    "Bu yangilik allaqachon e'lon qilingan. Telegram xabari ikkinchi marta yuborilmaydi.",
}

/**
 * Foydalanuvchiga ko'rsatiladigan o'zbekcha xabar.
 *
 * Tartib: tanish kod → serverning o'z jumlasi → HTTP holati → zaxira matn.
 * Tanish kod BIRINCHI turadi, chunki u eng aniq jumlani beradi va server
 * matnini o'zgartirsa ham ekran o'zbekcha qoladi.
 */
export function newsErrorMessage(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  const code = newsErrorCode(err)
  if (code && CODE_MESSAGES[code]) return CODE_MESSAGES[code]

  const serverMessage = body(err)?.message
  if (serverMessage) return serverMessage

  const status = newsErrorStatus(err)
  if (status === undefined) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
  if (status === 401) return 'Sessiya tugagan — qaytadan kiring.'
  if (status === 403) return "Bu amalga ruxsatingiz yo'q."
  if (status === 404) return "Yangilik topilmadi — ehtimol u o'chirilgan."
  if (status === 400) return CODE_MESSAGES.validation
  if (status >= 500) return "Serverda xatolik. Birozdan so'ng qayta urinib ko'ring."

  return fallback
}
