import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * Sertifikatlar registri (docs/modules/existing-module-gaps.md §2.3).
 *
 * Tiplar `SchoolLms.Application/Dtos/CertificateDtos.cs` dan AYNAN ko'chirilgan
 * (C# PascalCase -> JSON camelCase). Sanalar — matn ("yyyy-MM-dd"), chunki
 * <input type="date"> ham shu formatda ishlaydi.
 */

/** Sertifikat turi. `isScored` = standart test, ya'ni ball bo'ladi (IELTS/SAT). */
export interface CertificateType {
  id: string
  name: string
  isScored: boolean
  isActive: boolean
  /** Shu turda nechta hujjat bor — serverda sanaladi. 0 dan katta bo'lsa tur o'chirilmaydi. */
  certificateCount: number
  /** Natija daraja bilan ham beriladi ("B2", "A*") — mijoz, 2026-10-07. */
  usesLevel: boolean
  /** Ruxsat etilgan darajalar, pastdan yuqoriga. Bo'sh — erkin matn. */
  levelOptions: string[]
  /** Blok test krediti: "A+=100; A=93.3" (daraja yoki eng kam ball → blok foizi). null — yo'q. */
  examCredits?: string | null
}

export interface CertificateTypePayload {
  name: string
  isScored: boolean
  isActive: boolean
  usesLevel: boolean
  /** Vergul bilan, pastdan yuqoriga: "A1, A2, B1, B2, C1, C2". Bo'sh — erkin matn. */
  levelOptions: string | null
  /** Blok test krediti — "daraja=foiz" juftlari, nuqta-vergul bilan. Bo'sh — kredit yo'q. */
  examCredits: string | null
}

/** Bitta sertifikat — ro'yxat qatori (nomlari bilan). */
export interface Certificate {
  id: string
  studentId: string
  studentName: string
  className: string
  typeId: string
  typeName: string
  typeIsScored: boolean
  /** ASOSIY (birinchi tanlangan) fan — eski, bitta-fanli ustun/eksport shu maydonni o'qiydi. */
  subjectId: string | null
  subjectName: string | null
  teacherId: string | null
  teacherName: string | null
  number: string | null
  score: number | null
  issuedOn: string
  expiresOn: string | null
  /** Muddati o'tganmi — SERVERDA, maktab vaqti bilan hisoblanadi. */
  isExpired: boolean
  fileUrl: string | null
  comment: string | null
  /** Z-3 — hujjatning HAMMA fani. `subjectId` shu ro'yxatning birinchi elementi. */
  subjectIds: string[]
  subjectNames: string[]
  /** Daraja ("B2") — faqat `typeUsesLevel` turda. */
  level: string | null
  typeUsesLevel: boolean
}

export interface CertificatePayload {
  studentId: string
  typeId: string
  teacherId: string | null
  number: string | null
  /** FAQAT `isScored` turda yuboriladi — aks holda server o'qiladigan xato qaytaradi. */
  score: number | null
  issuedOn: string
  expiresOn: string | null
  fileUrl: string | null
  comment: string | null
  /** Z-3 — bir nechta fan; bo'sh massiv = fansiz. */
  subjectIds: string[]
  /** FAQAT `usesLevel` turda; ro'yxat bo'lsa — ro'yxatdagi qiymat. */
  level: string | null
}

/** "Natijalar" tab'ining bitta qatori. */
export interface CertificateResultRow {
  studentId: string
  studentName: string
  className: string
  bestScore: number | null
  latestScore: number | null
  latestIssuedOn: string
  count: number
  bestLevel: string | null
  latestLevel: string | null
}

/** Darajali tur: har darajada nechta o'quvchi (eng yaxshi natijasi bo'yicha). */
export interface CertificateLevelCount {
  level: string
  students: number
}

/** "Natijalar" tab'i — yig'ma raqamlar ham serverdan keladi. */
export interface CertificateResults {
  typeId: string
  typeName: string
  studentCount: number
  certificateCount: number
  averageScore: number | null
  maxScore: number | null
  minScore: number | null
  rows: CertificateResultRow[]
  isScored: boolean
  usesLevel: boolean
  /** Darajali turda — o'rtacha o'rniga taqsimot. */
  levels: CertificateLevelCount[] | null
}

/** Sertifikat bergan o'qituvchi — filtr tanlovi uchun. */
export interface IssuingTeacher {
  id: string
  fullName: string
  startDate: string
}

/** Registr ro'yxatining filtrlari. Hammasi ixtiyoriy. */
export interface CertificateFilters {
  studentId?: string
  typeId?: string
  teacherId?: string
  subjectId?: string
  className?: string
  from?: string
  to?: string
  /** "Muddati tugayapti": shu necha kun ichida tugaydiganlar (o'tganlari ham). */
  expiringInDays?: number
  search?: string
}

/** Bo'sh qiymatlarni tashlab, so'rov satrini yig'adi. */
function query(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === '') continue
    search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

// ---------------------------------------------------------------------------
//  Turlar
// ---------------------------------------------------------------------------

/**
 * Turlar ro'yxati. Jadval BO'SH holda yetkaziladi — birinchi ekran "hali yo'q"
 * emas, "birinchi turni qo'shing" degan chaqiriq bo'lishi kerak.
 */
export async function getCertificateTypes(options?: {
  activeOnly?: boolean
  scoredOnly?: boolean
  /** "Natijalar": ball YOKI daraja qo'yiladigan turlar. */
  resultsOnly?: boolean
}): Promise<CertificateType[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<CertificateType[]>(
    `/admin/certificate-types${query({
      activeOnly: options?.activeOnly ? 'true' : undefined,
      scoredOnly: options?.scoredOnly ? 'true' : undefined,
      resultsOnly: options?.resultsOnly ? 'true' : undefined,
    })}`,
  )
  return data
}

export async function createCertificateType(payload: CertificateTypePayload): Promise<CertificateType> {
  const { data } = await api.post<CertificateType>('/admin/certificate-types', payload)
  return data
}

export async function updateCertificateType(
  id: string,
  payload: CertificateTypePayload,
): Promise<CertificateType> {
  const { data } = await api.put<CertificateType>(`/admin/certificate-types/${id}`, payload)
  return data
}

/** Ishlatilgan turni o'chirib bo'lmaydi — server 400 va sababni qaytaradi. */
export async function deleteCertificateType(id: string): Promise<void> {
  await api.delete(`/admin/certificate-types/${id}`)
}

// ---------------------------------------------------------------------------
//  Sertifikatlar
// ---------------------------------------------------------------------------

export async function getCertificates(filters: CertificateFilters = {}): Promise<Certificate[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Certificate[]>(`/admin/certificates${query({ ...filters })}`)
  return data
}

export async function createCertificate(payload: CertificatePayload): Promise<Certificate> {
  const { data } = await api.post<Certificate>('/admin/certificates', payload)
  return data
}

export async function updateCertificate(id: string, payload: CertificatePayload): Promise<Certificate> {
  const { data } = await api.put<Certificate>(`/admin/certificates/${id}`, payload)
  return data
}

export async function deleteCertificate(id: string): Promise<void> {
  await api.delete(`/admin/certificates/${id}`)
}

/** "Natijalar" tab'i — bitta tur bo'yicha o'quvchi × ball jadvali. */
export async function getCertificateResults(
  typeId: string,
  className?: string,
): Promise<CertificateResults> {
  const { data } = await api.get<CertificateResults>(
    `/admin/certificates/results${query({ typeId, className })}`,
  )
  return data
}

/** Sertifikat BERGAN o'qituvchilar — filtr tanlovi (hammasi emas). */
export async function getIssuingTeachers(): Promise<IssuingTeacher[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<IssuingTeacher[]>('/admin/certificates/teachers')
  return data
}

/**
 * Z-2 — joriy filtrlar bilan RO'YXATNING .xlsx eksporti (`studentSearch.ts`
 * dagi `exportStudents` bilan bir xil naqsh: blob, fayl nomi
 * `Content-Disposition`dan, brauzerda "yuklab olish" hodisasi qo'lda).
 */
export async function exportCertificates(filters: CertificateFilters = {}): Promise<void> {
  if (USE_MOCK) {
    await delay()
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get(`/admin/certificates/export${query({ ...filters })}`, {
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `sertifikatlar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Server qaytargan o'qiladigan xato matni (400 javobidagi `message`). */
export function certificateError(err: unknown): string {
  const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
  return message ?? 'Saqlashda xatolik yuz berdi'
}
