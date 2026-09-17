import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  Arxivlash sabablari katalogi va ommaviy arxivlash — §2.2.
 *
 *  IKKI USTUN, IKKI VAZIFA: `reason` (erkin matn) MAJBURIY — tafsilot;
 *  `archiveReasonId` (katalog qatori) ixtiyoriy — guruhlash uchun.
 *  "Boshqa" tanlanganda ham matn qoladi, ya'ni ma'lumot yo'qolmaydi.
 * ========================================================================= */

/** Katalogdagi bitta arxivlash sababi. */
export interface ArchiveReason {
  id: string
  name: string
  isActive: boolean
  position: number
  /** Nechta o'quvchida ishlatilgan — 0 bo'lsagina o'chirish mumkin. */
  usedBy: number
}

export interface SaveArchiveReasonInput {
  name: string
  isActive?: boolean
  position?: number
}

/** Qarzi borligi uchun arxivlanmagan o'quvchi. */
export interface ArchiveBlockedStudent {
  studentId: string
  fullName: string
  className: string
  /** Qarz summasi (musbat son). */
  debt: number
}

/**
 * Arxivlash natijasi. Qarzdor topilsa amal BUTUNLAY rad etiladi
 * (`archived = 0`) — server 400 qaytaradi va shu obyekt javob tanasida keladi.
 */
export interface BulkArchiveResult {
  archived: number
  blocked: ArchiveBlockedStudent[]
  /** Joriy foydalanuvchi (superadmin) qoidani chetlab o'ta oladimi. */
  canOverride: boolean
  message?: string | null
}

export async function getArchiveReasons(includeInactive = false): Promise<ArchiveReason[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ArchiveReason[]>('/admin/archive-reasons', {
    params: includeInactive ? { includeInactive: true } : undefined,
  })
  return data
}

export async function createArchiveReason(input: SaveArchiveReasonInput): Promise<ArchiveReason> {
  const { data } = await api.post<ArchiveReason>('/admin/archive-reasons', input)
  return data
}

export async function updateArchiveReason(
  id: string,
  input: SaveArchiveReasonInput,
): Promise<ArchiveReason> {
  const { data } = await api.put<ArchiveReason>(`/admin/archive-reasons/${id}`, input)
  return data
}

export async function deleteArchiveReason(id: string): Promise<void> {
  await api.delete(`/admin/archive-reasons/${id}`)
}

export interface BulkArchiveInput {
  studentIds: string[]
  /** Erkin matn — majburiy. */
  reason: string
  /** Katalog qatori — "Boshqa" uchun bo'sh qoldiriladi. */
  archiveReasonId?: string | null
  /** Qarzdorlik to'sig'ini chetlab o'tish (faqat superadmin). */
  force?: boolean
}

/** Bir nechta o'quvchini bitta sabab bilan arxivlaydi. */
export async function archiveManyStudents(input: BulkArchiveInput): Promise<BulkArchiveResult> {
  const { data } = await api.post<BulkArchiveResult>('/admin/students/archive-many', {
    studentIds: input.studentIds,
    reason: input.reason,
    archiveReasonId: input.archiveReasonId ?? null,
    force: input.force ?? false,
  })
  return data
}
