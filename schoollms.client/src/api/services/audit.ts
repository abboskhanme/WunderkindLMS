import type { AuditLog } from '@/types'
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
  from?: string
  to?: string
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
