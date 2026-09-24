import axios from 'axios'

/** Chek QR kodi ochadigan ma'lumot — backend `PublicReceiptDto`. */
export interface PublicReceipt {
  receiptNo: number
  schoolName: string
  receivedAtText: string
  studentName: string
  className: string | null
  lines: { categoryName: string; periodText: string; amountText: string; statusText: string | null }[]
  totalText: string
  methodText: string
  cashierName: string
  /** valid — kuchda · cancelled — keyin storno qilingan · reversal — bu chekning o'zi storno */
  status: 'valid' | 'cancelled' | 'reversal'
  cancelledAtText: string | null
}

/**
 * Login'siz so'rov (QR'ni ota-ona telefoni ochadi) — shuning uchun umumiy `api` klienti emas: u token qo'shadi va
 * 401 da login sahifasiga yo'naltiradi.
 */
export async function getPublicReceipt(token: string): Promise<PublicReceipt> {
  const base = import.meta.env.VITE_API_BASE_URL ?? '/api'
  const res = await axios.get<PublicReceipt>(`${base}/public/receipts/${encodeURIComponent(token)}`)
  return res.data
}
