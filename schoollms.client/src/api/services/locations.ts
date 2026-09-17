import type { StudentLocationPin } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * Joylashuvi bor faol o'quvchilarning barcha pin'lari (admin xarita uchun,
 * §2.8 L-2) — bitta o'quvchida uchtagacha (home/school/pickup).
 */
export async function getStudentLocations(className?: string): Promise<StudentLocationPin[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentLocationPin[]>('/admin/locations', {
    params: className ? { className } : undefined,
  })
  return data
}
