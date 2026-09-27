/**
 * HR TOPSHIRIQLARI — xodimning YAGONA API qatlami (hr-tasks.md §6 "Employee").
 *
 * Eski WunderkindHR xodim ilovasidagi `hrapp/lib/api.ts` ning har bir chaqiruvi shu
 * yerda, xuddi o'sha yo'llar bilan, faqat `/api/hr/me/...` ostida va camelCase
 * JSON bilan. Imzo — bizning oddiy Bearer token (`lib/api.js`), Telegram
 * `initData` sarlavhasi EMAS: sessiya allaqachon Telegram imzosidan olingan.
 *
 * Foydalanuvchining `hr_employees` qatori bo'lmasa server 403
 * `{ code: "hr_not_employee" }` qaytaradi — `probe()` buni "xodim emas" deb o'qiydi.
 *
 * EKRANLAR `api` NI TO'G'RIDAN-TO'G'RI CHAQIRMAYDI — faqat shu fayl orqali.
 */
import { api, ApiError, tokenStore } from './api'

const BASE = '/hr/me'

/** Fayl yuklash uzoqroq davom etadi — 10 MB sekin mobil tarmoqda. */
const UPLOAD_TIMEOUT_MS = 120_000

/* ------------------------------------------------ GET kesh va birlashtirish */

/**
 * Eski ilovadagi yengil kesh (u yerda prod loglarida bir soniyada to'rtta bir xil
 * `GET /tasks` ko'rilgan): AYNI paytda ketayotgan bir xil GET — bitta tarmoq
 * so'rovi, muvaffaqiyatli javob 30 soniya keshda. Har muvaffaqiyatli POST keshni
 * butunlay tozalaydi — belgilangan topshiriq keyingi ekranda yana "ochiq" bo'lib
 * chiqmasin. Xatolar keshlanmaydi: "Qayta urinish" har doim haqiqiy so'rov.
 */
const GET_TTL_MS = 30_000
const getCache = new Map()
const getInflight = new Map()

function cachedGet(path) {
  const fresh = getCache.get(path)
  if (fresh && Date.now() - fresh.at < GET_TTL_MS) return Promise.resolve(fresh.data)

  let pending = getInflight.get(path)
  if (!pending) {
    pending = api
      .get(path)
      .then((data) => {
        getCache.set(path, { at: Date.now(), data })
        return data
      })
      .finally(() => getInflight.delete(path))
    getInflight.set(path, pending)
  }
  return pending
}

async function post(path, body) {
  const data = await api.post(path, body)
  getCache.clear()
  return data
}

const taskPath = (id, action) => `${BASE}/tasks/${encodeURIComponent(id)}/${action}`

/* -------------------------------------------------------------- endpointlar */

export const hrApi = {
  /** `GET /hr/me` — xodim profili: `{ employeeId, fullName, positionName, timezone, today }`. */
  me: () => cachedGet(BASE),

  /**
   * Shu foydalanuvchi HR xodimimi? Ha — profil, yo'q — `null`.
   *
   * "Yo'q" deb o'qiladi: 403 (`hr_not_employee` yoki tanasiz — rol yetmasa ham
   * xodim emas degani), 404/405 (endpoint hali joylanmagan). Boshqa xato
   * (tarmoq, 5xx) — otiladi: xodimni jimgina "xodim emas" deb bo'lmaydi.
   */
  probe: async () => {
    try {
      return await cachedGet(BASE)
    } catch (e) {
      if (e instanceof ApiError && (e.code === 'hr_not_employee' || [403, 404, 405].includes(e.status))) {
        return null
      }
      throw e
    }
  },

  /** `GET /hr/me/tasks?date=YYYY-MM-DD` — o'sha kundagi topshiriqlar `{ date, items }`. */
  tasks: (date) => cachedGet(`${BASE}/tasks?date=${encodeURIComponent(date)}`),

  /** `POST /hr/me/tasks` — o'ziga topshiriq (`source=self`): `{ title, description, dueAt }`. */
  createTask: (input) => post(`${BASE}/tasks`, input),

  /** `POST /hr/me/tasks/{id}/complete` — "Bajardim": `{ comment, proofIds }`. */
  complete: (id, input) => post(taskPath(id, 'complete'), input),

  /** `POST /hr/me/tasks/{id}/accept` — "ko'rdim, tushundim" (holat o'zgarmaydi). */
  accept: (id) => post(taskPath(id, 'accept')),

  /** `POST /hr/me/tasks/{id}/reopen` — xato belgilanganini qaytarib olish (o'sha kuni). */
  reopen: (id) => post(taskPath(id, 'reopen')),

  /** `POST /hr/me/tasks/{id}/progress` — KPI qadami: `{ amount: "12.50", note }`. */
  progress: (id, input) => post(taskPath(id, 'progress'), input),

  /** `GET /hr/me/summary` — joriy oy bo'yicha o'z natijasi. */
  summary: () => cachedGet(`${BASE}/summary`),

  /**
   * `POST /hr/me/proofs` — `multipart/form-data` (`file` + topshiriq id'si),
   * javob `{ id, url }`. Ovozli izoh ham shu yo'l bilan ketadi (u ham isbot fayli).
   *
   * `XMLHttpRequest` — `fetch` da yuklash progressini kuzatib bo'lmaydi, sekin
   * internetda esa "nima bo'layapti?" degan savol qolmasligi kerak.
   * `onProgress` — 0..100.
   */
  uploadProof: (file, taskId, onProgress) => uploadProof(file, taskId, onProgress),
}

