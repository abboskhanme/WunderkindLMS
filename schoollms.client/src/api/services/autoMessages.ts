/**
 * Avtomatik Telegram xabarlari — ota-onalarga qoida bo'yicha (docs/modules/auto-messages.md,
 * 2026-10-13). Har qoida alohida yoqiladi; har biri sukut bo'yicha O'CHIQ.
 * 2026-10-15: yuborishlar (rejalashtirilgan / tasdiq kutmoqda / yuborildi / to'xtatilgan),
 * tasdiqlash, yetkazish jurnali (sahifalab, Excel) va statistika; to'lov qoidasi.
 * 2026-10-25: qoidaga rasm (10 tagacha) — matn rasm ostida ketadi; to'lov qoidasidan tashqari.
 * 2026-10-26: xodim o'zi shablon yaratadi (kalit "c_…") — faqat taqvim bo'yicha: bir marta, har kuni,
 * haftaning tanlangan kunlari yoki har oy; hamma o'quvchilarga yoki tanlangan sinflarga.
 */
import { api, USE_MOCK } from '../client'

/** Asosiy qoidalar; xodim yaratgan shablon kaliti — "c_" va 8 belgi. */
export type BuiltInAutoMessageKey = 'absence' | 'invoice' | 'debt' | 'birthday' | 'payment'
export type AutoMessageKey = BuiltInAutoMessageKey | `c_${string}`

export type AutoMessageSchedule = 'once' | 'daily' | 'weekly' | 'monthly'

/** E'londagi "Kimga yuborish" bilan bir xil. */
export type AutoMessageAudience = 'all' | 'class' | 'group' | 'selected'

export interface AutoMessageClass {
  id: string
  name: string
  grade: number
}

export interface AutoMessageToken {
  token: string
  meaning: string
  /** Mijozning so'zi: "Ism", "Sana", "Vaqt", "To'lov summasi". */
  label: string | null
}

export interface AutoMessageRule {
  key: AutoMessageKey
  enabled: boolean
  sendTime: string
  dayOfMonth: number
  minAmount: number
  text: string
  monthly: boolean
  tokens: AutoMessageToken[]
  defaultText: string
  lastPeriod: string | null
  lastRunAt: string | null
  lastSent: number
  lastFailed: number
  lastNoTelegram: number
  updatedAt: string
  updatedByName: string | null
  requiresApproval: boolean
  cutoffTime: string | null
  /** To'lov qoidasi: chek bilan birga ketadi, o'z vaqti yo'q. */
  eventDriven: boolean
  /** Xabar bilan ketadigan rasmlar (`/uploads/…`); bo'sh — faqat matn. */
  imageUrls?: string[] | null
  /** Xodim yaratgan shablon; quyidagi maydonlar faqat unda to'ladi. */
  custom?: boolean
  title?: string | null
  schedule?: AutoMessageSchedule | null
  /** Bir marta: "yyyy-MM-dd". */
  sendDate?: string | null
  /** Har hafta: 1 = Dushanba … 7 = Yakshanba. */
  weekDays?: number[] | null
  /** Kimga — e'londagi kabi: barcha ota-onalar, sinf, guruh yoki tanlab. */
  audience?: AutoMessageAudience | null
  /** "Sinf bo'yicha": sinflar id'si. */
  classIds?: string[] | null
  /** "Guruh bo'yicha": o'quv guruhi id'si. */
  groupId?: string | null
  /** "Tanlab": o'quvchilar id'si. */
  studentIds?: string[] | null
  /** Faqat qarzdorlar ("Tanlab"da emas). */
  onlyDebtors?: boolean
  /** O'chirilgan shablon — faqat tarixda nomi ko'rinishi uchun keladi. */
  archived?: boolean
}

export interface SaveAutoMessageRule {
  enabled: boolean
  sendTime: string
  dayOfMonth: number
  minAmount: number
  text: string
  requiresApproval?: boolean
  cutoffTime?: string | null
  /** Berilmasa — o'zgarmaydi; bo'sh ro'yxat — rasmsiz. To'lov qoidasida e'tiborga olinmaydi. */
  imageUrls?: string[]
  /** Faqat shablonda; berilmasa — o'zgarmaydi. */
  title?: string
  schedule?: AutoMessageSchedule
  sendDate?: string | null
  weekDays?: number[]
  audience?: AutoMessageAudience
  classIds?: string[]
  groupId?: string | null
  studentIds?: string[]
  onlyDebtors?: boolean
}

export interface AutoMessagePreview {
  key: AutoMessageKey
  period: string
  pupils: number
  withTelegram: number
  alreadySent: number
  sample: string | null
  sampleFor: string | null
}

export type AutoMessageLogStatus = 'scheduled' | 'pending_approval' | 'sent' | 'failed' | 'no_telegram' | 'cancelled'
export type AutoMessageRunStatus = 'scheduled' | 'pending_approval' | 'sending' | 'done' | 'cancelled'

export interface AutoMessageLogRow {
  id: string
  ruleKey: AutoMessageKey
  period: string
  studentId: string
  fullName: string
  className: string
  chatId: number | null
  parentName: string | null
  status: AutoMessageLogStatus
  text: string
  at: string
  runId: string | null
  refId: string | null
  source: 'auto' | 'manual'
  sentByName: string | null
  attempts: number
  error: string | null
  plannedAt: string | null
}

