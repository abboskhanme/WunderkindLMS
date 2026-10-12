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
import { api, ApiError } from './api'

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

/**
 * How many news items one call asks for. §5.5 caps `take` at 50; the card
 * shows three and `Barchasi` reveals the rest (§3.4).
 */
const NEWS_TAKE = 20

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

  /* ---------- yangiliklar ---------- */

  /**
   * Published news for employees — §5.5 `GET /api/tg/teacher/news`.
   *
   * NOT UNDER `BASE`. Every other call in this file still goes to the teacher
   * portal (`/api/teacher/...`); the news reader is a Mini App endpoint and
   * lives on the `/api/tg/teacher` prefix, so it is written out in full here
   * and a later move of `BASE` leaves it alone.
   *
   * A MISSING ENDPOINT IS AN EMPTY FEED (404/405): until the reader endpoints
   * are deployed, an error card on the Bugun tab would report a fault that
   * does not exist. Any other status still throws.
   */
  news: async (take = NEWS_TAKE) => {
    try {
      const body = await api.get(`/tg/teacher/news${qs({ take })}`)
      const rows = Array.isArray(body) ? body : (body?.rows ?? [])
      return [...rows].sort((a, b) => (String(a.publishedAt) < String(b.publishedAt) ? 1 : -1))
    } catch (e) {
      if (e instanceof ApiError && (e.status === 404 || e.status === 405)) return []
      throw e
    }
  },

  /* ---------- dars tahlili (faqat o'zi haqida) ---------- */

  /**
   * O'z dars tahlillarim. Server faqat YAKUNLANGANLARini beradi — qoralama
   * kuzatuvchida qoladi (veb sahifadagi "Dars tahlilim" bilan bir xil manba).
   */
  lessonAnalyses: () => api.get(`${BASE}/lesson-analysis/observations${qs({ limit: 100 })}`),
  /** Bitta tahlil: mezonlar javobi, xulosalar, AI tahlil va mezonlar katalogi (`sections`). */
  lessonAnalysis: (id) => api.get(`${BASE}/lesson-analysis/observations/${encodeURIComponent(id)}`),

  /* ---------- jarimalar ---------- */

  /** O'zimga yozilgan jarimalar, yangisi tepada; bekor qilingani `cancelled` bilan. */
  penalties: () => api.get(`${BASE}/penalties`),

  /* ---------- taklif va shikoyatlar ---------- */

  /** Mening murojaatlarim — holati va javobi bilan. */
  myFeedback: () => api.get(`${BASE}/feedback`),
  /** Murojaat yuboriladigan bo'limlar. */
  feedbackDepartments: () => api.get(`${BASE}/feedback/departments`),
  /** Yangi murojaat (matn + ixtiyoriy rasm). */
  sendFeedback: (formData) => api.form(`${BASE}/feedback`, formData),

  // So'rovnomalar (client, 2026-10-07) — open polls for teachers, one answer sheet each.
  polls: () => api.get(`${BASE}/polls`),
  answerPoll: (id, answers) => api.post(`${BASE}/polls/${id}`, { answers }),

  /* ---------- kechki dars va yotoqxona — komendant roli (2026-10-12) ---------- */

  // The admin roll-call endpoints themselves: the teacher reaches them through the access role on
  // their account (profile.rolePermissions); without it the server answers 403.
  /** One day × session: sections, pupils and their marks. */
  boardingDay: (date, session) => api.get(`/admin/boarding-attendance${qs({ date, session })}`),
  /** Saves marks — `{ saved, notified }` (notified = parents told by Telegram). */
  saveBoarding: (date, session, marks) => api.put('/admin/boarding-attendance', { date, session, marks }),

  /* ---------- vazifalar — o'quvchilarga topshiriqlar (2026-10-12) ---------- */

  // The teacher portal's assignments (the old web app's "Topshiriqlar"): test, written, file, video.
  /** Vazifalarim, yangisi tepada — sinflar, materiallar va (test bo'lsa) to'g'ri javobli savollar bilan. */
  assignments: () => api.get(`${BASE}/assignments`),
  createAssignment: (body) => api.post(`${BASE}/assignments`, body),
  updateAssignment: (id, body) => api.put(`${BASE}/assignments/${encodeURIComponent(id)}`, body),
  deleteAssignment: (id) => api.del(`${BASE}/assignments/${encodeURIComponent(id)}`),
  /** Kim topshirdi / topshirmadi — har o'quvchining javobi (matn, fayl) va bali. */
  assignmentResults: (id) => api.get(`${BASE}/assignments/${encodeURIComponent(id)}/results`),
  /** Bajardi/bajarmadi + ball (o'qituvchi baholaydi). */
  setSubmission: (id, studentId, body) =>
    api.put(`${BASE}/assignments/${encodeURIComponent(id)}/submissions/${encodeURIComponent(studentId)}`, body),
  /** Material fayli (PDF, rasm, hujjat, ~20 MB gacha) — `{ name, url, size, contentType }`. */
  uploadMaterial: (file) => {
    const form = new FormData()
    form.append('file', file)
    return api.form(`${BASE}/uploads`, form)
  },
}
