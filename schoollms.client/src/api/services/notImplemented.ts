/**
 * P1-06 da muzlatilgan API stub'lari uchun yagona xato.
 *
 * Nega bo'sh massiv/mock emas: "ishlayotgandek ko'rinadigan" ekran eng
 * qimmat xato — u sinovdan jimgina o'tib ketadi va prodda topiladi.
 * Bu xato esa ekranda darrov ko'rinadi va QAYSI vazifa hali tayyor
 * emasligini aytib turadi.
 */
export class NotImplementedError extends Error {
  /** Backend endpoint — masalan "POST /admin/billing/discounts". */
  readonly endpoint: string
  /** Shu endpoint'ni yozadigan vazifa ID'si — masalan "P1-08". */
  readonly task: string
  /** Chaqiruvda berilgan argumentlar (nosozlikni tekshirish uchun). */
  readonly payload?: unknown

  constructor(endpoint: string, task: string, payload?: unknown) {
    super(`${endpoint} hali tayyor emas (${task}). Shartnoma P1-06 da muzlatilgan.`)
    this.name = 'NotImplementedError'
    this.endpoint = endpoint
    this.task = task
    this.payload = payload
  }
}

/**
 * Stub tanasi. Qaytish tipi `Promise<never>` — shuning uchun uni istalgan
 * `Promise<T>` o'rniga qo'yish mumkin va imzo TO'G'RI tekshiriladi
 * (`as any` kerak emas, TypeScript shartnomani himoya qilishda davom etadi).
 */
export function notImplemented(endpoint: string, task: string, payload?: unknown): Promise<never> {
  return Promise.reject(new NotImplementedError(endpoint, task, payload))
}
