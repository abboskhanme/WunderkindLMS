import type { JournalColumn, JournalEntry, JournalTopic, QuarterGradeRow } from '@/types'
import { delay } from '@/lib/utils'
import { getQuarterWeeks, addDaysISO, mondayOfISO } from '@/lib/weeks'
import { api, USE_MOCK } from '../client'
import { templatesMock } from '../mock/scheduleTemplates'
import { weekAssignmentsMock } from '../mock/weekAssignments'
import { settingsMock } from '../mock/settings'
import { journalMock } from '../mock/journal'
import { journalTopicsMock } from '../mock/journalTopics'

const gkey = (classId: string, subjectId: string, quarter: number) =>
  `${classId}-${subjectId}-${quarter}`

/** Fanning chorakdagi darslari (sana + dars raqami + guruh) — raspisaniyadan hisoblanadi */
function computeColumns(classId: string, subjectId: string, quarter: number): JournalColumn[] {
  const q = settingsMock.quarters.find((x) => x.quarter === quarter)
  if (!q) return []
  const weeks = getQuarterWeeks(q.startDate, q.endDate)
  const assignments = weekAssignmentsMock[`${classId}-${quarter}`] ?? []
  const templates = templatesMock[classId] ?? []

  const cols: JournalColumn[] = []
  weeks.forEach((w) => {
    const a = assignments.find((x) => x.week === w.week)
    if (!a?.templateId) return
    const tpl = templates.find((t) => t.id === a.templateId)
    if (!tpl) return
    tpl.lessons
      .filter((l) => l.subjectId === subjectId)
      .forEach((l) => {
        const d = addDaysISO(mondayOfISO(w.startISO), l.day)
        if (d >= q.startDate && d <= q.endDate)
          cols.push({ date: d, period: l.period, subGroup: l.subGroup ?? 0 })
      })
  })
  const seen = new Set<string>()
  return cols
    .filter((c) => {
      const k = `${c.date}|${c.period}|${c.subGroup}`
      if (seen.has(k)) return false
      seen.add(k)
      return true
    })
    .sort((a, b) =>
      a.date === b.date
        ? a.period === b.period
          ? (a.subGroup ?? 0) - (b.subGroup ?? 0)
          : a.period - b.period
        : a.date < b.date
          ? -1
          : 1,
    )
}

/** Berilgan sanada o'tilgan darslar (sinf+fan+dars raqami+guruh) — bosh sahifada yashil/qizil ko'rsatish uchun */
export interface ConductedLesson {
  classId: string
  subjectId: string
  period: number
  subGroup: number
}

export async function getConductedLessons(date: string): Promise<ConductedLesson[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ConductedLesson[]>('/admin/journal/conducted', {
    params: { date },
  })
  return data
}

export async function getJournalColumns(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalColumn[]> {
  if (USE_MOCK) {
    await delay()
    return computeColumns(classId, subjectId, quarter)
  }
  const { data } = await api.get<JournalColumn[]>('/admin/journal/columns', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function getJournalEntries(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalEntry[]> {
  if (USE_MOCK) {
    await delay()
    return journalMock[gkey(classId, subjectId, quarter)] ?? []
  }
  const { data } = await api.get<JournalEntry[]>('/admin/journal', {
    params: { classId, subjectId, quarter },
  })
  return data
}

interface EntryPayload {
  /** null — bahoni o'chiradi; berilmasa o'zgartirmaydi (API'da ikkalasi ham yuboriladi) */
  grade?: number | null
  /** null — davomat sababini o'chiradi */
  reasonId?: string | null
  /** Uyga vazifa: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi */
  homework?: number
  /** Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon */
  behavior?: number
  /** O'zlashtirish foizi 0-100; null = tozalash */
  mastery?: number | null
}

/** Bitta katakni belgilash — baho yoki davomat sababi (sana + dars raqami bo'yicha) */
export async function setJournalEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
  payload: EntryPayload,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = (journalMock[k] ??= [])
    const entry: JournalEntry = {
      studentId, date, period,
      grade: payload.grade ?? undefined,
      reasonId: payload.reasonId ?? undefined,
      homework: payload.homework ?? 0,
      behavior: payload.behavior ?? 0,
      mastery: payload.mastery ?? null,
    }
    const i = arr.findIndex((e) => e.studentId === studentId && e.date === date && e.period === period)
    if (i >= 0) arr[i] = entry
    else arr.push(entry)
    return
  }
  await api.put('/admin/journal', { classId, subjectId, quarter, studentId, date, period, ...payload })
}

export async function clearJournalEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = journalMock[k]
    if (arr)
      journalMock[k] = arr.filter(
        (e) => !(e.studentId === studentId && e.date === date && e.period === period),
      )
    return
  }
  await api.delete('/admin/journal', {
    params: { classId, subjectId, quarter, studentId, date, period },
  })
}

