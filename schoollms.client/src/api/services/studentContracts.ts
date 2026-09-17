import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10
 *  (K-1, K-2, K-3).
 *
 *  ANDOZA (`contracts.ts`) BILAN CHALKASHTIRMANG. U yerda Word andozalari va
 *  Telegram orqali yuborish; bu yerda — o'quvchi bilan tuzilgan shartnomaning
 *  O'ZI: raqam, imzo sanasi, tugash sanasi, fayl.
 *
 *  PUL YO'Q: summa, to'lov turi va to'lov kuni bu yozuvda saqlanmaydi —
 *  ular obunada (Moliya).
 *
 *  DTO nomlari backend bilan AYNAN bir xil (camelCase).
 * ========================================================================= */

/** Qayerdan paydo bo'lgan: tizim hosil qilgan yoki qo'lda yuklangan. */
export type StudentContractSource = 'generated' | 'uploaded'

/**
 * HISOBLANADIGAN holat (bazada bunday ustun yo'q):
 * `draft` — raqamsiz, `expired` — muddati o'tgan, `active` — qolgani.
 */
export type StudentContractStatus = 'draft' | 'active' | 'expired'

export const contractSourceLabels: Record<StudentContractSource, string> = {
  generated: 'Tizim hosil qilgan',
  uploaded: 'Qo‘lda yuklangan',
}

export const contractStatusLabels: Record<StudentContractStatus, string> = {
  draft: 'Qoralama',
  active: 'Amalda',
  expired: 'Muddati tugagan',
}

export interface StudentContract {
  id: string
  studentId: string
  studentName: string
  className: string
  templateId: string | null
  templateName: string | null
  number: string | null
  /** ISO "yyyy-MM-dd" yoki null. */
  signedOn: string | null
  endsOn: string | null
  fileUrl: string | null
  source: StudentContractSource
  status: StudentContractStatus
  comment: string | null
  createdBy: string
  createdByName: string | null
  createdAt: string
}

export interface StudentContractPage {
  items: StudentContract[]
  total: number
  page: number
  pageSize: number
}

export interface StudentContractFilter {
  search?: string
  studentId?: string
  className?: string
  source?: StudentContractSource
  hasFile?: boolean
  status?: StudentContractStatus
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

/** Fayl AVVAL `uploadAdminFile` orqali yuklanadi; bu yerga faqat manzili keladi. */
export interface SaveStudentContractInput {
  studentId?: string
  templateId?: string | null
  number?: string | null
  signedOn?: string | null
  endsOn?: string | null
  fileUrl?: string | null
  source?: StudentContractSource
  comment?: string | null
}

export interface GenerateStudentContractInput {
  studentId: string
  templateId: string
  number?: string | null
  signedOn?: string | null
  endsOn?: string | null
  comment?: string | null
}

export interface ContractToken {
  token: string
  value: string
}

export interface StudentContractPreview {
  studentId: string
  studentName: string
  className: string
  /** Keyingi bo'sh raqam — forma shu bilan to'ldiriladi, o'zgartirsa bo'ladi. */
  nextNumber: string
  /** Bugungi sana, ISO. */
  today: string
  tokens: ContractToken[]
}

const emptyPage: StudentContractPage = { items: [], total: 0, page: 1, pageSize: 50 }

export async function searchStudentContracts(
  filter: StudentContractFilter = {},
): Promise<StudentContractPage> {
  if (USE_MOCK) return emptyPage
  const { data } = await api.get<StudentContractPage>('/admin/student-contracts', {
    params: filter,
  })
  return data
}

/** Bitta o'quvchining shartnoma tarixi (kartochkadagi tab). */
export async function getStudentContracts(studentId: string): Promise<StudentContract[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentContract[]>(`/admin/students/${studentId}/contracts`)
  return data
}

export async function createStudentContract(
  input: SaveStudentContractInput,
): Promise<StudentContract> {
  const { data } = await api.post<StudentContract>('/admin/student-contracts', input)
  return data
}

export async function updateStudentContract(
  id: string,
  input: SaveStudentContractInput,
): Promise<StudentContract> {
  const { data } = await api.put<StudentContract>(`/admin/student-contracts/${id}`, input)
  return data
}

export async function deleteStudentContract(id: string): Promise<void> {
  await api.delete(`/admin/student-contracts/${id}`)
}

/** Hosil qilishdan oldingi ko'rinish: keyingi raqam va andozaga tushadigan qiymatlar. */
export async function previewStudentContract(studentId: string): Promise<StudentContractPreview> {
  const { data } = await api.get<StudentContractPreview>('/admin/student-contracts/preview', {
    params: { studentId },
  })
  return data
}

/** Andozani to'ldirib .docx hosil qiladi va reyestrga yozuv qo'shadi. */
export async function generateStudentContract(
  input: GenerateStudentContractInput,
): Promise<StudentContract> {
  const { data } = await api.post<StudentContract>('/admin/student-contracts/generate', input)
  return data
}

/* =========================================================================
 *  K-5 — ommaviy biriktirish (bir xil raqam va sana, tanlangan o'quvchilarga)
 * ========================================================================= */

export interface BulkAttachContractInput {
  studentIds: string[]
  number: string
  signedOn?: string | null
  endsOn?: string | null
  comment?: string | null
}

/** Bitta o'quvchi uchun natija: muvaffaqiyatli bo'lsa yangi yozuv id'si, aks holda sabab. */
export interface BulkAttachContractRow {
  studentId: string
  studentName: string | null
  ok: boolean
  contractId: string | null
  reason: string | null
}

export interface BulkAttachContractResult {
  attached: number
  results: BulkAttachContractRow[]
}

/**
 * K-5 — bitta shartnoma raqami va sanasini bir nechta tanlangan o'quvchiga
 * birdaniga biriktiradi. `student_contracts.number` QISMAN UNIKAL bo'lgani
 * uchun (bitta raqam — bitta yozuv) faqat BITTASI muvaffaqiyatli bo'ladi;
 * qolganlari ANIQ sababi bilan qaytadi (allaqachon raqami bor / raqam band).
 */
export async function attachContractsMany(
  input: BulkAttachContractInput,
): Promise<BulkAttachContractResult> {
  const { data } = await api.post<BulkAttachContractResult>(
    '/admin/student-contracts/attach-many',
    input,
  )
  return data
}
