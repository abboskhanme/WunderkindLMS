import type { Student } from '@/types'
import { delay } from '@/lib/utils'
import { getQuarterWeeks } from '@/lib/weeks'
import { api, USE_MOCK } from '../client'
import { classesMock } from '../mock/classes'
import { studentsMock } from '../mock/students'
import { settingsMock } from '../mock/settings'
import { templatesMock } from '../mock/scheduleTemplates'
import { weekAssignmentsMock } from '../mock/weekAssignments'
import { journalMock } from '../mock/journal'
import { subjectsMock } from '../mock/subjects'

export interface SubjectAttendance {
  subjectId: string
  subjectName: string
  period: number
  total: number
  present: number
  absent: number
  reasons: { name: string; count: number }[]
}

export interface DailyAttendance {
  total: number
  subjects: SubjectAttendance[]
}

function compute(classId: string, date: string): DailyAttendance {
  const cls = classesMock.find((c) => c.id === classId)
  if (!cls) return { total: 0, subjects: [] }

  const students = studentsMock.filter((s) => s.className === cls.name)
  const total = students.length

  const jsDay = new Date(date).getDay()
  if (jsDay === 0) return { total, subjects: [] } // yakshanba
  const lessonDay = jsDay - 1 // Dushanba=0 ... Shanba=5

  const q = settingsMock.quarters.find((x) => date >= x.startDate && date <= x.endDate)
  if (!q) return { total, subjects: [] }

  const week = getQuarterWeeks(q.startDate, q.endDate).find(
    (w) => date >= w.startISO && date <= w.endISO,
  )
  if (!week) return { total, subjects: [] }

  const assignment = (weekAssignmentsMock[`${classId}-${q.quarter}`] ?? []).find(
    (x) => x.week === week.week,
  )
  if (!assignment?.templateId) return { total, subjects: [] }

  const tpl = (templatesMock[classId] ?? []).find((t) => t.id === assignment.templateId)
  if (!tpl) return { total, subjects: [] }

  const dayLessons = tpl.lessons
    .filter((l) => l.day === lessonDay)
    .sort((a, b) => a.period - b.period)

  const subjects: SubjectAttendance[] = dayLessons.map((l) => {
    const entries = (journalMock[`${classId}-${l.subjectId}-${q.quarter}`] ?? []).filter(
      (e) => e.date === date && e.reasonId,
    )
    const reasonCount: Record<string, number> = {}
    entries.forEach((e) => {
      if (e.reasonId) reasonCount[e.reasonId] = (reasonCount[e.reasonId] ?? 0) + 1
    })
    const reasons = Object.entries(reasonCount).map(([rid, count]) => ({
      name: settingsMock.absenceReasons.find((r) => r.id === rid)?.name ?? '?',
      count,
    }))
    const absent = entries.length
    return {
      subjectId: l.subjectId,
      subjectName: subjectsMock.find((s) => s.id === l.subjectId)?.name ?? '',
      period: l.period,
      total,
      present: total - absent,
      absent,
      reasons,
    }
  })

  return { total, subjects }
}

export async function getDailyAttendance(classId: string, date: string): Promise<DailyAttendance> {
  if (USE_MOCK) {
    await delay()
    return compute(classId, date)
  }
  const { data } = await api.get<DailyAttendance>('/admin/attendance', {
    params: { classId, date },
  })
  return data
}

export interface StudentStatus {
  student: Student
  absent: boolean
  reasonName?: string
}

/** Bitta fan/kun bo'yicha har bir o'quvchining holati */
export async function getSubjectAttendanceDetail(
  classId: string,
  subjectId: string,
  date: string,
): Promise<StudentStatus[]> {
  if (USE_MOCK) {
    await delay()
    const cls = classesMock.find((c) => c.id === classId)
    if (!cls) return []
    const students = studentsMock.filter((s) => s.className === cls.name)
    const q = settingsMock.quarters.find((x) => date >= x.startDate && date <= x.endDate)
    const entries = q
      ? (journalMock[`${classId}-${subjectId}-${q.quarter}`] ?? []).filter(
          (e) => e.date === date && e.reasonId,
        )
      : []
    return students.map((s) => {
      const e = entries.find((x) => x.studentId === s.id)
      return {
        student: s,
        absent: !!e,
        reasonName: e?.reasonId
          ? settingsMock.absenceReasons.find((r) => r.id === e.reasonId)?.name
          : undefined,
      }
    })
  }
  const { data } = await api.get<StudentStatus[]>('/admin/attendance/subject', {
    params: { classId, subjectId, date },
  })
  return data
}
