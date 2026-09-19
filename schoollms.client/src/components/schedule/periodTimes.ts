import type { SchoolSettings } from '@/types'
import { schedulePeriods } from '@/config/constants'

/* ==========================================================================
   DARS RAQAMI + SOATI

   Mijoz, 2026-09-19: "ikkala qismda ham dars ketma ketligi soatlari bilan
   bo'lishi kerak, kirish va chiqishlari bilan" — ya'ni jadvalning chap
   ustunida faqat "1, 2, 3..." emas, har bir darsning BOSHLANISH va
   TUGASH vaqti ham turadi.

   NEGA SHU YERDA: bir xil ustun ikkita ekranda kerak (sinf jadvali va
   o'qituvchi jadvali). Ikkalasi bir manbadan o'qisa, sozlamalarda soat
   o'zgarganda ikkalasi birdek o'zgaradi.

   MANBA: `Sozlamalar → Dars vaqtlari` (`settings.lessonTimes`). Soati
   kiritilmagan dars raqami faqat raqam bo'lib qolaveradi — bu xato emas,
   maktab hali kiritmagan degani.
   ========================================================================== */

export interface PeriodTime {
  start: string
  end: string
}

/** Sozlamalardagi dars vaqtlari — raqam bo'yicha qidirish uchun. */
export function periodTimeMap(settings: SchoolSettings | null): Map<number, PeriodTime> {
  const map = new Map<number, PeriodTime>()
  for (const t of settings?.lessonTimes ?? []) {
    if (t.startTime && t.endTime) map.set(t.period, { start: t.startTime, end: t.endTime })
  }
  return map
}

/**
 * Ko'rsatiladigan dars raqamlari.
 *
 * Kuniga 10 tagacha dars bo'lishi mumkin, lekin maktab 6 ta dars vaqtini
 * kiritgan bo'lsa — qolgan to'rt qator bo'sh va soatsiz turaveradi. Shuning
 * uchun eng kattasi bo'yicha kesamiz: kiritilgan vaqtlar va haqiqatda dars
 * qo'yilgan raqamlardan qaysi biri uzoqroqqa borsa — o'shanigacha.
 */
export function visiblePeriods(times: Map<number, PeriodTime>, used: number[]): number[] {
  const max = Math.max(0, ...times.keys(), ...used)
  return max === 0 ? schedulePeriods : schedulePeriods.filter((p) => p <= max)
}
