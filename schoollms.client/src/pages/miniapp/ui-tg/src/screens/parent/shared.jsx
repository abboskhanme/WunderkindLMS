/**
 * Ota-ona tablari uchun umumiy mayda qismlar.
 *
 * BU YERDA YANGI DIZAYN TIZIMI YO'Q. Fayl faqat `components/ui.jsx` dagi
 * tayyor bloklarni birlashtiradi va bir necha marta takrorlanadigan matn
 * qoidasini bitta joyda saqlaydi (masalan pulni hech qachon manfiy son
 * ko'rinishida ko'rsatmaslik).
 */
import { ErrorState, Loader } from '../../components/ui'
import { money, sum } from '../../lib/format'

/**
 * `useAsync` holatini ekranga aylantiradi: yuklanish → xato (qayta urinish
 * bilan) → ma'lumot. Bo'shlik holatini har tab O'ZI hal qiladi — "bo'sh"
 * degani har ekranda boshqacha (jadvalda dars yo'q, to'lovda hisob-faktura
 * yo'q) va sababini ham har biri boshqacha tushuntiradi.
 */
export function AsyncBlock({ state, loadingLabel = 'Yuklanmoqda…', children }) {
  if (state.loading) return <Loader label={loadingLabel} />
  if (state.error) return <ErrorState message={state.error} onRetry={state.reload} />
  if (state.data === null || state.data === undefined) {
    return <ErrorState message="Ma'lumot bo'sh keldi." onRetry={state.reload} />
  }
  return children(state.data)
}

/**
 * Qarz/avansning MATNI. Ota-ona hech qachon yalang'och manfiy son
 * ko'rmasligi kerak: "−1 800 000" o'rniga "Qarz: 1 800 000 so'm".
 *
 * `debt` va `credit` ikkalasi ham musbat son bo'lib keladi
 * (`PortalFinanceDto`), shuning uchun bu yerda arifmetika yo'q — faqat
 * qaysi so'zni qo'yish tanlanadi.
 */
export function balanceText(debt, credit) {
  if (debt > 0) return { label: 'Qarz', value: money(debt), tone: 'danger' }
  if (credit > 0) return { label: 'Avans', value: money(credit), tone: 'success' }
  return { label: 'Holat', value: "Qarz yo'q", tone: 'success' }
}

/** Kartochkadagi qisqa pul yozuvi: "Qarz 1.8 mln" / "Avans 200 ming". */
export function balanceShort(debt, credit) {
  if (debt > 0) return { value: sum(debt), label: "Qarz, so'm" }
  if (credit > 0) return { value: sum(credit), label: "Avans, so'm" }
  return { value: '0', label: "Qarz yo'q" }
}

/** Baho rangi: 5–4 yaxshi, 3 o'rtacha, 2 va past — diqqat. */
export function gradeTone(grade) {
  if (grade >= 4) return 'success'
  if (grade === 3) return 'neutral'
  return 'danger'
}

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "2026-09" → "Sentabr 2026". */
export function monthLabel(period) {
  const [year, month] = String(period ?? '').split('-')
  return `${MONTHS[Number(month) - 1] ?? month} ${year}`
}

/** To'lov usuli — faqat yorliq, provayder integratsiyasi yo'q (SPEC §8.1). */
const METHODS = {
  cash: 'Naqd',
  card: 'Karta',
  transfer: "Bank o'tkazmasi",
  online: 'Onlayn',
}

/** Noma'lum usul kelsa ham qator ko'rinadi — kodning o'zi yoziladi. */
export const methodLabel = (method) => METHODS[method] ?? method

/** Hisob-faktura holati — ota-ona tilida. */
const STATUS = {
  open: "To'lanmagan",
  partial: 'Qisman',
  paid: "To'langan",
  void: 'Bekor qilingan',
}

/**
 * Ko'rsatiladigan holat. `status` va `remaining` zid bo'lsa — PULGA
 * ishonamiz (`FinanceView.tsx` dagi bilan bir xil qoida): qizil qoldiq
 * yonidagi "To'langan" yozuvi ota-onani to'lamaslikka olib boradi.
 */
export function lineStatus(line) {
  if (line.status === 'void') return { label: STATUS.void, tone: 'neutral' }
  if (line.remaining <= 0) return { label: STATUS.paid, tone: 'success' }
  return line.paid > 0
    ? { label: STATUS.partial, tone: 'brand' }
    : { label: STATUS.open, tone: 'danger' }
}
