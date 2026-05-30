import type { ParentRow } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/** Ota-onalar ro'yxati (telefon bo'yicha guruhlangan). Admin "Ilova → Ota-onalar" sahifasi uchun. */
export async function getParents(): Promise<ParentRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ParentRow[]>('/admin/parents')
  return data
}
