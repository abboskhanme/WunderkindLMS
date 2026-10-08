import type { AdminDashboard } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { adminDashboardMock } from '../mock/dashboard'

/**
 * Admin bosh sahifasi uchun barcha ma'lumotlar.
 *
 * @param from Davr boshi, "YYYY-MM-DD" (`to` bilan birga). Berilmasa — server o'z standart
 *   oynalarini qaytaradi (butun tarix bahosi, bugungi soatlar, oxirgi 30 kun).
 * @param to Davr oxiri, "YYYY-MM-DD".
 */
export async function getAdminDashboard(from?: string, to?: string): Promise<AdminDashboard> {
  if (USE_MOCK) {
    await delay()
    return adminDashboardMock
  }
  const { data } = await api.get<AdminDashboard>('/admin/dashboard', {
    params: from && to ? { from, to } : undefined,
  })
  return data
}
