import { api } from '../client'

/**
 * Sinf ro'yxati — `docs/modules/students-parity.md` §2.2 (C-1, C-2).
 *
 * MUHIM: o'quvchini sinfga bog'laydigan haqiqat manbai bugungiday
 * `students.class_name` bo'lib qoladi. Bu yerdagi har bir amal uni VA yangi
 * `class_memberships` yozuvini BIRGA yangilaydi (server, bitta tranzaksiyada).
 * Ya'ni jurnal, davomat va hisobotlar hech narsa sezmaydi — ular bugungi
 * ustunni o'qiyverdi.
 */

export interface ClassRosterRow {
  /** Bo'sh GUID = a'zolik yozuvi yo'q (eski o'quvchi) — amallar berilmaydi. */
  membershipId: string
  studentId: string
  fullName: string
  gender: string
  /** Manfiy = qarz. Faqat moliyani ko'ra oladiganlarga to'ldiriladi. */
  balance: number
  joinedOn: string
}

export interface ClassRoster {
  classId: string
  className: string
  grade: number
  students: ClassRosterRow[]
}

/** Sinfsiz o'quvchi — "sinfga qo'shish" tanlovi. */
export interface ClassCandidate {
  studentId: string
  fullName: string
  gender: string
}

/** O'quvchi kartochkasidagi sinf a'zoligi (tarix bilan). */
export interface StudentClassMembership {
  id: string
  classId: string
  className: string
  grade: number
  joinedOn: string
  leftOn: string | null
  leaveReason: string | null
  days: number
}

/** O'quvchi kartochkasidagi guruh a'zoligi (tarix bilan). */
export interface StudentGroupMembership {
  id: string
  groupId: string
  groupName: string
  /** Yo'nalish guruhida null */
  subjectId: string | null
  subjectName: string
  groupIsArchived: boolean
  joinedOn: string
  leftOn: string | null
  leaveReason: string | null
  days: number
}

export interface StudentMemberships {
  studentId: string
  /** `students.class_name` — bugungi haqiqat manbai. */
  className: string
  classes: StudentClassMembership[]
  groups: StudentGroupMembership[]
}

/** A'zolik yozuvi yo'qligini bildiruvchi bo'sh GUID. */
export const EMPTY_MEMBERSHIP = '00000000-0000-0000-0000-000000000000'

const base = '/admin/class-roster'

export async function getClassRoster(classId: string, search?: string): Promise<ClassRoster> {
  const { data } = await api.get<ClassRoster>(`${base}/${classId}`, {
    params: search ? { search } : undefined,
  })
  return data
}

/** Faqat SINFSIZ, arxivlanmagan o'quvchilar. */
export async function getClassCandidates(
  classId: string,
  search?: string,
): Promise<ClassCandidate[]> {
  const { data } = await api.get<ClassCandidate[]>(`${base}/${classId}/candidates`, {
    params: search ? { search } : undefined,
  })
  return data
}

/**
 * C-4: sig'im OGOHLANTIRISHI. Server amalni baribir bajaradi (bu yer taqiq emas) — sig'im
 * oshib ketgan bo'lsa javobda `warning` matni keladi (204 o'rniga 200), aks holda `null`.
 */
export interface CapacityWarning {
  warning: string | null
}

export async function addClassMember(classId: string, studentId: string): Promise<CapacityWarning> {
  const res = await api.post<{ warning?: string } | null>(`${base}/${classId}/members`, { studentId })
  return { warning: res.data?.warning ?? null }
}

/** Sinfdan chiqarish. Sabab MAJBURIY — a'zolik tarixi shu savolga javob beradi. */
export async function removeClassMember(membershipId: string, reason: string): Promise<void> {
  await api.post(`${base}/members/${membershipId}/remove`, { reason })
}

/**
 * Boshqa sinfga o'tkazish — AYNI DARAJADA.
 * `keepGroups` sukut bo'yicha true: yangi sinf boqmaydigan guruhlardagi
 * a'zolik faqat bayroq o'chirilganda yopiladi.
 */
export async function transferClassMember(
  membershipId: string,
  toClassId: string,
  keepGroups = true,
  reason?: string,
): Promise<CapacityWarning> {
  const res = await api.post<{ warning?: string } | null>(`${base}/members/${membershipId}/transfer`, {
    toClassId,
    keepGroups,
    reason: reason || null,
  })
  return { warning: res.data?.warning ?? null }
}

/** O'quvchi kartochkasi — "Sinf va guruhlar" tab'i (G-10). */
export async function getStudentMemberships(studentId: string): Promise<StudentMemberships> {
  const { data } = await api.get<StudentMemberships>(`/admin/students/${studentId}/memberships`)
  return data
}
