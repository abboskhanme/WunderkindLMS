import type { AdminDashboard } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { adminDashboardMock } from '../mock/dashboard'

/** Admin bosh sahifasi uchun barcha ma'lumotlar */
export async function getAdminDashboard(): Promise<AdminDashboard> {
  if (USE_MOCK) {
    await delay()
    return adminDashboardMock
  }
  const { data } = await api.get<AdminDashboard>('/admin/dashboard')
  return data
}
