import type { GuardianRelation } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  "Ilova → Ota-onalar" ekranidan vasiyni tahrirlash va unga farzand
 *  biriktirish (§2.9, P-4).
 *
 *  Endpointlar ALLAQACHON bor edi (`AdminGuardiansController`, `app`
 *  ruxsati) — ularga ekran yo'q edi. Shuning uchun bu yerda faqat mijoz
 *  tarafi qo'shiladi, serverda bir qator ham o'zgarmadi.
 * ========================================================================= */

/** Vasiy tahrirlash so'rovi. */
export interface SaveGuardianRequest {
  fullName: string
  phone: string
  passportUrl?: string | null
}

/** Vasiyga farzand biriktirish. */
export interface AttachChildRequest {
  studentId: string
  relation?: GuardianRelation
  isPrimary: boolean
}

export async function saveGuardian(id: string, req: SaveGuardianRequest): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/guardians/${id}`, req)
}

export async function attachGuardianChild(id: string, req: AttachChildRequest): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/guardians/${id}/children`, req)
}

export async function detachGuardianChild(id: string, studentId: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/guardians/${id}/children/${studentId}`)
}
