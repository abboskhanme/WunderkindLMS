/**
 * KATAKCHA ORTIDAGI SATRLAR (docs/modules/finance-parity.md §2.5 F5.03,
 * §2.7 F7.03, §2.4 F4.02).
 *
 * Hisobotdagi har bir raqam bosiladi va shu oyna ochiladi: raqamni HOSIL
 * QILGAN jurnal satrlari, na ko'p, na kam. Oynaning yuqorisidagi yig'indi
 * ham SERVERDAN keladi va u katakning aynan o'zi — ya'ni "hisobotda bir
 * raqam, ro'yxatda boshqa" degan holat tuzilish bo'yicha bo'lishi mumkin
 * emas (buni backend testi o'lchaydi).
 *
 * Ikki manba bor va ikkovi ham shu oynadan ochiladi:
 *   · `ledger`   — P&L katagi: bitta hisob yoki `revenue:*` guruhi;
 *   · `cashflow` — pul oqimi katagi: toifa yoki to'lov usuli. Bu yerda
 *     satrning summasi uning KATAKKA TUSHGAN qismi bo'ladi (taqsimlangan
 *     to'lov har toifada o'z bo'lagi bilan ko'rinadi).
 *
 * Storno YASHIRILMAYDI — sariq qatorda, minus bilan turadi (SPEC §4.1).
 */
import { useMemo } from 'react'
import { Receipt, Undo2 } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  getCashFlowLines,
  getLedgerLines,
  type LedgerLine,
} from '@/api/services/financeStatements'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { accountLabel, formatSignedMoney, paymentMethodLabel, signClass } from './reportLabels'

/** Qaysi katak ochildi — oynaning yagona kirishi. */
export type LedgerDetailsRequest =
  | {
      source: 'ledger'
      title: string
      subtitle?: string
      /** Hisob kodi yoki guruh (`revenue:*`). */
      account: string
      from: string
      to: string
    }
  | {
      source: 'cashflow'
      title: string
      subtitle?: string
      /** Toifa kaliti. Berilmasa — hamma toifa (usul bo'yicha drill-down). */
      key?: string
      /** To'lov usuli bo'yicha filtr. */
      method?: string
      from: string
      to: string
      /** `cash` yoki `bank`; berilmasa — ikkovi. */
      account?: string
    }

interface Props {
  request: LedgerDetailsRequest | null
  onClose: () => void
}

interface Loaded {
  lines: LedgerLine[]
  total: number
  count: number
  truncated: boolean
  inflow: number | null
  outflow: number | null
}

export function LedgerDetailsModal({ request, onClose }: Props) {
  // So'rov OBYEKTI har renderda qayta quriladi, shuning uchun kalit sifatida
  // uning matni ishlatiladi — aks holda `useAsync` cheksiz qayta yuklardi.
  const key = request ? JSON.stringify(request) : ''

  const { data, loading, error, refetch } = useAsync<Loaded | null>(
    async () => {
      if (!request) return null

      if (request.source === 'ledger') {
        const result = await getLedgerLines(request.account, request.from, request.to)
        return {
          lines: result.lines,
          total: result.total,
          count: result.count,
          truncated: result.truncated,
          inflow: null,
          outflow: null,
        }
      }

      const result = await getCashFlowLines({
        key: request.key,
        method: request.method,
        from: request.from,
        to: request.to,
        account: request.account,
      })
      return {
        lines: result.lines,
        total: result.total.amount,
        count: result.count,
        truncated: result.truncated,
        inflow: result.total.inflow,
        outflow: result.total.outflow,
      }
    },
    [key],
  )

  const period = useMemo(
    () => (request ? `${formatDate(request.from)} — ${formatDate(request.to)}` : ''),
    [request],
  )

  return (
    <Modal open={!!request} onClose={onClose} size="xl" title={request?.title ?? 'Tafsilot'}>
      {loading ? (
        <Loader label="Yuklanmoqda…" />
      ) : error ? (
        <div className="rounded-xl border border-red-200 bg-red-50/60 p-4 text-sm text-red-700">
          <p className="font-medium text-red-800">Satrlarni yuklab bo'lmadi</p>
          <p className="mt-1">{error}</p>
          <button
            type="button"
            onClick={refetch}
            className="mt-3 rounded-lg bg-white px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-50"
          >
            Qayta urinish
          </button>
        </div>
      ) : data ? (
        <div className="space-y-4">
          <div className="flex flex-wrap items-end justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3">
            <div className="min-w-0">
              <p className="text-sm text-slate-500">{request?.subtitle ?? period}</p>
              <p className="mt-0.5 text-xs text-slate-400">
                {data.count} ta yozuv
                {data.truncated && ` · ro'yxatda eng yangi ${data.lines.length} tasi`}
                {data.inflow !== null && data.outflow !== null && (
                  <>
                    {' · '}kirim {formatMoney(data.inflow)} · chiqim {formatMoney(data.outflow)}
                  </>
                )}
              </p>
            </div>
            <div className="text-right">
              <p className="text-xs uppercase tracking-wide text-slate-400">Katak yakuni</p>
              <p className={cn('text-lg font-semibold', signClass(data.total))}>
                {formatSignedMoney(data.total)}
              </p>
            </div>
          </div>

          {data.lines.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">
              Bu katak ortida jurnal satri yo'q.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Sana</th>
                    <th className="px-3 py-2">Nima</th>
                    <th className="px-3 py-2">Kim</th>
                    <th className="px-3 py-2">Hisob</th>
                    <th className="px-3 py-2">Chek</th>
                    <th className="px-3 py-2 text-right">Summa</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {data.lines.map((line) => (
                    <tr
                      key={line.entryId}
                      className={cn(line.isReversal ? 'bg-amber-50/70' : 'hover:bg-slate-50/60')}
                    >
                      <td className="whitespace-nowrap px-3 py-2 text-slate-500">
                        {formatDate(line.entryDate)}
                      </td>
                      <td className="px-3 py-2">
                        <p className="font-medium text-slate-800">
                          {line.isReversal && (
                            <Undo2 className="mr-1.5 inline h-3.5 w-3.5 text-amber-600" />
                          )}
                          {line.title}
                        </p>
                        <p className="mt-0.5 text-xs text-slate-400">
                          {line.kindLabel}
                          {line.method && ` · ${paymentMethodLabel(line.method)}`}
                          {line.memo && ` · ${line.memo}`}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-slate-500">
                        {line.person ?? line.actorName ?? '—'}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-slate-500">
                        {accountLabel(line.account)}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-slate-500">
                        {typeof line.receiptNo === 'number' ? (
                          <span className="inline-flex items-center gap-1">
                            <Receipt className="h-3.5 w-3.5 text-slate-300" />№{line.receiptNo}
                          </span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td
                        className={cn(
                          'whitespace-nowrap px-3 py-2 text-right font-semibold',
                          signClass(line.signed),
                        )}
                      >
                        {formatSignedMoney(line.signed)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}
    </Modal>
  )
}
