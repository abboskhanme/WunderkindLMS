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
 * HAR FUNKSIYA `childId` NI OSHKORA OLADI. Backend ota-ona uchun `?studentId=`
 * ni vasiylik (`student_guardians`) bo'yicha tekshiradi: O'Z farzandi bo'lsa —
 * o'sha farzand, begona id bo'lsa — jimgina e'tiborsiz qoladi
 * (`GuardianAccess.ParentTargetAsync`). Ya'ni farzand almashtirgich har tabda
 * haqiqatan tanlangan farzandning ma'lumotini ko'rsatadi.
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
 * IKKI MANBALI. Avval `/api/tg/parent/children` so'raladi — ko'p-ko'pga bog'lanish
 * (`guardians` + `student_guardians`) shu endpointda keladi. Backend uni hali
 * chiqarmagan bo'lsa (404/405) eski yo'lga tushamiz: `/api/student/me` bitta
 * farzand qaytaradi. Natija ikkalasida bir xil shaklda, ya'ni qobiq ham,
 * tablar ham farqni sezmaydi; ro'yxat bittaga qisqarsa almashtirgich o'zi
 * ko'rinmay qoladi.
 *
 * ESKI YO'LGA UCH HOLATDA tushamiz: endpoint yo'q (404/405); hisob o'quvchiniki
 * (403 — `/tg/parent/*` faqat ota-onaga ochiq, bu panel esa o'quvchiga ham
 * xizmat qiladi va ko'p ota-onalar farzandining login'i bilan kiradi); ro'yxat
 * bo'sh (vasiylik yozilmagan, lekin telefon bo'yicha eski bog'lanish bor).
 * 401 yoki 500 da qaytish haqiqiy nosozlikni yashirgan bo'lardi.
 *
 * `role` berilsa va u o'quvchi bo'lsa, keraksiz 403 so'rovi umuman yuborilmaydi.
 */
export async function listChildren(role) {
  if (role !== 'student') {
    try {
      const body = await api.get('/tg/parent/children')
      // Shartnoma hali e'lon qilinmagan: ro'yxat to'g'ridan-to'g'ri ham,
      // `{ children: [...] }` ichida ham kelishi mumkin. Ikkalasini ham
      // qabul qilamiz — mos kelmagan javob ekranni yiqitmasin.
      const rows = Array.isArray(body) ? body : (body?.children ?? [])
      if (rows.length > 0) {
        return rows.map((c) => ({
          id: c.id ?? c.studentId,
          fullName: c.fullName,
          className: c.className ?? '',
        }))
      }
    } catch (e) {
      const fallback = e instanceof ApiError && [403, 404, 405].includes(e.status)
      if (!fallback) throw e
    }
  }

  // A pupil's login: the pupil and their brothers and sisters (2026-10-08 — the switcher at the top,
  // no second login). An older server without the endpoint → the pupil alone, as before.
  if (role === 'student') {
    try {
      const family = await api.get('/student/family')
      if (Array.isArray(family) && family.length > 0) {
        return family.map((c) => ({ id: c.id, fullName: c.fullName, className: c.className ?? '' }))
      }
    } catch (e) {
      if (!(e instanceof ApiError && [403, 404, 405].includes(e.status))) throw e
    }
  }

  try {
    const me = await api.get('/student/me')
    return me ? [{ id: me.id, fullName: me.fullName, className: me.className ?? '' }] : []
  } catch (e) {
    // Hech qaysi yo'l bilan bog'langan farzand yo'q — bu xato emas, bo'sh holat:
    // qobiq "Farzand biriktirilmagan" deb sababini tushuntiradi.
    if (e instanceof ApiError && e.status === 404) return []
    throw e
  }
}

/* -------------------------------------------------------------- ko'rinish */

/**
 * Maktab ota-onaga o'zlashtirishni (baholarni) ko'rsatadimi — §5.5
 * `show_learning_progress_in_parent_dashboard`, `/api/tg/me` dagi
 * `showLearningProgress`. O'quvchi hisobi uchun server har doim `true` beradi.
 *
 * QULF SERVERDA: bayroq o'chiq bo'lsa baholar endpointlari ota-onaga 403 yoki
 * bahosiz javob qaytaradi. Bu funksiya faqat ekranni tartibga soladi — bo'sh
 * "Baholar" tabi va "0 ta baho" ko'rinmasin. Shuning uchun so'rov yiqilsa
 * `true`: ekranni bekorga yashirmaymiz, ma'lumot baribir kelmaydi.
 */
export async function getShowLearningProgress() {
  try {
    const me = await api.get('/tg/me')
    return me?.showLearningProgress !== false
  } catch {
    return true
  }
}

/* ----------------------------------------------------------------- Profil */

/**
 * Profil ekrani: kim (ism, rol, maktab), qaysi Telegram ulangan va farzandlar
 * (sinf, qarindoshlik, asosiy vasiymi, qarz) — `/api/tg/me` dan; login esa
 * `/api/auth/me` dan (u yo'q bo'lsa profil baribir ochiladi, faqat login qatori
 * chiqmaydi).
 */
export async function getProfile() {
  const [me, account] = await Promise.all([
    api.get('/tg/me'),
    api.get('/auth/me').catch(() => null),
  ])
  return { ...me, login: account?.email ?? null }
}

/* ------------------------------------------------------------------- Bosh */

