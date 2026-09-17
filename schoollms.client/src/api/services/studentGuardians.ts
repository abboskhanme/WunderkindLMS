import type {
  GuardianRelation,
  StudentFormCard,
  StudentGuardian,
  StudentGuardianInput,
} from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchining vasiylari — docs/modules/students-parity.md §2.3 (S-8).
 *
 *  Prefiks o'quvchiniki (`/admin/students/{id}/...`) va darvoza ham
 *  o'quvchiniki (`students` ruxsati). `/admin/guardians` esa "Ilova"
 *  bo'limining ekrani bo'lib qoladi — u VASIYDAN qarab ishlaydi.
 *
 *  DTO nomlari backend bilan AYNAN bir xil (camelCase).
 * ========================================================================= */

/** Tanlov ro'yxati — forma va ota-onalar ekrani shundan o'qiydi. */
export const guardianRelations: { value: GuardianRelation; label: string }[] = [
  { value: 'father', label: 'Otasi' },
  { value: 'mother', label: 'Onasi' },
  { value: 'parent', label: 'Ota-ona' },
  { value: 'grandparent', label: 'Bobosi / buvisi' },
  { value: 'trustee', label: 'Ishonchli shaxs' },
  { value: 'other', label: 'Boshqa' },
]

/** Vasiylik turining o'zbekcha nomi ("boshqa" bo'lsa — kiritilgan izoh). */
export function relationLabel(relation: string, note?: string | null): string {
  if (relation === 'other') return (note ?? '').trim() || 'Boshqa'
  return guardianRelations.find((r) => r.value === relation)?.label ?? 'Ota-ona'
}

const EMPTY_CARD: StudentFormCard = {
  studentId: '',
  phone: null,
  language: null,
  documentUrl: null,
  guardians: [],
}

/** Forma tahrirda yuklaydigan qo'shimcha ma'lumot (ro'yxat ustunlarida yo'q). */
export async function getStudentCard(id: string): Promise<StudentFormCard> {
  if (USE_MOCK) {
    await delay(150)
    return { ...EMPTY_CARD, studentId: id }
  }
  const { data } = await api.get<StudentFormCard>(`/admin/students/${id}/card`)
  return data
}

export async function getStudentGuardians(id: string): Promise<StudentGuardian[]> {
  if (USE_MOCK) {
    await delay(150)
    return []
  }
  const { data } = await api.get<StudentGuardian[]>(`/admin/students/${id}/guardians`)
  return data
}

/** Vasiy qo'shish. Telefon mavjud bo'lsa yangi qator emas, mavjudi biriktiriladi. */
export async function attachGuardian(
  studentId: string,
  input: StudentGuardianInput,
): Promise<StudentGuardian | null> {
  if (USE_MOCK) {
    await delay(200)
    return null
  }
  const { data } = await api.post<StudentGuardian>(
    `/admin/students/${studentId}/guardians`,
    input,
  )
  return data
}

export async function updateStudentGuardian(
  studentId: string,
  guardianId: string,
  input: StudentGuardianInput,
): Promise<StudentGuardian | null> {
  if (USE_MOCK) {
    await delay(200)
    return null
  }
  const { data } = await api.put<StudentGuardian>(
    `/admin/students/${studentId}/guardians/${guardianId}`,
    input,
  )
  return data
}

/** Asosiy vasiyni almashtirish — eski `parent_phone` ustuni ham shunga tenglashadi. */
export async function makeGuardianPrimary(studentId: string, guardianId: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/students/${studentId}/guardians/${guardianId}/primary`)
}

/** Vasiyni o'quvchidan uzish (vasiy qatori o'chmaydi — boshqa farzandi bo'lishi mumkin). */
export async function detachGuardian(studentId: string, guardianId: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.delete(`/admin/students/${studentId}/guardians/${guardianId}`)
}
