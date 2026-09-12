/** Ko'rinish uchun formatlash — butun Mini App bo'ylab bitta manba. */

/** 1 800 000 → "1 800 000". Bo'sh ajratgich — probel, nuqta yoki vergul emas. */
export function sum(value) {
  if (value === null || value === undefined) return '—'
  const n = Math.round(Number(value))
  return n.toLocaleString('ru-RU').replace(/ /g, ' ')
}

/** Pul + birlik. Manfiy — qarz. */
export function money(value) {
  return `${sum(value)} so'm`
}

/** Katta raqamni qisqartirish: 14 000 000 → "14 mln". Panel kartochkalari uchun. */
export function shortSum(value) {
  const n = Number(value ?? 0)
  if (Math.abs(n) >= 1_000_000) return `${(n / 1_000_000).toFixed(n % 1_000_000 === 0 ? 0 : 1)} mln`
  if (Math.abs(n) >= 1_000) return `${Math.round(n / 1_000)} ming`
  return sum(n)
}

const MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

const WEEKDAYS = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']

function toDate(value) {
  if (!value) return null
  const d = value instanceof Date ? value : new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

/** "12 sentabr" */
export function dayMonth(value) {
  const d = toDate(value)
  return d ? `${d.getDate()} ${MONTHS[d.getMonth()]}` : '—'
}

/** "12 sentabr 2026" */
export function fullDate(value) {
  const d = toDate(value)
  return d ? `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}` : '—'
}

/** "Sentabr 2026" — sarlavhadagi oy o'tkagichi uchun. */
export function monthTitle(year, monthIndex0) {
  const m = MONTHS[monthIndex0]
  return `${m.charAt(0).toUpperCase()}${m.slice(1)} ${year}`
}

/** "12 sentabr, 14:30" */
export function dateTime(value) {
  const d = toDate(value)
  if (!d) return '—'
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return `${dayMonth(d)}, ${hh}:${mm}`
}

/** 0=dushanba … 5=shanba — backend shu asosda ishlaydi. */
export function weekdayName(day) {
  return WEEKDAYS[day] ?? '—'
}

/** Bugun 0=dushanba asosida. */
export function todayIndex() {
  return (new Date().getDay() + 6) % 7
}
