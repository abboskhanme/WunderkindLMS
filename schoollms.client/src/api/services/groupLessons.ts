import { api } from '../client'

/**
 * Guruh darslari o'chirgichi — `docs/modules/students-parity.md` §4.3.
 *
 * Bu oddiy sozlama emas, BIR MARTALIK o'tish: yoqilgan lahzada guruh
 * jadvallari haftaga biriktirila boshlaydi va jurnal, davomat, hisobot,
 * maosh hamda turniket raqamlari guruh darslarini ham ko'radi. Shuning
 * uchun yozish faqat TIZIM EGASIga (`superadmin`) ochiq va har o'zgarish
 * auditga tushadi; o'qish esa hamma admin ekraniga kerak.
 */

export interface GroupLessonsSwitch {
  /** false = guruhlar bor, lekin birorta dars/raqam ularni ko'rmaydi */
  enabled: boolean
  /** Arxivlanmagan o'quv guruhlari soni */
  activeGroups: number
  /** Guruhga tegishli jadval variantlari (qoralama) soni */
  groupTemplates: number
  /** Haftaga biriktirilgan guruh jadvallari soni (o'chiq holatda 0 bo'lishi kerak) */
  groupWeekAssignments: number
}

export async function getGroupLessonsSwitch(): Promise<GroupLessonsSwitch> {
  const { data } = await api.get<GroupLessonsSwitch>('/admin/group-lessons')
  return data
}

export async function setGroupLessonsSwitch(enabled: boolean): Promise<GroupLessonsSwitch> {
  const { data } = await api.put<GroupLessonsSwitch>('/admin/group-lessons', { enabled })
  return data
}
