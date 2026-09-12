/**
 * Moliya endpoint'larining xato javobini o'qish (P1-17).
 *
 * Server `BillingErrorDto { code, message }` qaytaradi (qarang
 * `BillingCatalogController.BillingFaultAttribute`). Bu yerda uch narsa
 * ajratiladi:
 *
 *  - `billingErrorMessage` — foydalanuvchiga ko'rsatiladigan o'zbekcha matn;
 *  - `billingErrorCode`    — mashina o'qiydigan kod (`self_approval`,
 *    `subscription_overlap`, ...) — shartli mantiq FAQAT shunga qaraydi,
 *    matnga emas;
 *  - `isEndpointMissing`   — 404/501: endpoint hali ulanmagan. Buni oddiy
 *    xatodan ajratish kerak, chunki ekranda ko'rsatiladigan xabar boshqa:
 *    "server ishlamadi" emas, "bu qism hali tayyor emas".
 */

/** Server qaytaradigan moliya xatosi. */
export interface BillingErrorBody {
  code?: string
  message?: string
}

interface HttpErrorShape {
  response?: {
    status?: number
    data?: BillingErrorBody | string | null
  }
  message?: string
}

function asHttpError(err: unknown): HttpErrorShape {
  return typeof err === 'object' && err !== null ? (err as HttpErrorShape) : {}
}

function body(err: unknown): BillingErrorBody | null {
  const data = asHttpError(err).response?.data
  return typeof data === 'object' && data !== null ? data : null
}

/** HTTP status kodi (tarmoq uzilsa — undefined). */
export function billingErrorStatus(err: unknown): number | undefined {
  return asHttpError(err).response?.status
}

/** Mashina o'qiydigan xato kodi (`self_approval`, `category_code_immutable`, ...). */
export function billingErrorCode(err: unknown): string | undefined {
  return body(err)?.code
}

/** Foydalanuvchiga ko'rsatiladigan o'zbekcha xabar. */
export function billingErrorMessage(err: unknown, fallback = 'Xatolik yuz berdi'): string {
  const serverMessage = body(err)?.message
  if (serverMessage) return serverMessage

  const status = billingErrorStatus(err)
  if (status === 401) return 'Sessiya tugagan — qaytadan kiring.'
  if (status === 403) return "Bu amalga ruxsatingiz yo'q."
  if (status === 404) return 'Ma\'lumot topilmadi.'
  if (status && status >= 500) return 'Serverda xatolik. Keyinroq urinib ko\'ring.'
  if (status === undefined) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."

  const raw = asHttpError(err).message
  return raw ?? fallback
}

/**
 * Endpoint hali yozilmaganmi? 404 = marshrut yo'q, 501 = ataylab ulanmagan.
 *
 * Chiqimlar (P1-13) uchun kerak: shartnoma bu yerda muzlatilgan, backend
 * esa keyinroq keladi — ekran buni "server buzildi" deb emas, "bu bo'lim
 * hali ulanmagan" deb ko'rsatishi kerak.
 */
export function isEndpointMissing(err: unknown): boolean {
  const status = billingErrorStatus(err)
  return status === 404 && !body(err)?.code
}
