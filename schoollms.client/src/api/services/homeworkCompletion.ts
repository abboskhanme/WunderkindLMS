import type { LessonOwnerKind } from '@/types'
import type { HomeworkTally } from '@/lib/homeworkStatus'
import { api, USE_MOCK } from '../client'

/**
 * "Uy vazifasi bajarilishi" — pupil × lesson table of homework statuses
 * (docs/modules/homework-completion.md). The admin and the teacher call the same shape behind their
 * own gates: `/admin/journal/homework` (journal key) and `/teacher/journal/homework` (own class +
 * subject, like the teacher journal).
 */
export type HomeworkScope = 'admin' | 'teacher'

/** One lesson column; `homework` is the text set in that lesson (its lesson note). */
export interface HomeworkLesson {
  date: string
  period: number
  /** 0 = whole class, 1/2 = half of a split lesson */
  subGroup: number
  /** Conducted or attendance taken */
  held: boolean
  topic?: string | null
  homework?: string | null
  hasHomework: boolean
  summary: HomeworkTally
}

export interface HomeworkCell {
  /** 0..5 — see lib/homeworkStatus */
  status: number
  /** Not stored: Kelmadi shown from a non-late absence reason */
  derived: boolean
  /** false — the pupil is in the other half of a split lesson */
  inLesson: boolean
}

export interface HomeworkRow {
  studentId: string
  fullName: string
  subGroup: number
  /** Aligned with `HomeworkSheet.lessons` */
  cells: HomeworkCell[]
  summary: HomeworkTally
}

export interface HomeworkSheet {
  ownerId: string
  ownerKind: LessonOwnerKind
  ownerName: string
  isTrack: boolean
  subjectId: string
  subjectName: string
  from: string
  to: string
  lessons: HomeworkLesson[]
  rows: HomeworkRow[]
  total: HomeworkTally
}

export interface HomeworkMark {
  studentId: string
  date: string
  period: number
  subGroup: number
  status: number
}

export interface HomeworkSheetQuery {
  ownerId: string
  subjectId: string
  from: string
  to: string
}

const base = (scope: HomeworkScope) =>
  scope === 'admin' ? '/admin/journal/homework' : '/teacher/journal/homework'

const emptyTally: HomeworkTally = { done: 0, partial: 0, notDone: 0, late: 0, absent: 0, marked: 0, percent: null }

export async function getHomeworkSheet(scope: HomeworkScope, q: HomeworkSheetQuery): Promise<HomeworkSheet> {
  if (USE_MOCK)
    return {
      ownerId: q.ownerId, ownerKind: 'class', ownerName: '', isTrack: false, subjectId: q.subjectId,
      subjectName: '', from: q.from, to: q.to, lessons: [], rows: [], total: emptyTally,
    }
  const { data } = await api.get<HomeworkSheet>(base(scope), { params: q })
  return data
}

/** Bulk write for one owner + subject; only the homework mark of each cell changes. */
export async function saveHomeworkMarks(
  scope: HomeworkScope,
  ownerId: string,
  subjectId: string,
  marks: HomeworkMark[],
): Promise<void> {
  if (USE_MOCK) return
  await api.put(base(scope), { ownerId, subjectId, marks })
}

/** The shown table as .xlsx (server-built, same filters). */
export async function downloadHomeworkSheet(scope: HomeworkScope, q: HomeworkSheetQuery): Promise<void> {
  if (USE_MOCK) {
    alert('Excel faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get(`${base(scope)}/export`, { params: q, responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `uy-vazifasi_${q.from}_${q.to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
