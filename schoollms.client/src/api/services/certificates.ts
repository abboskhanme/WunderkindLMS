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
}

export interface CertificateTypePayload {
  name: string
  isScored: boolean
  isActive: boolean
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
}

export interface CertificatePayload {
  studentId: string
  typeId: string
  subjectId: string | null
  teacherId: string | null
  number: string | null
  /** FAQAT `isScored` turda yuboriladi — aks holda server o'qiladigan xato qaytaradi. */
  score: number | null
  issuedOn: string
  expiresOn: string | null
  fileUrl: string | null
  comment: string | null
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
}): Promise<CertificateType[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<CertificateType[]>(
    `/admin/certificate-types${query({
      activeOnly: options?.activeOnly ? 'true' : undefined,
      scoredOnly: options?.scoredOnly ? 'true' : undefined,
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

/** Server qaytargan o'qiladigan xato matni (400 javobidagi `message`). */
export function certificateError(err: unknown): string {
  const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
  return message ?? 'Saqlashda xatolik yuz berdi'
}
