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
import { api, ApiError, tokenStore } from './api'

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

  /* ---------- mavsumiy baholash (2026-10-28) — docs/modules/admission-and-testing.md §6.6 ---------- */

  /** Faqat o'qituvchi dars beradigan sinf·fan juftlari. `seasonalMarks` ruxsati bo'lmasa — 403. */
  seasonalScope: () => api.get(`${BASE}/seasonal-marks/scope`),
  /** Bitta juftning davrdagi o'quvchilari, saqlangan ball va izoh bilan. */
  seasonalStudents: (query) => api.get(`${BASE}/seasonal-marks/students${qs(query)}`),
  /** Faqat o'zgargan qatorlar; ball ham izoh ham bo'sh — shu davrdagi bahoni o'chirish. */
  seasonalBulk: (body) => api.post(`${BASE}/seasonal-marks/bulk`, body),

  /* ---------- jadval ---------- */

  /** Bir haftalik darslar. quarter/week berilmasa — server joriysini oladi. */
  schedule: (quarter, week) => api.get(`${BASE}/schedule${qs({ quarter, week })}`),

  /** Chorak bo'yicha o'tilgan darslar progresi (reja / o'tilgan / kesimlar). */
  progress: (quarter) => api.get(`${BASE}/progress${qs({ quarter })}`),

  /** Sinf rahbari: rahbarlikdagi sinf/yo'nalish guruhi o'quvchilari va qarzi (2026-10-12). Rahbar bo'lmasa — bo'sh. */
  homeroomDebts: () => api.get(`${BASE}/homeroom/debts`),

  /**
   * Shu ro'yxat Excel'da — o'qituvchining o'z Telegram chatiga (bot hujjati). Telegram ichida fayl
   * yuklab olish ishonchsiz, chatga kelgan faylni esa har telefon ochadi (ota-ona chekidagi yo'l).
   * `group` — sinf/guruh (bo'sh = hammasi), `all` — qarzi yo'qlar ham.
   */
  /**
   * Qarz bo'yicha izohlar (2026-10-29): admin "Qarzdorlar → Izoh" bilan bitta tarix. Faqat o'z o'quvchisi.
   */
  homeroomDebtComments: (studentId) =>
    api.get(`${BASE}/homeroom/debts/${encodeURIComponent(studentId)}/comments`),
  /** Izoh yozish: `{ comment }` — ota-ona bilan nima kelishildi. */
  addHomeroomDebtComment: (studentId, comment) =>
    api.post(`${BASE}/homeroom/debts/${encodeURIComponent(studentId)}/comments`, { comment }),

  homeroomDebtsToTelegram: ({ group, all } = {}) =>
    api.post(`${BASE}/homeroom/debts/telegram${qs({ group, all: all || undefined })}`),

  /**
   * Qizil ro'yxat (2026-10-15): rahbarlikdagi o'quvchilarning ochiq holatlari — ketma-ket / sababsiz
   * kelmaganlar, ota-ona telefonlari va amallar tarixi. Rahbar bo'lmasa — bo'sh.
   */
  homeroomRedList: () => api.get(`${BASE}/homeroom/red-list`),
  /** Holatga amal: `{ status, comment, callbackOn }` — yangilangan holatni qaytaradi. */
  addRedListAction: (caseId, body) =>
    api.post(`${BASE}/homeroom/red-list/${encodeURIComponent(caseId)}/actions`, body),

  /** Sinfim · Kelmaganlar (2026-10-26): shu kuni kelmagan o'quvchilarim, sabab va izoh. */
  homeroomAbsences: (date) => api.get(`${BASE}/homeroom/absences?date=${encodeURIComponent(date)}`),
  /** Kelmagan kunning sababi va/yoki izohi: `{ reasonId, comment }` — yangilangan qatorni qaytaradi. */
  saveHomeroomAbsence: (studentId, date, body) =>
    api.put(`${BASE}/homeroom/absences/${encodeURIComponent(studentId)}/${encodeURIComponent(date)}`, body),

  /** Telegramdan tashqarida (brauzerda) — o'sha Excel to'g'ridan-to'g'ri yuklab olinadi. */
  downloadHomeroomDebts: async ({ group, all } = {}) => {
    const token = tokenStore.get()
    const res = await fetch(`/api${BASE}/homeroom/debts/export${qs({ group, all: all || undefined })}`, {
      headers: token ? { Authorization: 'Bearer ' + token } : {},
    })
    if (!res.ok) throw new ApiError(res.status, { message: "Excel faylni olib bo'lmadi" })
    const blob = await res.blob()
    const name = /filename\*=UTF-8''([^;]+)/i.exec(res.headers.get('content-disposition') || '')?.[1]
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = name ? decodeURIComponent(name) : 'qarzdorlar.xlsx'
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
  },

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

  /* ---------- xulq-atvor (2026-10-15, docs/modules/behaviour.md) ---------- */

  /** Faol sabablar: `{ id, name, points, type: 'positive' | 'negative', description, notifyParent }`. */
  disciplineReasons: () => api.get(`${BASE}/discipline/reasons`),
  /** Dars ro'yxatidagi har o'quvchi: `{ studentId, remaining, today }` (100 dan boshlanadigan ball). */
  disciplineScores: (ownerId) => api.get(`${BASE}/discipline/scores${qs({ ownerId })}`),
  /** Bitta o'quvchi: ball, bugungi ballar va oxirgi yozuvlar (`canDelete` — o'zim bugun qo'yganim). */
  disciplinePoints: (studentId) => api.get(`${BASE}/discipline/points${qs({ studentId })}`),
  /** Darhol saqlanadi — darsning "Saqlash" tugmasiga bog'liq emas. */
  addDisciplinePoint: (body) => api.post(`${BASE}/discipline/points`, body),
  /** Faqat o'zim bugun qo'ygan yozuv. */
  deleteDisciplinePoint: (id) => api.del(`${BASE}/discipline/points/${encodeURIComponent(id)}`),

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

  /* ---------- bulk import of test questions (2026-10-15, docs/modules/test-import.md) ---------- */

  // Nothing is saved by these: the server reads the questions and the form receives them.
  /** Pasted text (an AI answer or the template format) — `{ unit, totalRows, validCount, errorCount, questions, errors }`. */
  parseQuestionsText: (text) => api.post(`${BASE}/assignments/questions/parse`, { text }),
  /** A file: .xlsx (the template), .docx, .txt or .json, up to 10 MB — the same answer. */
  parseQuestionsFile: (file) => {
    const form = new FormData()
    form.append('file', file)
    return api.form(`${BASE}/assignments/questions/parse`, form)
  },
  /** The Excel template to the teacher's own Telegram chat (downloads inside Telegram are unreliable). */
  questionTemplateToTelegram: () => api.post(`${BASE}/assignments/questions/template/telegram`),
  /** Outside Telegram (a browser) — the template is downloaded directly. */
  downloadQuestionTemplate: async () => {
    const token = tokenStore.get()
    const res = await fetch(`/api${BASE}/assignments/questions/template`, {
      headers: token ? { Authorization: 'Bearer ' + token } : {},
    })
    if (!res.ok) throw new ApiError(res.status, { message: "Shablonni olib bo'lmadi" })
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'test_savollari_shablon.xlsx'
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
  },
}
