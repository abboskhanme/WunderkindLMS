import { api } from '../client'
import type { LessonOwnerKind } from '@/types'

/**
 * Dars jadvali, davomat va jurnal tanlagichlari uchun egalar ro'yxati — BITTA qoida,
 * serverda (docs/modules/track-groups-as-classes.md):
 *
 *  1. yo'nalish guruhini boqmaydigan sinflar — daraja, keyin nom bo'yicha;
 *  2. faol YO'NALISH guruhlari — nom bo'yicha (9–11-sinflar o'rnida);
 *  3. faol oddiy guruhlar — faqat "Guruh darslari" o'chirgichi yoqilganda.
 *
 * 9–11-sinflar (9-A ...) bu ro'yxatda yo'q — ular hujjat, moliya va hisobotlar uchun
 * "Sinflar" bo'limida qoladi.
 */
export interface LessonOwnerItem {
  id: string
  name: string
  kind: LessonOwnerKind
  /** Sinf darajasi; guruh uchun 0 */
  grade: number
  isTrack: boolean
  /** Oddiy guruhning fani; sinf va yo'nalish guruhida null */
  subjectId: string | null
  /** Guruhni boqadigan sinflar (sinf uchun bo'sh) */
  classIds: string[]
}

export async function getLessonOwners(): Promise<LessonOwnerItem[]> {
  const { data } = await api.get<LessonOwnerItem[]>('/admin/schedule/owners')
  return data
}
