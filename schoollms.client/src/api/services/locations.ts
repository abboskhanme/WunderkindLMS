import type { StudentLocationRow } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/** Joylashuvi bor faol o'quvchilar ro'yxati (admin xarita uchun). */
export async function getStudentLocations(className?: string): Promise<StudentLocationRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentLocationRow[]>('/admin/locations', {
    params: className ? { className } : undefined,
  })
  return data
}