/* ----------------------------------------------------------- fayl yuklash */

function parseBody(text) {
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    return { message: text }
  }
}

function uploadProof(file, taskId, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api${BASE}/proofs`, true)
    xhr.timeout = UPLOAD_TIMEOUT_MS
    // `Content-Type` QO'LDA qo'yilmaydi: `FormData` chegara (`boundary`) bilan o'zi qo'yadi.
    const token = tokenStore.get()
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.setRequestHeader('Accept', 'application/json')

    xhr.upload.onprogress = (event) => {
      if (!event.lengthComputable || !onProgress) return
      onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)))
    }

    xhr.onload = () => {
      const body = parseBody(xhr.responseText)
      if (xhr.status < 200 || xhr.status >= 300) {
        reject(new ApiError(xhr.status, body))
        return
      }
      if (!body || typeof body.id !== 'string' || body.id.length === 0) {
        reject(new ApiError(500, { message: UPLOAD_FAILED }))
        return
      }
      getCache.clear()
      onProgress?.(100)
      resolve(body)
    }

    const fail = (message) => () => reject(new ApiError(0, { message }))
    xhr.onerror = fail("Internet aloqasi yo'q. Tarmoqni tekshirib, qayta urinib ko'ring.")
    xhr.ontimeout = fail("Server javob bermadi. Internetni tekshirib, qayta urinib ko'ring.")
    xhr.onabort = fail(UPLOAD_FAILED)

    const form = new FormData()
    form.append('file', file, file.name)
    // Ikkala nom ham: backend `taskId` (camelCase shartnoma) yoki eski `task_id`
    // ni bog'lashi mumkin — ortiqcha maydon zarar qilmaydi (docs/ASSUMPTIONS.md).
    form.append('taskId', taskId)
    form.append('task_id', taskId)
    xhr.send(form)
  })
}

const UPLOAD_FAILED = 'Fayl yuklanmadi. Qayta urinib ko‘ring.'

/* ------------------------------------------------------------ xato matnlari */

/**
 * Server matn bermagan holat uchun zaxira matnlar — eski `lib/errors.ts` va
 * `texts.ts` dan aynan. Server o'zbekcha `message` bergan bo'lsa HAR DOIM o'sha
 * ko'rsatiladi (u aniqroq); bu jadval faqat tanasiz javob uchun.
 */
const TEXT_BY_CODE = {
  employee_not_linked: 'Telegram hisobingiz hech bir xodimga bog‘lanmagan. Botga qaytib, telefon raqamingizni yuboring; raqam topilmasa direktorga murojaat qiling.',
  not_employee: 'Siz HR tizimida xodim sifatida ro‘yxatda yo‘qsiz. Direktorga murojaat qiling.',
  employee_inactive:
    'Xodim sifatida faolligingiz to‘xtatilgan — shuning uchun topshiriqlar ko‘rinmaydi. Xato bo‘lsa direktorga murojaat qiling.',
  task_not_found: 'Bu topshiriq endi mavjud emas — ro‘yxatni yangilang.',
  task_not_owned: 'Bu topshiriq endi mavjud emas — ro‘yxatni yangilang.',
  task_not_editable: 'Topshiriq holati o‘zgargan. Ro‘yxatni yangilab, qayta urinib ko‘ring.',
  invalid_transition: 'Topshiriq holati o‘zgargan. Ro‘yxatni yangilab, qayta urinib ko‘ring.',
  proof_required: 'Isbot kerak: fayl biriktiring yoki nima qilganingizni izohda yozing.',
  proof_limit_exceeded:
    'Bu topshiriqqa ruxsat etilgan isbotlar soni to‘lgan. Ortiqchasini o‘chirib, qayta urinib ko‘ring.',
  unsupported_proof_type:
    'Bu turdagi faylni isbot qilib bo‘lmaydi (HTML, SVG yoki skript). Boshqa formatda yuboring — rasm, hujjat, jadval, arxiv, ovoz yoki video.',
  proof_too_large:
    'Fayl hajmi ruxsat etilgan chegaradan katta. Uni siqib yoki kichikroq qilib qayta yuklang.',
  rate_limited: 'Biroz kuting va qayta urinib ko‘ring.',
}

const TEXT_BY_STATUS = {
  404: 'Bu topshiriq endi mavjud emas — ro‘yxatni yangilang.',
  409: 'Topshiriq holati o‘zgargan. Ro‘yxatni yangilab, qayta urinib ko‘ring.',
  413: UPLOAD_FAILED,
  429: 'Biroz kuting va qayta urinib ko‘ring.',
}

/** Xatolikning bitta satrlik o'zbekcha matni (banner va oynalar uchun). */
export function hrErrorMessage(error) {
  if (!(error instanceof ApiError)) return error?.message || 'Kutilmagan xatolik yuz berdi. Qayta urinib ko‘ring.'
  const body = error.body || {}
  // ASP.NET validatsiya javobi: `{ title, errors: { field: ["..."] } }` — birinchi sabab.
  const firstFieldError = body.errors && typeof body.errors === 'object'
    ? Object.values(body.errors).flat().find((m) => typeof m === 'string' && m.length > 0)
    : null
  const serverText = body.message || body.detail || firstFieldError
  if (serverText) return serverText
  const code = typeof error.code === 'string' ? error.code.replace(/^hr_/, '') : ''
  return TEXT_BY_CODE[code] || TEXT_BY_STATUS[error.status] || error.message
}
