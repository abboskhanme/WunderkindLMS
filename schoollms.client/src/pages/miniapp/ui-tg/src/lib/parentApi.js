/**
 * OTA-ONA PANELINING BARCHA SO'ROVLARI — bitta fayl.
 *
 * NEGA SHUNDAY. Bugun ota-ona ma'lumotini o'quvchi portali beradi
 * (`/api/student/*`), ertaga esa Mini App uchun alohida `/api/tg/*` chiqadi.
 * Har ekran o'zi `api.get('/student/...')` yozsa, o'sha kun o'nlab faylni
 * qidirib chiqishga to'g'ri kelardi. Shuning uchun ekranlar FAQAT shu
 * fayldagi funksiyalarni chaqiradi — yo'lni almashtirish har endpoint uchun
 * bitta qator.
 *
 * HAR FUNKSIYA `childId` NI OSHKORA OLADI. Bugungi backend ota-onani telefon
 * raqami orqali BITTA farzandga bog'laydi (`students.parent_phone`) va
 * `?studentId=` ni ota-ona uchun ATAYLAB e'tiborga olmaydi — begona bolaning
 * id'sini yozib qo'yishning oldini oladi. Ya'ni bugun bu parametr javobga
 * ta'sir qilmaydi, lekin ko'p farzandli bog'lanish (`guardians` jadvali)
 * kelganda ekranlar o'zgarmaydi: ular allaqachon "qaysi farzand" degan
 * savolga javob berib turibdi.
 */
import { api, ApiError, tokenStore } from './api'

/** `?studentId=...` (+ qo'shimcha parametrlar). Bo'sh bo'lsa umuman qo'shilmaydi. */
function forChild(childId, extra = '') {
  const parts = []
  if (childId) parts.push('studentId=' + encodeURIComponent(childId))
  if (extra) parts.push(extra)
  return parts.length ? '?' + parts.join('&') : ''
}

/* ------------------------------------------------------------- farzandlar */

/**
 * Ota-onaga biriktirilgan farzandlar: `[{ id, fullName, className }]`.
 *
 * IKKI MANBALI. Avval `/api/tg/children` so'raladi — ko'p-ko'pga bog'lanish
 * (`guardians` + `student_guardians`) shu endpointda keladi. Backend uni hali
 * chiqarmagan bo'lsa (404/405) eski yo'lga tushamiz: `/api/student/me` bitta
 * farzand qaytaradi. Natija ikkalasida bir xil shaklda, ya'ni qobiq ham,
 * tablar ham farqni sezmaydi; ro'yxat bittaga qisqarsa almashtirgich o'zi
 * ko'rinmay qoladi.
 *
 * FAQAT "endpoint yo'q" xatosida orqaga qaytamiz: 401 yoki 500 da qaytish
 * haqiqiy nosozlikni yashirgan bo'lardi.
 */
export async function listChildren() {
  try {
    const body = await api.get('/tg/children')
    // Shartnoma hali e'lon qilinmagan: ro'yxat to'g'ridan-to'g'ri ham,
    // `{ children: [...] }` ichida ham kelishi mumkin. Ikkalasini ham
    // qabul qilamiz — mos kelmagan javob ekranni yiqitmasin.
    const rows = Array.isArray(body) ? body : (body?.children ?? [])
    return rows.map((c) => ({
      id: c.id ?? c.studentId,
      fullName: c.fullName,
      className: c.className ?? '',
    }))
  } catch (e) {
    const missing = e instanceof ApiError && (e.status === 404 || e.status === 405)
    if (!missing) throw e
  }

  const me = await api.get('/student/me')
  return me ? [{ id: me.id, fullName: me.fullName, className: me.className ?? '' }] : []
}

/* ------------------------------------------------------------------- Bosh */

/** Bosh sahifa: profil, meta, bugungi darslar, bugungi baholar, qoldiq. */
export const getDashboard = (childId) => api.get('/student/dashboard' + forChild(childId))

/** Sinf e'lonlari (sinf chatidagi xabarlar) — yangisidan eskisiga. */
export async function getAnnouncements(childId) {
  const rows = (await api.get('/student/chat' + forChild(childId))) ?? []
  return [...rows].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
}

