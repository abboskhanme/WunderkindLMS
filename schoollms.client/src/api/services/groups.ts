import { api } from '../client'

/**
 * O'quv guruhlari — `docs/modules/students-parity.md` §2.1 (G-6, G-8).
 *
 * Guruh = bir yoki bir nechta SINFDAN yig'ilgan, BITTA fan bo'yicha
 * o'qiydigan o'quvchilar. Sinf ichidagi 1/2 bo'linish (`ClassGroupsModal`)
 * bilan aralashtirmang: u bitta sinf ichidagi narsa, bu esa sinflardan
 * YUQORIDA turadi.
 *
 * Bu slice'da guruh DARSLARI yo'q — `school_meta.group_lessons_enabled`
 * o'chiq. Jadval, jurnal, davomat va maosh bugungiday ishlaydi.
 */

/** Guruhni boqadigan sinf. */
export interface StudyGroupClassRef {
  id: string
  name: string
  grade: number
}

/** Guruh o'qituvchisi. */
export interface StudyGroupTeacherRef {
  id: string
  fullName: string
}

/** Ro'yxatdagi bitta guruh. */
export interface StudyGroupListItem {
  id: string
  name: string
  subjectId: string
  subjectName: string
  /** 'male' | 'female' | null (aralash) */
  gender: string | null
  isArchived: boolean
  archivedAt: string | null
  classes: StudyGroupClassRef[]
  teachers: StudyGroupTeacherRef[]
  /** FAOL a'zolar soni */
  memberCount: number
  /** Yo'nalish guruhi — kechki dars va yotoqxona davomati shu guruhlar bo'yicha */
  isTrack?: boolean
}

/** Guruhdagi bitta a'zolik. `leftOn` null = hozir ham guruhda. */
export interface StudyGroupMember {
  id: string
  studentId: string
  fullName: string
  /** O'quvchining BUGUNGI sinfi */
  className: string
  gender: string
  joinedOn: string
  leftOn: string | null
  leaveReason: string | null
}

/** Guruhning to'liq kartochkasi — forma uchun. */
export interface StudyGroupDetail extends StudyGroupListItem {
  members: StudyGroupMember[]
}

/**
 * Ro'yxatga qo'shish nomzodi. `currentGroupId` to'ldirilgan bo'lsa —
 * o'quvchi SHU FAN bo'yicha allaqachon boshqa guruhda va tanlab bo'lmaydi.
 */
export interface GroupCandidate {
  studentId: string
  fullName: string
  classId: string
  className: string
  gender: string
  currentGroupId: string | null
  currentGroupName: string | null
}

export interface SaveGroupPayload {
  name: string
  subjectId: string
  classIds: string[]
  teacherIds: string[]
  gender?: string | null
  /**
   * Berilsa — FAOL ro'yxatning to'liq holati (ro'yxatda yo'qlari yopiladi).
   * Berilmasa ro'yxatga umuman tegilmaydi.
   */
  studentIds?: string[]
  /** Yo'nalish guruhi (kechki/yotoqxona davomati). Berilmasa — o'zgarmaydi. */
  isTrack?: boolean
}

export interface GroupFilters {
  search?: string
  /** Sinf darajalari, masalan [5, 6] */
  grades?: number[]
  subjectId?: string
  teacherId?: string
  archived?: boolean
}

const base = '/admin/study-groups'

export async function getGroups(filters: GroupFilters = {}): Promise<StudyGroupListItem[]> {
  const { data } = await api.get<StudyGroupListItem[]>(base, {
    params: {
      search: filters.search || undefined,
      grades: filters.grades?.length ? filters.grades.join(',') : undefined,
      subjectId: filters.subjectId || undefined,
      teacherId: filters.teacherId || undefined,
      archived: filters.archived ? true : undefined,
    },
  })
  return data
}

export async function getGroup(id: string): Promise<StudyGroupDetail> {
  const { data } = await api.get<StudyGroupDetail>(`${base}/${id}`)
  return data
}

export async function getGroupMembers(
  id: string,
  includeHistory = false,
): Promise<StudyGroupMember[]> {
  const { data } = await api.get<StudyGroupMember[]>(`${base}/${id}/members`, {
    params: includeHistory ? { includeHistory: true } : undefined,
  })
  return data
}

/** Chap panel nomzodlari. Sinf VA fan berilmaguncha bo'sh qaytadi. */
export async function getGroupCandidates(params: {
  classIds: string[]
  subjectId: string
  gender?: string | null
  excludeGroupId?: string
}): Promise<GroupCandidate[]> {
  if (!params.subjectId || params.classIds.length === 0) return []
  const { data } = await api.get<GroupCandidate[]>(`${base}/candidates`, {
    params: {
      classIds: params.classIds.join(','),
      subjectId: params.subjectId,
      gender: params.gender || undefined,
      excludeGroupId: params.excludeGroupId || undefined,
    },
  })
  return data
}

export async function createGroup(payload: SaveGroupPayload): Promise<StudyGroupDetail> {
  const { data } = await api.post<StudyGroupDetail>(base, payload)
  return data
}

export async function updateGroup(
  id: string,
  payload: SaveGroupPayload,
): Promise<StudyGroupDetail> {
  const { data } = await api.put<StudyGroupDetail>(`${base}/${id}`, payload)
  return data
}

/** Arxivlash — faol a'zoliklar yopiladi, tarix qoladi. */
export async function archiveGroup(id: string): Promise<void> {
  await api.post(`${base}/${id}/archive`)
}

export async function unarchiveGroup(id: string): Promise<void> {
  await api.post(`${base}/${id}/unarchive`)
}

/**
 * Nusxalash. `copyMembers` faqat manba guruh ARXIVLANGAN bo'lsa ishlaydi —
 * aks holda bola bir vaqtda ikki guruhda bo'lib qolardi.
 */
export async function duplicateGroup(
  id: string,
  payload: { name: string; teacherIds?: string[]; copyMembers?: boolean },
): Promise<StudyGroupDetail> {
  const { data } = await api.post<StudyGroupDetail>(`${base}/${id}/duplicate`, payload)
  return data
}

export async function addGroupMembers(id: string, studentIds: string[]): Promise<void> {
  await api.post(`${base}/${id}/members`, { studentIds })
}

export async function removeGroupMember(
  groupId: string,
  memberId: string,
  reason?: string,
): Promise<void> {
  await api.post(`${base}/${groupId}/members/${memberId}/remove`, { reason: reason || null })
}

/** O'tkazish — FAQAT ayni fandagi boshqa guruhga. */
export async function transferGroupMember(
  memberId: string,
  toGroupId: string,
  reason?: string,
): Promise<void> {
  await api.post(`${base}/members/${memberId}/transfer`, { toGroupId, reason: reason || null })
}
