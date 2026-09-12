/**
 * O'quv haftalari va ISO sanalar — ota-ona paneli uchun.
 *
 * SERVER MANTIG'INING NUSXASI. `ScheduleMath.GetQuarterWeeks` (C#) chorakni
 * dushanbadan boshlanuvchi haftalarga bo'ladi va hafta chetlarini chorak
 * chegarasiga qisadi. Jadval endpointi `?quarter=&week=` so'raydi, ya'ni
 * klient qaysi hafta qaysi sanalar ekanini BILISHI shart — aks holda
 * "keyingi hafta" tugmasi nimaga o'tayotganini ayta olmaydi. Shu sababli
 * hisob shu yerda takrorlangan; formulani o'zgartirish kerak bo'lsa,
 * ikkalasi ham o'zgaradi.
 *
 * SANALAR MAHALLIY VAQTDA. `new Date('2026-09-07')` ni brauzer UTC deb
 * o'qiydi va manfiy zonada kun ORQAGA suriladi. Shuning uchun ISO satr
 * qo'lda bo'linadi va `new Date(y, m, d)` bilan yig'iladi.
 */

/** "YYYY-MM-DD" → mahalliy `Date` (yarim tunda). */
export function parseISO(iso) {
  if (!iso) return null
  const [y, m, d] = iso.split('-').map(Number)
  if (!y || !m || !d) return null
  return new Date(y, m - 1, d)
}

/** `Date` → "YYYY-MM-DD" (mahalliy kun). */
export function toISO(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Bugungi kun ISO ko'rinishida. */
export const todayISO = () => toISO(new Date())

/** ISO sanaga kun qo'shish. */
export function addDaysISO(iso, n) {
  const d = parseISO(iso)
  if (!d) return iso
  d.setDate(d.getDate() + n)
  return toISO(d)
}

/** 0=dushanba … 6=yakshanba (backend shu tartibda ishlaydi). */
export function lessonDow(iso) {
  const d = parseISO(iso)
  return d ? (d.getDay() + 6) % 7 : 0
}

/** Sana joylashgan haftaning dushanbasi. */
export function mondayOfISO(iso) {
  return addDaysISO(iso, -lessonDow(iso))
}

/**
 * Chorakni haftalarga bo'ladi — `ScheduleMath.GetQuarterWeeks` bilan bir xil,
 * shu jumladan ikkita nozik joyi:
 *   • chorak yakshanbada boshlansa 1-hafta KEYINGI dushanbadan boshlanadi;
 *   • hafta raqami har doim oshadi, hatto o'sha hafta chorakka tushmasa ham.
 */
export function quarterWeeks(startISO, endISO) {
  const out = []
  if (!startISO || !endISO || startISO > endISO) return out

  const dow = lessonDow(startISO)
  let cursor = dow === 6 ? addDaysISO(startISO, 1) : addDaysISO(startISO, -dow)
  let week = 1
  // Chorak bir necha oy — 40 hafta chegarasi cheksiz sikldan himoya.
  while (cursor <= endISO && week <= 40) {
    const ws = cursor
    const we = addDaysISO(cursor, 5) // shanba
    const clampedStart = ws < startISO ? startISO : ws
    const clampedEnd = we > endISO ? endISO : we
    if (clampedStart <= clampedEnd) out.push({ week, startISO: clampedStart, endISO: clampedEnd })
    cursor = addDaysISO(cursor, 7)
    week++
  }
  return out
}

/**
 * Butun o'quv yilining haftalari bitta tekis ro'yxatda:
 * `[{ quarter, week, startISO, endISO }]`. Stepper shu ro'yxat bo'ylab
 * yuradi, ya'ni chorak chegarasidan o'tish alohida holat emas.
 */
export function schoolYearWeeks(quarters) {
  return (quarters ?? [])
    .slice()
    .sort((a, b) => a.quarter - b.quarter)
    .flatMap((q) =>
      quarterWeeks(q.startDate, q.endDate).map((w) => ({ quarter: q.quarter, ...w })),
    )
}

/**
 * Ro'yxatdan berilgan sanani o'z ichiga olgan haftani topadi. Sana ta'til
 * kunlariga tushsa (choraklar orasi) — undan keyingi eng yaqin hafta, u ham
 * bo'lmasa oxirgisi. Hech qachon -1 qaytmaydi (ro'yxat bo'sh bo'lmasa).
 */
export function weekIndexFor(weeks, iso) {
  if (!weeks.length) return -1
  const exact = weeks.findIndex((w) => iso >= w.startISO && iso <= w.endISO)
  if (exact >= 0) return exact
  const next = weeks.findIndex((w) => w.startISO > iso)
  return next >= 0 ? next : weeks.length - 1
}

/** Kalendar haftasi (dushanba–yakshanba) — oshxona menyusi shu oraliqda so'raladi. */
export function calendarWeek(iso) {
  const start = mondayOfISO(iso)
  return { startISO: start, endISO: addDaysISO(start, 6) }
}
