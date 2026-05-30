import type { Credentials, MonthStatus, Student, StudentLedger } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { studentsMock } from '../mock/students'
import { classesMock } from '../mock/classes'
import { financeMock } from '../mock/finance'

/** Serverga yuklangan fayl haqida (admin uploads javobi). */
export interface UploadedFile {
  name: string
  url: string
  size: number
  contentType: string
}

/** Faylni serverga yuklash (rasm/PDF, ~20 MB). URL qaytaradi — uni keyin entity'da saqlash mumkin. */
export async function uploadAdminFile(file: File): Promise<UploadedFile> {
  if (USE_MOCK) {
    await delay(200)
    return { name: file.name, url: `/uploads/mock-${Date.now()}-${file.name}`, size: file.size, contentType: file.type }
  }
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<UploadedFile>('/admin/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/** Forma maydonlari (balans bu yerda emas — u to'lov orqali o'zgaradi).
 *  newPassword — ixtiyoriy: tahrirda kiritilsa o'quvchi akkaunti paroli almashtiriladi. */
export type StudentPayload = Omit<Student, 'id' | 'balance'> & { newPassword?: string }

export async function getStudents(): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return studentsMock
  }
  const { data } = await api.get<Student[]>('/admin/students')
  return data
}

/** Faqat arxivlangan o'quvchilar ro'yxati (alohida ko'rish uchun). */
export async function getArchivedStudents(): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Student[]>('/admin/students/archived')
  return data
}

/** O'quvchini arxivga ko'chirish (sabab bilan). Login bloklanadi. */
export async function archiveStudent(id: string, reason: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/students/${id}/archive`, { reason })
}

/** Arxivdan qaytarish. Ixtiyoriy yangi parol bilan (parol bo'sh = login bloklangicha qoladi). */
export async function restoreStudent(id: string, newPassword?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/students/${id}/restore`, { newPassword: newPassword ?? null })
}

export async function createStudent(payload: StudentPayload): Promise<Student> {
  if (USE_MOCK) {
    await delay(300)
    // Kelgan oyidan joriy oygacha har oy uchun qarz: balans = -fee * oylar soni
    const fee = classesMock.find((c) => c.name === payload.className)?.monthlyFee ?? 0
    const cur = new Date().toISOString().slice(0, 7)
    const enr = (payload.enrollmentDate || cur).slice(0, 7)
    let months = 0
    if (enr <= cur) {
      const [ey, em] = enr.split('-').map(Number)
      const [cy, cm] = cur.split('-').map(Number)
      months = (cy - ey) * 12 + (cm - em) + 1
    }
    return { ...payload, id: uid(), balance: -fee * months }
  }
  const { data } = await api.post<Student>('/admin/students', payload)
  return data
}

/** Update o'quvchini tahrirlash.
 *  `applyDiscount=true` — chegirma o'zgargan bo'lsa, joriy oy hisobi yangi summaga
 *  to'g'rilanadi (balans deltaga moslab tuziladi). false (default) — joriy oy eski summada
 *  qoladi, yangi chegirma keyingi accrual'dan amal qiladi. */
export async function updateStudent(
  id: string,
  payload: StudentPayload,
  applyDiscount?: boolean,
): Promise<void> {
  if (USE_MOCK) {
    await delay(300)
    return
  }
  await api.put(`/admin/students/${id}`, payload, {
    params: applyDiscount ? { applyDiscount: true } : undefined,
  })
}

export async function deleteStudent(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/students/${id}`)
}

/** O'quvchining tizim akkaunti (login/parol) */
export async function getStudentCredentials(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'aliyevvali', password: 'demo23', role: 'student' }
  }
  const { data } = await api.get<Credentials>(`/admin/students/${id}/credentials`)
  return data
}

/** O'quvchiga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetStudentPassword(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'aliyevvali', password: 'yangi' + Math.random().toString(36).slice(2, 8), role: 'student' }
  }
  const { data } = await api.post<Credentials>(`/admin/students/${id}/reset-password`)
  return data
}

/** O'quvchiga to'lov kiritish — balansga qo'shiladi.
 *  `month` ("YYYY-MM") berilsa, to'lov shu oy uchun hisoblanadi. */
export async function addPayment(id: string, amount: number, month?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(250)
    return
  }
  await api.post(`/admin/students/${id}/payments`, { amount, month })
}

const LEDGER_MONTHS = ['2026-01', '2026-02', '2026-03', '2026-04', '2026-05']

/** O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat */
export async function getStudentLedger(id: string): Promise<StudentLedger> {
  if (USE_MOCK) {
    await delay()
    const student = studentsMock.find((s) => s.id === id)
    if (!student) throw new Error('O\'quvchi topilmadi')
    const rawFee = classesMock.find((c) => c.name === student.className)?.monthlyFee ?? 0
    const monthDiscount = Math.max(
      0,
      Math.min(rawFee, (rawFee * student.discountPct) / 100 + student.discountAmount),
    )
    const fee = Math.max(0, rawFee - monthDiscount)
    const totalCharged = rawFee * LEDGER_MONTHS.length
    const totalDiscount = monthDiscount * LEDGER_MONTHS.length
    let pool = Math.max(0, fee * LEDGER_MONTHS.length + student.balance)
    const totalPaid = pool
    const months = LEDGER_MONTHS.map((month) => {
      const paid = Math.min(pool, fee)
      pool -= paid
      const remaining = fee - paid
      const status: MonthStatus = remaining === 0 ? 'paid' : paid > 0 ? 'partial' : 'unpaid'
      return { month, charged: rawFee, discount: monthDiscount, paid, remaining, status }
    })
    const payments = financeMock
      .filter((t) => t.studentId === id && t.category === 'tuition')
      .map((t) => ({ date: t.date, amount: t.amount, note: t.note }))
      .sort((a, b) => (a.date < b.date ? 1 : -1))
    return {
      student,
      balance: student.balance,
      monthlyFee: fee,
      totalCharged,
      totalDiscount,
      totalPaid,
      months,
      payments,
    }
  }
  const { data } = await api.get<StudentLedger>(`/admin/students/${id}/ledger`)
  return data
}
