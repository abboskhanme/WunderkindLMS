/**
 * Pul aylanmasi grafi — 3D halqa vizualizatsiyasi uchun (P1-26).
 *
 * Backend: `GET /api/admin/finance/money-flow?from&to`
 * (`SchoolLms.Server/Controllers/MoneyFlowController.cs`).
 *
 * Tiplar SHU YERDA e'lon qilinadi, `@/types` da emas: P1-26 boshqa vazifalar
 * bilan parallel yozildi va `types/index.ts` P1-06 ning fayli. Bitta modul —
 * bitta shartnoma fayli; birlashtirish kerak bo'lsa keyin bir qatorda ko'chadi.
 *
 * Mock yo'q (yangi moliya modullaridagi `billing.ts` / `payments.ts` bilan bir
 * xil): "ishlayotgandek ko'rinadigan" soxta halqa eng qimmat xato bo'lardi.
 */
import { api } from '../client'

/** Tugun turi: kirim manbai | markaz | chiqim | sof natija. */
export type MoneyFlowKind = 'income' | 'hub' | 'expense' | 'net'

export interface MoneyFlowNode {
  /** Hisob kodi (`revenue:tuition`, `expense:salary`) yoki `hub` / `net`. */
  id: string
  /** O'zbekcha nom — backend beradi. */
  label: string
  kind: MoneyFlowKind
  /**
   * So'mda, tiyingacha. Odatda musbat; storno davr chegarasida qolsa manfiy
   * bo'lishi mumkin — bu YASHIRILMAYDI (yashirish balansni buzardi).
   */
  value: number
}

export interface MoneyFlowLink {
  source: string
  target: string
  value: number
}

/**
 * KAFOLAT (backend testlari bilan qulflangan): kiruvchi bog'lanishlar
 * yig'indisi = markaziy tugun qiymati = chiquvchi bog'lanishlar yig'indisi,
 * tiyingacha. Frontend bu tenglikni qayta hisoblamaydi va "tuzatmaydi".
 *
 * Ma'lumot yo'q davrda ikkala ro'yxat ham bo'sh bo'ladi.
 */
export interface MoneyFlow {
  nodes: MoneyFlowNode[]
  links: MoneyFlowLink[]
}

/**
 * Davr uchun pul aylanmasi.
 * @param from "YYYY-MM-DD" (kiritiladi)
 * @param to   "YYYY-MM-DD" (kiritiladi)
 */
export async function getMoneyFlow(from: string, to: string): Promise<MoneyFlow> {
  const { data } = await api.get<MoneyFlow>('/admin/finance/money-flow', {
    params: { from, to },
  })
  return data
}

/**
 * Summani so'mda, mingliklar ajratilgan holda: "412 350 000 so'm".
 *
 * `lib/utils.ts` dagi umumiy `formatMoney` dan farqi — TIYIN. U `Intl` ning
 * standart sozlamasida ishlaydi va 1234.50 ni "1 234,5" qilib ko'rsatadi;
 * pul uchun bu noto'g'ri. Bu yerda kasr qismi bo'lsa DOIM ikki xona, bo'lmasa
 * umuman ko'rsatilmaydi (yuz million so'mning yonida ",00" faqat shovqin).
 *
 * Formatlash shu faylda, chunki uni ham sahifa, ham halqa ishlatadi va
 * umumiy `lib/utils.ts` boshqa vazifalarning fayli.
 */
export function formatSom(value: number): string {
  const digits = Math.abs(value % 1) > 1e-9 ? 2 : 0
  const text = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value)
  return `${text} so'm`
}
