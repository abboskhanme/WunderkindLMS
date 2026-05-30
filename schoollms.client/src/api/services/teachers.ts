import type {
  Credentials,
  MonthSalary,
  MonthStatus,
  SalaryLedger,
  Teacher,
} from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { teachersMock } from '../mock/teachers'
import { financeMock } from '../mock/finance'

/** newPassword — ixtiyoriy: tahrirda kiritilsa o'qituvchi akkaunti paroli almashtiriladi. */
export type TeacherPayload = Omit<Teacher, 'id'> & { newPassword?: string }

export async function getTeachers(): Promise<Teacher[]> {
  if (USE_MOCK) {
    await delay()
    return teachersMock
  }
  const { data } = await api.get<Teacher[]>('/admin/teachers')
  return data
}

export async function createTeacher(payload: TeacherPayload): Promise<Teacher> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id: uid() }
  }
  const { data } = await api.post<Teacher>('/admin/teachers', payload)
  return data
}

export async function updateTeacher(id: string, payload: TeacherPayload): Promise<Teacher> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id }
  }
  const { data } = await api.put<Teacher>(`/admin/teachers/${id}`, payload)
  return data
}

export async function deleteTeacher(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/teachers/${id}`)
}

/** Faqat arxivlangan o'qituvchilar */
export async function getArchivedTeachers(): Promise<Teacher[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Teacher[]>('/admin/teachers/archived')
  return data
}

/** O'qituvchini arxivga ko'chirish (login bloklanadi) */
export async function archiveTeacher(id: string, reason: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/teachers/${id}/archive`, { reason })
}

/** Arxivdan qaytarish — ixtiyoriy yangi parol bilan */
export async function restoreTeacher(id: string, newPassword?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/teachers/${id}/restore`, { newPassword })
}

/** O'qituvchining tizim akkaunti (login/parol) */
export async function getTeacherCredentials(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'umarovaziz', password: 'demo23', role: 'teacher' }
  }
  const { data } = await api.get<Credentials>(`/admin/teachers/${id}/credentials`)
  return data
}

/** O'qituvchiga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetTeacherPassword(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'umarovaziz', password: 'yangi' + Math.random().toString(36).slice(2, 8), role: 'teacher' }
  }
  const { data } = await api.post<Credentials>(`/admin/teachers/${id}/reset-password`)
  return data
}

/** O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): oma-oy taqsimot */
export async function getSalaryLedger(id: string, from?: string, to?: string): Promise<SalaryLedger> {
  if (USE_MOCK) {
    await delay()
    const teacher = teachersMock.find((t) => t.id === id)
    if (!teacher) throw new Error("O'qituvchi topilmadi")
    const periodFrom = (from ?? `${new Date().getFullYear()}-01-01`).slice(0, 7)
    const toM = (to ?? new Date().toISOString().slice(0, 10)).slice(0, 7)
    // Oylik o'qituvchi boshlagan oydan hisoblanadi — undan oldingi oylar uchun qarz yozilmaydi.
    const fromM =
      teacher.salaryStartMonth && teacher.salaryStartMonth > periodFrom
        ? teacher.salaryStartMonth
        : periodFrom
    const pays = financeMock.filter(
      (t) =>
        t.teacherId === id &&
        t.category === 'salary' &&
        t.date.slice(0, 7) >= fromM &&
        t.date.slice(0, 7) <= toM,
    )
    const months: MonthSalary[] = []
    let m = fromM
    while (m <= toM) {
      const paid = pays.filter((p) => p.date.slice(0, 7) === m).reduce((a, p) => a + p.amount, 0)
      const remaining = teacher.salary - paid
      const status: MonthStatus = remaining <= 0 ? 'paid' : paid > 0 ? 'partial' : 'unpaid'
      months.push({ month: m, expected: teacher.salary, paid, remaining, status })
      const [y, mm] = m.split('-').map(Number)
      m = mm === 12 ? `${y + 1}-01` : `${y}-${String(mm + 1).padStart(2, '0')}`
    }
    const totalExpected = teacher.salary * months.length
    const totalPaid = pays.reduce((a, p) => a + p.amount, 0)
    return {
      teacherId: teacher.id,
      fullName: teacher.fullName,
      salary: teacher.salary,
      totalExpected,
      totalPaid,
      remaining: totalExpected - totalPaid,
      months,
      payments: pays
        .map((t) => ({ date: t.date, amount: t.amount, note: t.note, month: t.month }))
        .sort((a, b) => (a.date < b.date ? 1 : -1)),
    }
  }
  const { data } = await api.get<SalaryLedger>(`/admin/teachers/${id}/salary-ledger`, {
    params: { from, to },
  })
  return data
}

/** Bitta oy uchun maosh holati (belgilangan/berilgan/qoldiq) — to'lov oynasi uchun */
export async function getSalaryMonth(id: string, month: string): Promise<MonthSalary | null> {
  const ledger = await getSalaryLedger(id, `${month}-01`, `${month}-31`)
  return ledger.months[0] ?? null
}
