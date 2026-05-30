export interface WeekRange {
  /** Hafta raqami (1-based) */
  week: number
  /** Hafta boshi (ISO) — chorak chegarasiga qisilgan (odatda dushanba, birinchi haftada chorak boshlanish sanasi) */
  startISO: string
  /** Hafta oxiri (ISO) — chorak chegarasiga qisilgan (odatda shanba, oxirgi haftada chorak tugash sanasi) */
  endISO: string
}

function toISO(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(
    d.getDate(),
  ).padStart(2, '0')}`
}

export function addDaysISO(iso: string, n: number): string {
  const d = new Date(iso)
  d.setDate(d.getDate() + n)
  return toISO(d)
}

/** Berilgan sana joylashgan haftaning dushanbasi (ISO) */
export function mondayOfISO(iso: string): string {
  const d = new Date(iso)
  const dow = (d.getDay() + 6) % 7
  d.setDate(d.getDate() - dow)
  return toISO(d)
}

/** Bugungi sanaga mos chorak va hafta raqamini qaytaradi.
 * Joriy chorak topilmasa: keyingi yaqin chorak (ta'il), u ham yo'q bo'lsa oxirgi o'tgan chorak,
 * hech narsa topilmasa { quarter: 1, week: 1 }. */
export function getCurrentQuarterAndWeek(
  quarters: Array<{ quarter: number; startDate?: string; endDate?: string }>,
): { quarter: number; week: number } {
  const today = toISO(new Date())

  const active = quarters.find(
    (q) => q.startDate && q.endDate && q.startDate <= today && today <= q.endDate,
  )
  if (active?.startDate && active.endDate) {
    const wks = getQuarterWeeks(active.startDate, active.endDate)
    const w = wks.find((x) => x.startISO <= today && today <= x.endISO)
    return { quarter: active.quarter, week: w?.week ?? wks[0]?.week ?? 1 }
  }

  const next = quarters
    .filter((q) => q.startDate && q.startDate > today)
    .sort((a, b) => (a.startDate ?? '').localeCompare(b.startDate ?? ''))[0]
  if (next?.startDate && next.endDate) {
    const wks = getQuarterWeeks(next.startDate, next.endDate)
    return { quarter: next.quarter, week: wks[0]?.week ?? 1 }
  }

  const prev = quarters
    .filter((q) => q.endDate && q.endDate < today)
    .sort((a, b) => (b.endDate ?? '').localeCompare(a.endDate ?? ''))[0]
  if (prev?.startDate && prev.endDate) {
    const wks = getQuarterWeeks(prev.startDate, prev.endDate)
    return { quarter: prev.quarter, week: wks[wks.length - 1]?.week ?? 1 }
  }

  return { quarter: 1, week: 1 }
}

/**
 * Chorak sanalari oralig'ini dushanbadan boshlanuvchi haftalarga bo'lish.
 * Hafta diapazoni chorak chegaralariga QISILADI — chorakdan tashqaridagi kunlar (masalan
 * chorak 25-mayda tugasa, o'sha haftaning 26–30-may kunlari) jadvalga kiritilmaydi.
 * startISO endi har doim ham dushanba bo'lavermaydi (birinchi/oxirgi hafta chorak chetiga
 * qisilgan bo'lishi mumkin); haftadagi dars sanasini hisoblashda mondayOfISO(startISO) ishlating.
 */
export function getQuarterWeeks(startISO: string, endISO: string): WeekRange[] {
  const result: WeekRange[] = []
  const start = new Date(startISO)
  const end = new Date(endISO)
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || start > end) {
    return result
  }

  // start sanasi joylashgan haftaning dushanbasi.
  // EDGE: yakshanba kalendar hafta oxiri — shu haftaning dushanbasi (6 kun oldin) chorakdan
  // butunlay tashqarida bo'ladi va 1-hafta yo'qoladi. Shu sababli chorak yakshanba'da
  // boshlansa, KEYINGI dushanbani olamiz (haqiqiy birinchi o'quv hafta = 1-hafta).
  const firstMonday = new Date(start)
  const dow = (firstMonday.getDay() + 6) % 7 // 0=Dushanba ... 6=Yakshanba
  if (dow === 6) {
    firstMonday.setDate(firstMonday.getDate() + 1) // keyingi dushanba
  } else {
    firstMonday.setDate(firstMonday.getDate() - dow) // shu haftaning dushanbasi
  }

  const cursor = new Date(firstMonday)
  let week = 1
  while (cursor <= end) {
    const ws = new Date(cursor)
    const we = new Date(cursor)
    we.setDate(we.getDate() + 5) // Shanba
    // Chorak chegarasiga qisamiz.
    const clampedStart = ws < start ? startISO : toISO(ws)
    const clampedEnd = we > end ? endISO : toISO(we)
    // Chorak ichida hech bo'lmaganda bitta o'quv kuni (Du–Sha) bo'lgan haftanigina qo'shamiz.
    if (clampedStart <= clampedEnd) {
      result.push({ week, startISO: clampedStart, endISO: clampedEnd })
    }
    cursor.setDate(cursor.getDate() + 7)
    week++
  }
  return result
}