/** Bosh sahifa: profil, meta, bugungi darslar, bugungi baholar, qoldiq. */
export const getDashboard = (childId) => api.get('/student/dashboard' + forChild(childId))

/**
 * Maktab e'lonlari — farzandga (ota-onasiga) yuborilgan e'lonlar, yangisidan eskisiga.
 *
 * 2026-10-11: ilgari bu sinf GURUH CHATI xabarlarini o'qirdi; guruh chati ota-ona va
 * o'quvchidan butunlay olib tashlandi, karta endi haqiqiy e'lonlarni ko'rsatadi
 * (`/student/announcements` — ota-ona ham, farzand login'i ham, admin ham).
 */
export async function getAnnouncements(childId) {
  const rows = (await api.get('/student/announcements' + forChild(childId))) ?? []
  return [...rows].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
}

/* ------------------------------------------------------------ yangiliklar */

/**
 * How many items one call asks for. §5.5 caps `take` at 50; the card shows
 * three and `Barchasi` reveals the rest, so 20 is the whole of what the card
 * can ever need (§3.4).
 */
const NEWS_TAKE = 20

/**
 * `NewsFeedDto[]`, newest first.
 *
 * The contract does not pin the envelope down, so both shapes are accepted —
 * a bare array and `{ rows: [...] }` (the admin list uses the latter). Sorting
 * here as well as on the server costs nothing and keeps the card honest about
 * "newest first" whichever way the rows arrive.
 */
function newsRows(body) {
  const rows = Array.isArray(body) ? body : (body?.rows ?? [])
  return [...rows].sort((a, b) => (String(a.publishedAt) < String(b.publishedAt) ? 1 : -1))
}

/**
 * Published school news for this reader (§5.5).
 *
 * TWO ENDPOINTS, ONE CONTRACT. `/tg/parent/news` is the Mini App's own reader
 * and it is parent-gated; this same panel also serves PUPIL accounts
 * (`ParentPanel` is rendered for `role === 'student'` too), and for them that
 * endpoint answers 403. `/student/news` accepts both roles and picks the
 * audience from the caller's own role, so it is a fallback, not a second
 * feature.
 *
 * A MISSING ENDPOINT IS AN EMPTY FEED, not an error: until the reader
 * endpoints are deployed, an error card on the Bosh tab would tell every
 * parent that something is broken when nothing is. Every other status still
 * throws — a 500 must not be disguised as "no news".
 */
export async function getNews(take = NEWS_TAKE, role) {
  const query = `?take=${take}`

  // A pupil's login (how families sign in) goes straight to its own reader — no 403 first.
  if (role !== 'student') {
    try {
      return newsRows(await api.get('/tg/parent/news' + query))
    } catch (e) {
      const retryAsPupil =
        e instanceof ApiError && (e.status === 403 || e.status === 404 || e.status === 405)
      if (!retryAsPupil) throw e
    }
  }

  try {
    return newsRows(await api.get('/student/news' + query))
  } catch (e) {
    if (e instanceof ApiError && (e.status === 404 || e.status === 405)) return []
    throw e
  }
}

/* -------------------------------------------------- Farzandni olib ketish */

/** Bugungi pickup so'rovi holati (yo'q bo'lsa — `null`). */
export const getPickup = (childId) => api.get('/student/pickup' + forChild(childId))

/** "Farzandimni olib ketaman" — sinf rahbariga so'rov yuboradi. */
export const requestPickup = (childId) => api.post('/student/pickup', { studentId: childId })

/* ---------------------------------------------------------------- Baholar */

/** Fan × chorak o'rtacha baholari + chorak bo'yicha davomat jamlamasi. */
export const getGrades = (childId) => api.get('/student/grades' + forChild(childId))

/* ------------------------------------------------- Taklif va shikoyatlar */

/** Mening murojaatlarim — holati (qabul qilindi → jarayonda → hal qilindi) va javobi bilan. */
export const getMyFeedback = () => api.get('/student/feedback')

/** Murojaat yuboriladigan bo'limlar. */
export const getFeedbackDepartments = () => api.get('/student/feedback/departments')

/** Yangi murojaat (matn + ixtiyoriy rasm) — tanlangan farzand nomidan. */
export function sendFeedback(childId, formData) {
  if (childId) formData.append('studentId', childId)
  return api.form('/student/feedback', formData)
}

/** Sertifikatlar (ball / daraja), yangisi tepada — "hozircha necha balli" (mijoz, 2026-10-07). */
export const getCertificates = (childId) => api.get('/student/certificates' + forChild(childId))

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

/**
 * Chekni foydalanuvchining O'Z Telegram chatiga yuboradi (bot hujjat qilib).
 *
 * Telegram ichida `downloadReceipt` ishonchsiz: Android WebView blob yuklashni
 * jimgina e'tiborsiz qoldiradi, iOS PDF'ni ilova ustida ochib yuboradi. Chatga
 * kelgan faylni esa har qanday telefon ochadi va saqlaydi.
 */
export const sendReceiptToTelegram = (paymentId) => api.post(`/tg/receipts/${paymentId}/send`)

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

/* ------------------------------------------------------------ so'rovnomalar */

/** Open polls for parents, answered per child (client, 2026-10-07). */
export const getPolls = (childId) => api.get('/student/polls' + forChild(childId))

export const answerPoll = (childId, pollId, answers) =>
  api.post(`/student/polls/${pollId}` + forChild(childId), { answers })
