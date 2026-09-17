/**
 * O'QITUVCHI PANELINING YAGONA API QATLAMI.
 *
 * Bugun so'rovlar mavjud o'qituvchi portaliga (`/api/teacher/...`) boradi — u
 * allaqachon ishlaydi va bizga kerak bo'lgan hamma narsani beradi. Backend
 * `/api/tg/teacher/...` ni chiqarganda pastdagi `BASE` ni bitta qatorda
 * o'zgartirish kifoya; agar faqat ayrim yo'llar ko'chsa — o'sha funksiyaning
 * bitta qatori o'zgaradi. EKRANLAR HECH QACHON `api` NI TO'G'RIDAN-TO'G'RI
 * CHAQIRMAYDI, shuning uchun ko'chirish narxi shu fayl bilan cheklanadi.
 */
import { api } from './api'

/** Endpoint prefiksi. `/tg/teacher` ga o'tganda faqat shu qator o'zgaradi. */
const BASE = '/teacher'

/** Bo'sh/undefined qiymatlarni tashlab, so'rov satrini yig'adi. */
function qs(params) {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params || {})) {
    if (v !== undefined && v !== null && v !== '') p.append(k, String(v))
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

/** Kanal nomi yo'lga tushadi: "5-A" ham, "__xodimlar__" ham xavfsiz kodlanadi. */
const channelPath = (name) => `${BASE}/chat/${encodeURIComponent(name)}`

export const teacherApi = {
  /* ---------- profil va umumiy kontekst ---------- */

  /** O'qituvchining o'zi: ism, sinf rahbarligi, fanlar va RUXSATLAR ro'yxati. */
  profile: () => api.get(`${BASE}/me`),
  /** Choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta. */
  meta: () => api.get(`${BASE}/meta`),
  /** Dars beradigan sinflar (sinf rahbarligi ham shu ro'yxatda). */
  classes: () => api.get(`${BASE}/classes`),

  /* ---------- jadval ---------- */

  /** Bir haftalik darslar. quarter/week berilmasa — server joriysini oladi. */
  schedule: (quarter, week) => api.get(`${BASE}/schedule${qs({ quarter, week })}`),

  /** Chorak bo'yicha o'tilgan darslar progresi (reja / o'tilgan / kesimlar). */
  progress: (quarter) => api.get(`${BASE}/progress${qs({ quarter })}`),

  /* ---------- jurnal / davomat ---------- */

  /** Sinf o'quvchilari (jurnal ro'yxati — bu yerda pul ko'rsatilmaydi). */
  journalStudents: (classId) => api.get(`${BASE}/journal/students${qs({ classId })}`),
  /** Chorakdagi dars kataklari (sana + dars raqami). */
  journalColumns: (classId, subjectId, quarter) =>
    api.get(`${BASE}/journal/columns${qs({ classId, subjectId, quarter })}`),
  /** Chorakdagi baho va davomat yozuvlari. */
  journalEntries: (classId, subjectId, quarter) =>
    api.get(`${BASE}/journal${qs({ classId, subjectId, quarter })}`),
  /** Bitta katak: baho va/yoki davomat sababi. */
  setJournalEntry: (body) => api.put(`${BASE}/journal`, body),
  /** Katakni butunlay tozalash (davomat belgisi olib tashlanganda). */
  clearJournalEntry: ({ classId, subjectId, quarter, studentId, date, period }) =>
    api.del(`${BASE}/journal${qs({ classId, subjectId, quarter, studentId, date, period })}`),
  /** Dars qaydlari: mavzu, uyga vazifa va "dars o'tildi" belgisi. */
  journalNotes: (classId, subjectId, quarter) =>
    api.get(`${BASE}/journal/notes${qs({ classId, subjectId, quarter })}`),
  /**
   * Dars qaydini yozish. DIQQAT: mavzu va uyga vazifa ham shu so'rovda ustiga
   * yoziladi — chaqiruvchi ularni mavjud qayddan olib uzatishi SHART, aks holda
   * o'qituvchi kiritgan mavzu o'chib ketadi.
   */
  setJournalNote: (body) => api.put(`${BASE}/journal/notes`, body),

  /* ---------- maosh ---------- */

  /** O'quv yili bo'yicha maosh daftari: oylar, to'lovlar, qoldiq. */
  salary: () => api.get(`${BASE}/salary`),

  /* ---------- xabarlar ---------- */

  /**
   * O'qituvchi a'zo bo'lgan chat kanallari — `{ key, label, kind }` ro'yxati
   * (sinflar, o'quv guruhlari va xodimlar guruhi). O'quv guruhining kaliti
   * `grp:<id>`, ya'ni uni ekranda ko'rsatib bo'lmaydi — shuning uchun nom
   * (`label`) serverdan keladi (G-17).
   */
  chatChannels: () => api.get(`${BASE}/chat/channels`),
  /** Har kanalning oxirgi xabar vaqti (ISO) yoki null. */
  chatLastMessages: () => api.get(`${BASE}/chat/last-messages`),
  /** Kanal xabarlari. `since` berilsa — faqat undan keyingilari (yangilanish/o'qilmagan). */
  chatMessages: (channel, since) => api.get(`${channelPath(channel)}${qs({ since })}`),
  /** Kanalga xabar yuborish. */
  sendChatMessage: (channel, text) => api.post(channelPath(channel), { text }),
}
