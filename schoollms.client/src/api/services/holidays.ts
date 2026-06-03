import type { Holiday } from '@/types'
import { api, USE_MOCK } from '../client'

/** Bayram/dam olish kunlari (butun maktab). Bu sanalarda hech bir sinfda dars bo'lmaydi. */
export async function getHolidays(year?: number): Promise<Holiday[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Holiday[]>('/admin/holidays', {
    params: year ? { year } : undefined,
  })
  return data
}

/** Bayram kunini qo'shadi yoki nomini yangilaydi (sana bo'yicha). */
export async function saveHoliday(date: string, name: string): Promise<Holiday> {
  const { data } = await api.put<Holiday>('/admin/holidays', { date, name })
  return data
}

/** Bayram kunini olib tashlaydi. */
export async function deleteHoliday(date: string): Promise<void> {
  await api.delete('/admin/holidays', { params: { date } })
}
