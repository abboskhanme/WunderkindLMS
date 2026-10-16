import type { AuditActor, AuditLog, AuditPage, AuditSectionOption } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

export interface AuditFilters {
  /** FinanceTransaction | TeacherSalary | ClassFee */
  entityType?: string
  /** Bitta yozuv tarixi uchun */
  entityId?: string
  /** O'quvchiga oid o'zgarishlar */
  studentId?: string
  /** O'qituvchiga oid o'zgarishlar */
  teacherId?: string
  action?: string
  /** Kun (`yyyy-MM-dd`, shu kun ham kiradi) */
  from?: string
  /** Kun (`yyyy-MM-dd`, butun kun kiradi) */
  to?: string
  /** users.id; "system" — tizim yozgan qatorlar */
  actorId?: string
  /** Bo'lim kaliti; bir nechtasi vergul bilan ("finance,salary") */
  section?: string
  /** create | update | delete */
  kind?: string
  /** Izoh ichidagi matn */
  q?: string
  /** Sukut bo'yicha 100, eng ko'pi 500 */
  limit?: number
}

/** O'zgarishlar tarixini olish (filtrlar bo'yicha, vaqt kamayish tartibida) */
export async function getAuditLogs(filters: AuditFilters = {}): Promise<AuditLog[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<AuditLog[]>('/admin/audit', { params: filters })
  return data
}

/* ---------- O'zgarishlar tarixi ekrani (docs/modules/audit-log.md) ---------- */

export type AuditLogQuery = Omit<AuditFilters, 'entityType' | 'entityId' | 'limit'>

function clean(q: AuditLogQuery): Record<string, string> {
  return Object.fromEntries(Object.entries(q).filter(([, v]) => typeof v === 'string' && v.trim() !== '')) as Record<
    string,
    string
  >
}

/** Bitta sahifa (eng yangisi birinchi). `cursor` — oldingi sahifaning `nextCursor`i. */
export async function getAuditPage(q: AuditLogQuery, cursor?: string | null, limit = 50): Promise<AuditPage> {
  if (USE_MOCK) {
    await delay()
    return { items: [], total: 0, nextCursor: null }
  }
  const { data } = await api.get<AuditPage>('/admin/audit/log', {
    params: { ...clean(q), cursor: cursor ?? undefined, limit },
  })
  return data
}

export async function getAuditActors(): Promise<AuditActor[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AuditActor[]>('/admin/audit/actors')
  return data
}

export async function getAuditSections(): Promise<AuditSectionOption[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AuditSectionOption[]>('/admin/audit/sections')
  return data
}

/** Ko'rinib turgan (filtrlangan) tarix — server tuzgan .xlsx. */
export async function downloadAuditLog(q: AuditLogQuery): Promise<void> {
  if (USE_MOCK) {
    alert('Excel faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/audit/export', { params: clean(q), responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `ozgarishlar_tarixi_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
