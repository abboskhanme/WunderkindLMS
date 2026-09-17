import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  Ikki bosqichli import — docs/modules/students-parity.md §2.3 (S-3).
 *
 *  1) `validateStudentImport` — butun fayl tekshiriladi, bazaga HECH NARSA
 *     yozilmaydi; har xato qator raqami va ustun nomi bilan qaytadi;
 *  2) `commitStudentImport` — yozadi. Bitta ham xato bo'lsa hech narsa
 *     yozilmaydi (server faylni qayta tekshiradi).
 *
 *  Fayl ikkala bosqichda ham yuboriladi: serverda "yarim yuklangan" fayl
 *  turmaydi.
 * ========================================================================= */

/** Bitta xato: Excel qatori, ustun nomi va sababi. */
export interface ImportCellError {
  row: number
  column: string
  message: string
}

/** Ko'rib chiqish jadvalining bitta qatori. */
export interface StudentImportPreviewRow {
  row: number
  fullName: string
  className: string
  /** `create` — yangi o'quvchi; `update` — mavjudining ustiga yoziladi. */
  action: 'create' | 'update'
}

/** Tekshiruv natijasi. `ok = false` bo'lsa tasdiqlash hech narsa yozmaydi. */
export interface StudentImportPreview {
  ok: boolean
  total: number
  created: number
  updated: number
  skipped: number
  errors: ImportCellError[]
  preview: StudentImportPreviewRow[]
  /** Faylni umuman o'qib bo'lmaganda (buzuq .xlsx, noto'g'ri sarlavha). */
  message?: string | null
}

export interface StudentImportCommit {
  created: number
  updated: number
  skipped: number
}

const EMPTY_PREVIEW: StudentImportPreview = {
  ok: false,
  total: 0,
  created: 0,
  updated: 0,
  skipped: 0,
  errors: [],
  preview: [],
  message: 'Import faqat real serverda ishlaydi (VITE_USE_MOCK=false).',
}

/** Yangi (kengaytirilgan) shablonni yuklab oladi. */
export async function downloadStudentImportTemplateV2(): Promise<void> {
  if (USE_MOCK) {
    alert('Shablon faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/students/import/shablon', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'oquvchilar_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

function form(file: File): FormData {
  const fd = new FormData()
  fd.append('file', file)
  return fd
}

export async function validateStudentImport(file: File): Promise<StudentImportPreview> {
  if (USE_MOCK) return EMPTY_PREVIEW
  const { data } = await api.post<StudentImportPreview>(
    '/admin/students/import/tekshirish',
    form(file),
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}

export async function commitStudentImport(file: File): Promise<StudentImportCommit> {
  const { data } = await api.post<StudentImportCommit>(
    '/admin/students/import/tasdiqlash',
    form(file),
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}
