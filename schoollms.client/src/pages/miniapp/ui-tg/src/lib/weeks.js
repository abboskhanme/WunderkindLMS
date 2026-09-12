/**
 * Chorak → hafta hisob-kitobi.
 *
 * Bu serverdagi `ScheduleMath.GetQuarterWeeks` ning AYNAN nusxasi. Ikkalasi bir
 * xil natija bermasa, ilovadagi "2-hafta" server tushunadigan "2-hafta" bo'lmay
 * qoladi va jadval boshqa haftanikini ko'rsatadi — shuning uchun mantiq shu
 * yerda takrorlanadi va o'zgartirilmaydi.
 *
 * Sanalar hamma joyda "YYYY-MM-DD" satri. `Date` faqat kun qo'shish uchun
 * ishlatiladi (mahalliy yarim tun), ya'ni vaqt mintaqasi hisobga aralashmaydi.
 */

function parse(iso) {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

function toISO(date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** 0=dushanba … 6=yakshanba (serverdagi LessonDow bilan bir xil). */
function dow(date) {
  return (date.getDay() + 6) % 7
}

/** "YYYY-MM-DD" ga kun qo'shadi. */
export function addDays(iso, n) {
  const d = parse(iso)
  d.setDate(d.getDate() + n)
  return toISO(d)
}

/** Qurilma soatiga ko'ra bugungi sana, "YYYY-MM-DD". */
export function todayISO() {
  return toISO(new Date())
}

/** Sana joylashgan haftaning dushanbasi. */
export function mondayOf(iso) {
  const d = parse(iso)
  d.setDate(d.getDate() - dow(d))
  return toISO(d)
}

/**
 * Chorak oralig'ini dushanbadan boshlanuvchi haftalarga bo'ladi. Hafta chorak
 * chegarasiga qisiladi, ya'ni `startISO` har doim ham dushanba emas — haftaning
 * kunlarini hisoblashda `mondayOf(startISO)` dan boshlang.
 */
export function quarterWeeks(startISO, endISO) {
  const out = []
  if (!startISO || !endISO || startISO > endISO) return out

  // Chorak yakshanba'da boshlansa, o'sha haftaning dushanbasi chorakdan
  // butunlay tashqarida qoladi — bunday holda keyingi dushanbadan boshlaymiz.
  const start = parse(startISO)
  const firstMonday = new Date(start)
  firstMonday.setDate(start.getDate() + (dow(start) === 6 ? 1 : -dow(start)))

  const cursor = new Date(firstMonday)
  let week = 1
  while (toISO(cursor) <= endISO) {
    const wsISO = toISO(cursor)
    const weekEnd = new Date(cursor)
    weekEnd.setDate(cursor.getDate() + 5) // shanba
    const weISO = toISO(weekEnd)

    const clampedStart = wsISO < startISO ? startISO : wsISO
    const clampedEnd = weISO > endISO ? endISO : weISO
    if (clampedStart <= clampedEnd) out.push({ week, startISO: clampedStart, endISO: clampedEnd })

    cursor.setDate(cursor.getDate() + 7)
    week += 1
  }
  return out
}

/**
 * Barcha choraklarni bitta ketma-ket "hafta o'qi" ga yig'adi — Stepper shu
 * ro'yxat bo'ylab oldinga-orqaga yuradi va chorak chegarasidan o'zi o'tadi.
 */
export function weekAxis(quarters) {
  const axis = []
  for (const q of quarters || []) {
    for (const w of quarterWeeks(q.startDate, q.endDate)) {
      axis.push({ quarter: q.quarter, week: w.week, startISO: w.startISO, endISO: w.endISO })
    }
  }
  return axis
}

/** O'qdagi (chorak, hafta) ning indeksi; topilmasa -1. */
export function axisIndexOf(axis, quarter, week) {
  return axis.findIndex((a) => a.quarter === quarter && a.week === week)
}

/** Haftaning kun indeksi (0=dushanba) bo'yicha sana. */
export function dateOfWeekday(weekStartISO, dayIndex) {
  return addDays(mondayOf(weekStartISO), dayIndex)
}

/**
 * O'quv yilining oylari ("YYYY-MM") — birinchi chorak boshidan oxirgisi
 * oxirigacha. Maosh o'tkagichi shu o'q bo'ylab yuradi.
 */
export function academicMonths(quarters) {
  const list = quarters || []
  if (list.length === 0) return []
  const starts = list.map((q) => q.startDate).sort()
  const ends = list.map((q) => q.endDate).sort()
  const first = starts[0].slice(0, 7)
  const last = ends[ends.length - 1].slice(0, 7)

  const out = []
  let [y, m] = first.split('-').map(Number)
  for (let guard = 0; guard < 24; guard += 1) {
    const cur = `${y}-${String(m).padStart(2, '0')}`
    out.push(cur)
    if (cur >= last) break
    m += 1
    if (m > 12) {
      m = 1
      y += 1
    }
  }
  return out
}
