/**
 * Ota-ona ekranlariga kerak bo'lgan, `src/lib/weeks.js` da YO'Q sana
 * yordamchilari.
 *
 * CHORAK → HAFTA HISOBI BU YERDA TAKRORLANMAYDI. U `src/lib/weeks.js` da
 * (`quarterWeeks`, `weekAxis`) va o'sha yagona nusxa ishlatiladi: bitta
 * formulaning ikkita nusxasi yozilgan kuni bir xil, tuzatilgan kuni har xil
 * bo'lib qoladi. Bu fayl faqat ustiga qo'shadi.
 *
 * SANALAR MAHALLIY VAQTDA. `new Date('2026-09-07')` ni brauzer UTC deb
 * o'qiydi va manfiy zonada kun ORQAGA suriladi. Shuning uchun ISO satr qo'lda
 * bo'linadi va `new Date(y, m, d)` bilan yig'iladi.
 */
import { addDays, mondayOf } from '../../lib/weeks'

/** "YYYY-MM-DD" → mahalliy `Date` (yarim tunda). `format.js` shuni kutadi. */
export function parseISO(iso) {
  if (!iso) return null
  const [y, m, d] = String(iso).split('-').map(Number)
  if (!y || !m || !d) return null
  return new Date(y, m - 1, d)
}

/** 0=dushanba … 6=yakshanba (serverdagi `LessonDow` bilan bir xil). */
export function lessonDow(iso) {
  const d = parseISO(iso)
  return d ? (d.getDay() + 6) % 7 : 0
}

/**
 * Hafta o'qidan berilgan sanani o'z ichiga olgan haftani topadi. Sana
 * choraklar orasidagi ta'tilga tushsa — undan keyingi eng yaqin hafta, u ham
 * bo'lmasa oxirgisi. Ro'yxat bo'sh bo'lmasa hech qachon -1 qaytmaydi:
 * ta'tilda ilovani ochgan ota-ona bo'sh ekran emas, keyingi haftani ko'radi.
 */
export function weekIndexFor(axis, iso) {
  if (!axis.length) return -1
  const exact = axis.findIndex((w) => iso >= w.startISO && iso <= w.endISO)
  if (exact >= 0) return exact
  const next = axis.findIndex((w) => w.startISO > iso)
  return next >= 0 ? next : axis.length - 1
}

/**
 * Kalendar haftasi (dushanba–yakshanba). Oshxona menyusi o'quv haftasiga
 * emas, kalendarga bog'liq — dam olish kunida ham ovqat bo'ladi.
 */
export function calendarWeek(iso) {
  const start = mondayOf(iso)
  return { startISO: start, endISO: addDays(start, 6) }
}