/* -------------------------------------------------- Farzandni olib ketish */

/** Bugungi pickup so'rovi holati (yo'q bo'lsa — `null`). */
export const getPickup = (childId) => api.get('/student/pickup' + forChild(childId))

/** "Farzandimni olib ketaman" — sinf rahbariga so'rov yuboradi. */
export const requestPickup = (childId) => api.post('/student/pickup', { studentId: childId })

/* ---------------------------------------------------------------- Baholar */

/** Fan × chorak o'rtacha baholari + chorak bo'yicha davomat jamlamasi. */
export const getGrades = (childId) => api.get('/student/grades' + forChild(childId))

/** Davomat: chorak jamlamasi + har bir qoldirish/kechikish qatori (sababi bilan). */
export const getAttendance = (childId, quarter) =>
  api.get('/student/attendance' + forChild(childId, quarter ? `quarter=${quarter}` : ''))

/* ----------------------------------------------------------------- Jadval */

/**
 * Maktab konteksti: choraklar (sanalari bilan), dars vaqtlari, joriy
 * chorak/hafta. Farzandga bog'liq emas — butun maktab uchun bitta, shuning
 * uchun `childId` olmaydi.
 */
export const getMeta = () => api.get('/student/meta')

/**
 * Bitta haftaning kunlik jadvali: sana, dars vaqti, fan, o'qituvchi, mavzu,
 * uy vazifasi, shu o'quvchining bahosi va davomat sababi.
 *
 * `/student/schedule` dan farqi — bu yerda SANA bor, ya'ni "bugun" ni aniq
 * belgilash mumkin va o'tgan haftalarda baho ham ko'rinadi.
 */
export const getWeek = (childId, quarter, week) =>
  api.get('/student/journal' + forChild(childId, `quarter=${quarter}&week=${week}`))

/* ----------------------------------------------------------------- To'lov */

/** Hisob-fakturalar, qarz qatorlari va to'lovlar tarixi (chek raqami bilan). */
export const getBilling = (childId) => api.get('/student/billing' + forChild(childId))

/**
 * Chek PDF'ini qurilmaga yuklaydi.
 *
 * `api.js` faqat JSON bilan ishlaydi, chek esa binar — shuning uchun bu yerda
 * `fetch` to'g'ridan-to'g'ri chaqiriladi, lekin token o'sha yagona
 * `tokenStore` dan olinadi.
 *
 * Telegram ichida yangi oyna (`window.open`) ishonchsiz: mijoz uni bloklashi
 * mumkin. Shuning uchun blob'dan YUKLAB OLISH havolasi yasaladi — iOS va
 * Android mijozlari faylni o'z hujjat ko'ruvchisida ochadi.
 */
export async function downloadReceipt(childId, paymentId, receiptNo) {
  const token = tokenStore.get()
  const res = await fetch(`/api/student/receipts/${paymentId}.pdf` + forChild(childId), {
    headers: token ? { Authorization: 'Bearer ' + token } : {},
  })
  if (!res.ok) throw new ApiError(res.status, { message: "Chekni olib bo'lmadi" })

  const blob = await res.blob()
  // Xato javob ham blob bo'lib keladi — JSON kelgan bo'lsa bu chek emas.
  if (blob.type.includes('application/json')) throw new ApiError(500, { message: 'Chek topilmadi' })

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `chek-${receiptNo}.pdf`
  document.body.appendChild(a)
  a.click()
  a.remove()
  // Darrov bekor qilsak fayl yuklanmay qoladi — nusxa ko'chirilguncha turadi.
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

/* ------------------------------------------------------------------ Ovqat */

/**
 * Oshxona menyusi (start..end, ISO sanalar).
 *
 * Menyu bugun BUTUN MAKTAB uchun bitta, lekin `childId` baribir uzatiladi:
 * filial yoki sinf bo'yicha alohida menyu paydo bo'lganda chaqiruvchi kodni
 * qayta yozish kerak bo'lmaydi. Bugungi endpoint uni e'tiborsiz qoldiradi.
 */
export const getMenu = (childId, startISO, endISO) =>
  api.get(`/student/canteen?start=${startISO}&end=${endISO}`)
