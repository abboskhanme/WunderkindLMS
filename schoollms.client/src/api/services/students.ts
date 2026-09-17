import type { Credentials, MonthStatus, Student, StudentLedger } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { studentsMock } from '../mock/students'
import { classesMock } from '../mock/classes'

/** Serverga yuklangan fayl haqida (admin uploads javobi). */
export interface UploadedFile {
  name: string
  url: string
  size: number
  contentType: string
}

/**
 * Barcha o'quvchilarni login/parol bilan Excel (.xlsx) ga yuklab oladi (faqat superadmin).
 * Parol faqat foydalanuvchi hali kirmagan bo'lsa to'ldiriladi (kirgach bo'sh).
 */
export async function downloadStudentCredentials(): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/students/export', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `oquvchilar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Excel'dan ommaviy import natijasi (bitta xato qator). */
export interface StudentImportRowError {
  row: number
  message: string
}

/** Excel'dan ommaviy import yakuniy hisoboti. */
export interface StudentImportResult {
  created: number
  failed: number
  skipped: number
  errors: StudentImportRowError[]
}

/** O'quvchilarni ommaviy kiritish uchun bo'sh Excel shablonini yuklab oladi (.xlsx). */
export async function downloadStudentImportTemplate(): Promise<void> {
  if (USE_MOCK) {
    alert('Shablon faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/students/import-template', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'oquvchilar_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** To'ldirilgan Excel (.xlsx) shablonini yuklab, o'quvchilarni ommaviy yaratadi. */
export async function importStudents(file: File): Promise<StudentImportResult> {
  if (USE_MOCK) {
    await delay(300)
    return { created: 0, failed: 0, skipped: 0, errors: [] }
  }
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<StudentImportResult>('/admin/students/import', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
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

/** Forma maydonlari. Balans bu yerda YO'Q — u hisoblanadi (P1-21), chegirma ham
 *  yo'q: u "Moliya → Chegirmalar" da, direktor tasdig'i bilan beriladi (SPEC §8.1 Q5).
 *  newPassword — ixtiyoriy: tahrirda kiritilsa o'quvchi akkaunti paroli almashtiriladi. */
export type StudentPayload = Omit<Student, 'id' | 'balance'> & { newPassword?: string }

/**
 * §2.3 — o'quvchilar ro'yxatining sertifikat filtrlari. QO'SHIMCHA: berilmasa
 * so'rov bugungiday, hech qanday parametrsiz ketadi.
 */
export interface StudentCertificateFilters {
  /** Sertifikat turlari (ko'p tanlov). Orasidagi bog'lovchi — YOKI. */
  certificateTypeIds?: string[]
  /** Sertifikatni bergan o'qituvchi. */
  certificateTeacherId?: string
}

/** Filtrlarni so'rov satriga aylantiradi. Bo'sh bo'lsa — bo'sh satr. */
function certificateQuery(filters?: StudentCertificateFilters): string {
  const params = new URLSearchParams()
  // Vergul bilan — server ham aynan shunday o'qiydi (CertificateService.ParseIds).
  if (filters?.certificateTypeIds?.length)
    params.set('certificateTypeIds', filters.certificateTypeIds.join(','))
  if (filters?.certificateTeacherId)
    params.set('certificateTeacherId', filters.certificateTeacherId)
  const text = params.toString()
  return text ? `?${text}` : ''
}

export async function getStudents(filters?: StudentCertificateFilters): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return studentsMock
  }
  const { data } = await api.get<Student[]>(`/admin/students${certificateQuery(filters)}`)
  return data
}

/** Faqat arxivlangan o'quvchilar ro'yxati (alohida ko'rish uchun). */
export async function getArchivedStudents(
  filters?: StudentCertificateFilters,
): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Student[]>(
    `/admin/students/archived${certificateQuery(filters)}`,
  )
  return data
}

/**
 * O'quvchini arxivga ko'chirish. Login bloklanadi.
 *
 * `reason` (erkin matn) MAJBURIY, `archiveReasonId` (katalog qatori, §2.2) ixtiyoriy.
 * Qarzi bor o'quvchi rad etiladi — `force` faqat superadmin uchun ishlaydi.
 *
 * Ekranlar `archiveManyStudents` dan foydalanadi (bitta o'quvchi ham shu yerdan ketadi):
 * xato javobi bitta shaklda bo'lishi uchun. Bu funksiya API to'liqligi uchun qoladi.
 */
export async function archiveStudent(
  id: string,
  input: { reason: string; archiveReasonId?: string | null; force?: boolean },
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/students/${id}/archive`, {
    reason: input.reason,
    archiveReasonId: input.archiveReasonId ?? null,
    force: input.force ?? false,
  })
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
    // Yangi o'quvchining qarzi 0: hisob obuna ochilgandan keyin yoziladi (P1-21).
    return { ...payload, id: uid(), balance: 0 }
  }
  const { data } = await api.post<Student>('/admin/students', payload)
  return data
}

/** O'quvchini tahrirlash. Pulga TEGMAYDI (P1-21): sinf o'zgarsa ham oylik
 *  summa obunada qoladi, chegirma esa "Moliya → Chegirmalar" da beriladi. */
export async function updateStudent(id: string, payload: StudentPayload): Promise<void> {
  if (USE_MOCK) {
    await delay(300)
    return
  }
  await api.put(`/admin/students/${id}`, payload)
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

// P1-21: `addPayment` olib tashlandi. To'lov faqat kassa orqali qabul qilinadi
// (`/api/cash/payments`, ochiq smena + kassir + chek raqami bilan); eski
// endpoint 410 Gone qaytaradi. Kassir ish o'rni: `src/pages/cashier`.

const LEDGER_MONTHS = ['2026-01', '2026-02', '2026-03', '2026-04', '2026-05']

/** O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat */
export async function getStudentLedger(id: string): Promise<StudentLedger> {
  if (USE_MOCK) {
    await delay()
    const student = studentsMock.find((s) => s.id === id)
    if (!student) throw new Error('O\'quvchi topilmadi')
    const rawFee = classesMock.find((c) => c.name === student.className)?.monthlyFee ?? 0
    const monthDiscount = 0
    const fee = rawFee
    const totalCharged = rawFee * LEDGER_MONTHS.length
    const totalDiscount = 0
    let pool = Math.max(0, fee * LEDGER_MONTHS.length + (student.balance ?? 0))
    const totalPaid = pool
    const months = LEDGER_MONTHS.map((month) => {
      const paid = Math.min(pool, fee)
      pool -= paid
      const remaining = fee - paid
      const status: MonthStatus = remaining === 0 ? 'paid' : paid > 0 ? 'partial' : 'unpaid'
      return { month, charged: rawFee, discount: monthDiscount, paid, remaining, status }
    })
    const payments: StudentLedger['payments'] = []
    return {
      student,
      balance: student.balance ?? 0,
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