export interface AutoMessageLogPage {
  total: number
  page: number
  pageSize: number
  rows: AutoMessageLogRow[]
}

export interface AutoMessageLogFilters {
  rule?: string
  from?: string
  to?: string
  status?: string
  source?: string
  q?: string
  runId?: string
}

export interface AutoMessageRun {
  id: string
  ruleKey: AutoMessageKey
  period: string
  status: AutoMessageRunStatus
  plannedAt: string
  pupils: number
  sent: number
  failed: number
  noTelegram: number
  pending: number
  cancelled: number
  note: string | null
  approvedByName: string | null
  approvedAt: string | null
  cancelledByName: string | null
  cancelledAt: string | null
  startedAt: string | null
  finishedAt: string | null
  /** Yuborish tayyorlangandagi qoida rasmlari — aynan shular ketadi. */
  imageUrls?: string[] | null
}

export interface AutoMessageStatsCell {
  day: string
  ruleKey: AutoMessageKey
  status: AutoMessageLogStatus
  count: number
}

export interface AutoMessageStats {
  from: string
  to: string
  totals: Partial<Record<AutoMessageLogStatus, number>>
  byRule: AutoMessageStatsCell[]
  cells: AutoMessageStatsCell[]
}

export async function getAutoMessageRules(): Promise<AutoMessageRule[]> {
  const { data } = await api.get<AutoMessageRule[]>('/admin/auto-messages')
  return data
}

export async function saveAutoMessageRule(key: AutoMessageKey, body: SaveAutoMessageRule): Promise<AutoMessageRule> {
  const { data } = await api.put<AutoMessageRule>(`/admin/auto-messages/${key}`, body)
  return data
}

export async function createAutoMessageRule(body: SaveAutoMessageRule): Promise<AutoMessageRule> {
  const { data } = await api.post<AutoMessageRule>('/admin/auto-messages', body)
  return data
}

/** Shablonni o'chiradi (tarixi saqlanadi). Asosiy qoidalar o'chirilmaydi. */
export async function deleteAutoMessageRule(key: AutoMessageKey): Promise<void> {
  await api.delete(`/admin/auto-messages/${key}`)
}

export async function getAutoMessageClasses(): Promise<AutoMessageClass[]> {
  const { data } = await api.get<AutoMessageClass[]>('/admin/auto-messages/classes')
  return data
}

export async function previewAutoMessage(key: AutoMessageKey, text?: string): Promise<AutoMessagePreview> {
  const { data } = await api.post<AutoMessagePreview>(`/admin/auto-messages/${key}/preview`, { text: text ?? null })
  return data
}

export async function getAutoMessageLog(params: {
  rule?: string
  from?: string
  to?: string
  status?: string
}): Promise<AutoMessageLogRow[]> {
  const { data } = await api.get<AutoMessageLogRow[]>('/admin/auto-messages/log', { params })
  return data
}

function clean(filters: AutoMessageLogFilters) {
  return {
    rule: filters.rule || undefined,
    from: filters.from || undefined,
    to: filters.to || undefined,
    status: filters.status || undefined,
    source: filters.source || undefined,
    q: filters.q?.trim() || undefined,
    runId: filters.runId || undefined,
  }
}

export async function getAutoMessageLogPage(
  filters: AutoMessageLogFilters,
  page: number,
  pageSize: number,
): Promise<AutoMessageLogPage> {
  const { data } = await api.get<AutoMessageLogPage>('/admin/auto-messages/log/paged', {
    params: { ...clean(filters), page, pageSize },
  })
  return data
}

/** Jurnalni Excel (.xlsx) ga yuklab oladi — ekrandagi filtrlar bilan bir xil qatorlar. */
export async function downloadAutoMessageLog(filters: AutoMessageLogFilters): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/auto-messages/log/export', { params: clean(filters), responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? `avto-xabarlar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export async function getAutoMessageRuns(params: {
  from?: string
  to?: string
  rule?: string
  status?: string
}): Promise<AutoMessageRun[]> {
  const { data } = await api.get<AutoMessageRun[]>('/admin/auto-messages/runs', {
    params: {
      from: params.from || undefined,
      to: params.to || undefined,
      rule: params.rule || undefined,
      status: params.status || undefined,
    },
  })
  return data
}

export async function approveAutoMessageRun(id: string): Promise<AutoMessageRun> {
  const { data } = await api.post<AutoMessageRun>(`/admin/auto-messages/runs/${id}/approve`, {})
  return data
}

export async function cancelAutoMessageRun(id: string): Promise<AutoMessageRun> {
  const { data } = await api.post<AutoMessageRun>(`/admin/auto-messages/runs/${id}/cancel`, {})
  return data
}

export async function excludeFromAutoMessageRun(id: string, studentId: string): Promise<AutoMessageRun> {
  const { data } = await api.post<AutoMessageRun>(`/admin/auto-messages/runs/${id}/exclude`, { studentId })
  return data
}

export async function getAutoMessageStats(from: string, to: string): Promise<AutoMessageStats> {
  const { data } = await api.get<AutoMessageStats>('/admin/auto-messages/stats', { params: { from, to } })
  return data
}
