/**
 * Hal qilinmagan NOMUVOFIQLIK kuzatuvchisi (SPEC §4.6) — P1-18.
 *
 * Bitta manba, ikki iste'molchi: yopib bo'lmaydigan banner
 * (`VarianceBanner`) va "Nomuvofiqlik" tabi (`VarianceTab`). Ikkalasi
 * alohida so'rov yuborsa, ekranda ikkita har xil raqam paydo bo'lishi
 * mumkin edi — moliyada bu qabul qilinmaydi.
 *
 * IKKI MANBA, IKKI DARAJA
 * -----------------------
 * 1. `GET /api/cash/shifts?onlyWithVariance=true` — BUGUN ishlaydi. Yopilgan
 *    va nomuvofiqligi nolga teng bo'lmagan har bir smena. `variance` bazada
 *    generated column, ya'ni bu raqamni hech kim "tuzatib" qo'ya olmaydi.
 * 2. `GET /api/admin/finance/flags?unresolved=true` — P1-14 (tungi anomaliya
 *    skaneri). ULANGAN. Hisoblagich shu manbadan olinadi: u faqat
 *    nomuvofiqlikni emas, SPEC §4.6 dagi to'rtta belgini ham qamraydi va
 *    SABAB bilan yopilishini eslab qoladi. Endpoint 404 qaytarsa (eski
 *    server) birinchi manbaga qaytiladi.
 *
 * Jurnal ulanmaganda hisoblagich JIMGINA nolga tushmaydi — `flagsNote`
 * ekranda buni ochiq aytadi. "Anomaliya yo'q" bilan "skaner ishlamayapti"
 * ni chalkashtirish — aynan shu modul oldini olishi kerak bo'lgan xato.
 */
import { useMemo } from 'react'
import type { CashShift } from '@/types'
import { useAsync } from '@/hooks/useAsync'
import {
  getCashShifts,
  getFinanceFlags,
  type FinanceFlag,
  type FinanceFlagsResult,
} from '@/api/services/financeReports'

export interface VarianceWatch {
  loading: boolean
  error: string | null
  /** Nomuvofiqligi bor yopilgan smenalar (eng yangisi yuqorida). */
  shifts: CashShift[]
  /** P1-14 bayroqlari. Jurnal ulanmagan bo'lsa — bo'sh. */
  flags: FinanceFlag[]
  /** Anomaliya jurnali (P1-14) ishlayaptimi. */
  flagsAvailable: boolean
  /** Jurnal ulanmagan bo'lsa — buning sababi (ekranda ko'rsatiladi). */
  flagsNote: string | null
  /** Hal qilinmagan nomuvofiqliklar soni — banner shu raqamni ko'rsatadi. */
  count: number
  refetch: () => void
}

interface WatchData {
  shifts: CashShift[]
  flags: FinanceFlagsResult
}

async function fetchWatch(): Promise<WatchData> {
  const [shifts, flags] = await Promise.all([
    getCashShifts({ onlyWithVariance: true }),
    getFinanceFlags(true),
  ])
  return { shifts, flags }
}

/**
 * @param enabled Ruxsati bo'lmagan foydalanuvchi uchun `false`. So'rov
 * umuman yuborilmaydi: `/admin/finance/*` kassir va xodimga 403 beradi, va
 * "ruxsat yo'q" xatosini ekranga chiqarishdan ko'ra so'ramaslik to'g'ri.
 */
export function useVarianceWatch(enabled = true): VarianceWatch {
  const { data, loading, error, refetch } = useAsync(
    () => (enabled ? fetchWatch() : Promise.resolve(null)),
    [enabled],
  )

  return useMemo(() => {
    const shifts = [...(data?.shifts ?? [])].sort((a, b) =>
      (b.closedAt ?? b.openedAt).localeCompare(a.closedAt ?? a.openedAt),
    )
    const flagsResult = data?.flags
    const flagsAvailable = flagsResult?.available === true
    const flags = flagsResult?.available ? flagsResult.flags : []

    return {
      loading,
      error,
      shifts,
      flags,
      flagsAvailable,
      flagsNote: flagsResult && !flagsResult.available ? flagsResult.reason : null,
      // `flags.length` EMAS: server o'zi sanagan to'liq sonni beradi va
      // ro'yxat kelajakda sahifalansa ham banner to'g'ri qoladi.
      count: flagsResult?.available ? flagsResult.unresolved : shifts.length,
      refetch,
    }
  }, [data, loading, error, refetch])
}