/* ---------- Mavzu va uyga vazifa ---------- */

export async function getLessonNotes(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalTopic[]> {
  if (USE_MOCK) {
    await delay()
    return journalTopicsMock[gkey(classId, subjectId, quarter)] ?? []
  }
  const { data } = await api.get<JournalTopic[]>('/admin/journal/notes', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function setLessonNote(
  classId: string,
  subjectId: string,
  quarter: number,
  date: string,
  period: number,
  topic: string,
  homework: string,
  conducted: boolean,
  subGroup = 0,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = (journalTopicsMock[k] ??= [])
    const i = arr.findIndex(
      (t) => t.date === date && t.period === period && (t.subGroup ?? 0) === subGroup,
    )
    if (topic.trim() === '' && homework.trim() === '' && !conducted) {
      if (i >= 0) arr.splice(i, 1)
    } else if (i >= 0) {
      arr[i] = { date, period, topic, homework, conducted, subGroup }
    } else {
      arr.push({ date, period, topic, homework, conducted, subGroup })
    }
    return
  }
  await api.put('/admin/journal/notes', {
    classId,
    subjectId,
    quarter,
    date,
    period,
    topic,
    homework,
    conducted,
    subGroup,
  })
}

/* ---------- Chorak (yakuniy) bahosi ---------- */

const quarterGradesMock: Record<string, Record<string, number>> = {} // gkey -> studentId -> baho

/** Fan+chorak bo'yicha o'quvchilarning chorak bahosi (explicit) va tavsiyasi (kunlik o'rtacha) */
export async function getQuarterGrades(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<QuarterGradeRow[]> {
  if (USE_MOCK) {
    await delay()
    const k = gkey(classId, subjectId, quarter)
    const explicit = quarterGradesMock[k] ?? {}
    const byStudent = new Map<string, number[]>()
    ;(journalMock[k] ?? []).forEach((e) => {
      if (e.grade == null) return
      const arr = byStudent.get(e.studentId) ?? []
      arr.push(e.grade)
      byStudent.set(e.studentId, arr)
    })
    const ids = new Set<string>([...Object.keys(explicit), ...byStudent.keys()])
    return [...ids].map((studentId) => {
      const ds = byStudent.get(studentId)
      const recommended =
        ds && ds.length ? Math.round((ds.reduce((a, b) => a + b, 0) / ds.length) * 100) / 100 : undefined
      return { studentId, grade: explicit[studentId], recommended }
    })
  }
  const { data } = await api.get<QuarterGradeRow[]>('/admin/journal/quarter-grades', {
    params: { classId, subjectId, quarter },
  })
  return data
}

/** Chorak bahosini belgilash; grade=null bo'lsa — mavjud baho o'chiriladi */
export async function setQuarterGrade(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  grade: number | null,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const store = (quarterGradesMock[k] ??= {})
    if (grade == null) delete store[studentId]
    else store[studentId] = grade
    return
  }
  await api.put('/admin/journal/quarter-grades', { classId, subjectId, quarter, studentId, grade })
}

/* ---------- Mavzularni Excel'dan ommaviy yuklash ---------- */

export interface TopicImportRowError {
  row: number
  reason: string
}
export interface TopicImportResult {
  imported: number
  skipped: number
  errors: number
  rowErrors: TopicImportRowError[]
}

/** Tanlangan sinf+fan+chorak uchun mavzular shabloni (.xlsx) — jadval kunlari oldindan to'ldirilgan. */
export async function downloadTopicsTemplate(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<void> {
  if (USE_MOCK) {
    alert('Shablon faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/journal/topics-template', {
    params: { classId, subjectId, quarter },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'mavzular_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** To'ldirilgan Excel'dan mavzu+uy vazifani import qiladi (darsni "o'tilgan" qilmaydi). */
export async function importTopics(
  file: File,
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<TopicImportResult> {
  const fd = new FormData()
  fd.append('file', file)
  fd.append('classId', classId)
  fd.append('subjectId', subjectId)
  fd.append('quarter', String(quarter))
  const { data } = await api.post<TopicImportResult>('/admin/journal/topics-import', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}
