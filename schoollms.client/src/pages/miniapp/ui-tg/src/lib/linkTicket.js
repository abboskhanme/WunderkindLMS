/**
 * Bog'lash chiptasi (2026-10-07). Server uni cookie qilib ham qo'yadi, lekin Telegram Web'da Mini App
 * boshqa saytdagi iframe ichida ochiladi va cookie qaytib bormasligi mumkin — shuning uchun javob
 * tanasidagi chipta shu yerda saqlanadi va login so'roviga sarlavha bilan qo'shiladi.
 * Chiqishdan yoki "bog'lanmagan" javobidan keyin keladi, ulangach o'chadi.
 */
let ticket = ''

export const linkTicket = {
  get: () => ticket,
  set: (value) => {
    ticket = typeof value === 'string' ? value : ''
  },
  clear: () => {
    ticket = ''
  },
  /** Login so'rovi uchun sarlavha (chipta bo'lmasa — bo'sh). */
  headers: () => (ticket ? { 'X-Telegram-Link-Ticket': ticket } : undefined),
}
