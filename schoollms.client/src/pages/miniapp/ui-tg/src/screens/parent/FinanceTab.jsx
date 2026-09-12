/**
 * TO'LOV — hisob-fakturalar, qarz va cheklar.
 *
 * TO'RTTA QOIDA (veb-portaldagi `FinanceView.tsx` bilan bir xil, chunki bu
 * ikkalasi bitta `/api/student/billing` javobini ko'rsatadi):
 *   1. Hisob-fakturalar OY bo'yicha, oy ichida TOIFA kesimida — ota-ona nima
 *      uchun to'layotganini bilishi kerak.
 *   2. Har to'lovda chek bor (SPEC §4.7 — maktab o'zgartira olmaydigan nusxa).
 *   3. Bekor qilingan to'lov YASHIRILMAYDI: ustidan chizilgan holda qoladi va
 *      sababi yoziladi.
 *   4. Ekranda birorta ichki id yo'q — chek raqami bor, uuid yo'q.
 *
 * PULNI SERVER QO'SHADI. Bu faylda birorta `+` yo'q: oy jamlari ham, qarz ham
 * `PortalFinanceController` da `decimal` da hisoblanadi. JavaScript'ning
 * `number` i — float64, tiyin yo'qoladi.
 *
 * QARZ HECH QACHON MANFIY SON EMAS: "Qarz: 450 000 so'm" yoki "Avans:
 * 200 000 so'm" — minus belgisi ota-onaga hech narsa aytmaydi.
 */
import { useState } from 'react'
import {
  ChevronDown, ChevronRight, FileDown, ReceiptText, TriangleAlert, Wallet,
} from 'lucide-react'
import { Badge, Card, EmptyState, Row } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dateTime, dayMonth, money, sum } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { downloadReceipt, getBilling } from '../../lib/parentApi'
import { AsyncBlock, balanceText, lineStatus, methodLabel, monthLabel } from './shared'
import { parseISO } from './weeks'

export function FinanceTab({ child }) {
  const state = useAsync(() => getBilling(child.id), [child.id])

  return (
    <AsyncBlock state={state} loadingLabel="To'lov ma'lumoti yuklanmoqda…">
      {(billing) => (
        <>
          <SummaryCard billing={billing} />
          <DebtCard lines={billing.debtLines ?? []} />
          <MonthsCard months={billing.months ?? []} />
          <PaymentsCard childId={child.id} payments={billing.payments ?? []} />
        </>
      )}
    </AsyncBlock>
  )
}

/* --------------------------------------------------------------- umumiy */

function SummaryCard({ billing }) {
  const b = balanceText(billing.debt, billing.credit)
  const tone = {
    danger: 'bg-red-50 text-red-600',
    success: 'bg-emerald-50 text-emerald-600',
  }[b.tone]

  return (
    <Card>
      <div className="flex items-center gap-3 px-4 pb-4 pt-4">
        <div className={'flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl ' + tone}>
          <Wallet className="h-6 w-6" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-[13px] text-slate-500">{b.label}</p>
          <p
            className={
              'text-[24px] font-extrabold leading-tight ' +
              (b.tone === 'danger' ? 'text-red-600' : 'text-emerald-600')
            }
          >
            {b.value}
          </p>
        </div>
      </div>
      {billing.debt > 0 && (
        <p className="border-t border-slate-100 px-4 py-3 text-[13px] leading-relaxed text-slate-500">
          To'lov maktab kassasida qabul qilinadi. To'lagach chek shu yerda paydo bo'ladi va uni
          yuklab olishingiz mumkin.
        </p>
      )}
    </Card>
  )
}

/* ------------------------------------------------------- to'lanmaganlari */

function DebtCard({ lines }) {
  if (lines.length === 0) return null

  return (
    <Card title="Nimalar to'lanmagan">
      {lines.map((l) => (
        <Row
          key={`${l.periodMonth}-${l.categoryCode}`}
          lead={
            <div
              className={
                'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
                (l.isOverdue ? 'bg-red-50 text-red-600' : 'bg-slate-100 text-slate-500')
              }
            >
              <TriangleAlert className="h-5 w-5" />
            </div>
          }
          title={`${l.categoryName} · ${monthLabel(l.periodMonth)}`}
          subtitle={`To'lash muddati: ${dayMonth(parseISO(l.dueOn))}`}
          right={
            <div className="text-right">
              <p className="text-[15px] font-bold text-red-600">{sum(l.remaining)}</p>
              {l.isOverdue && <p className="text-[12px] text-red-500">muddati o'tgan</p>}
            </div>
          }
        />
      ))}
    </Card>
  )
}

/* -------------------------------------------------------- hisob-fakturalar */

function MonthsCard({ months }) {
  // Eng yangi oy ochiq turadi — ota-ona ko'pincha o'shani qidiradi.
  const [open, setOpen] = useState(() => new Set(months[0] ? [months[0].periodMonth] : []))

  const toggle = (key) => {
    haptic('light')
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  if (months.length === 0) {
    return (
      <Card title="Hisob-fakturalar">
        <EmptyState
          icon={<ReceiptText className="h-8 w-8" />}
          title="Hisob-faktura yo'q"
          note="Bu farzand uchun hali birorta oylik hisob-faktura chiqarilmagan. Shartnoma rasmiylashtirilgach ular shu yerda paydo bo'ladi."
        />
      </Card>
    )
  }

  return (
    <Card title="Hisob-fakturalar">
      {months.map((m) => {
        const expanded = open.has(m.periodMonth)
        const paidOff = m.remaining <= 0
        return (
          <div key={m.periodMonth}>
            <Row
              onClick={() => toggle(m.periodMonth)}
              lead={
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-500">
                  {expanded ? <ChevronDown className="h-5 w-5" /> : <ChevronRight className="h-5 w-5" />}
                </div>
              }
              title={monthLabel(m.periodMonth)}
              subtitle={`To'lanishi kerak: ${money(m.payable)} · to'langan: ${sum(m.paid)}`}
              right={
                paidOff ? (
                  <Badge tone="success">To'langan</Badge>
                ) : (
                  <div className="text-right">
                    <p className="text-[15px] font-bold text-red-600">{sum(m.remaining)}</p>
                    {m.hasOverdue && <p className="text-[12px] text-red-500">muddati o'tgan</p>}
                  </div>
                )
              }
            />
            {expanded &&
              m.categories.map((c) => {
                const st = lineStatus(c)
                return (
                  <div
                    key={c.categoryCode}
                    className="flex items-center gap-3 border-t border-slate-100 bg-slate-50/60 px-4 py-2.5 pl-[68px]"
                  >
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[14px] font-medium">{c.categoryName}</p>
                      <p className="text-[12px] text-slate-500">
                        {money(c.payable)}
                        {c.discount > 0 ? ` · chegirma ${sum(c.discount)}` : ''}
                      </p>
                    </div>
                    <Badge tone={st.tone}>{st.label}</Badge>
                  </div>
                )
              })}
          </div>
        )
      })}
    </Card>
  )
}

/* ------------------------------------------------------------- to'lovlar */

function PaymentsCard({ childId, payments }) {
  const [open, setOpen] = useState(() => new Set())
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)

  const toggle = (id) => {
    haptic('light')
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const take = async (p) => {
    setBusy(p.paymentId)
    setError(null)
    try {
      await downloadReceipt(childId, p.paymentId, p.receiptNo)
      haptic('medium')
    } catch (e) {
      // Xato AYNAN shu to'lov ostida chiqsin — ro'yxatda o'nta chek bo'lishi mumkin.
      setError({ id: p.paymentId, message: e.message })
    } finally {
      setBusy(null)
    }
  }

  if (payments.length === 0) {
    return (
      <Card title="To'lovlar tarixi">
        <EmptyState
          icon={<ReceiptText className="h-8 w-8" />}
          title="To'lov qilinmagan"
          note="Kassada to'lov qabul qilinishi bilan chek raqami bilan shu yerda ko'rinadi."
        />
      </Card>
    )
  }

  return (
    <Card title="To'lovlar tarixi">
      {payments.map((p) => {
        const expanded = open.has(p.paymentId)
        const reversed = Boolean(p.reversal)
        return (
          <div key={p.paymentId}>
            <Row
              onClick={() => toggle(p.paymentId)}
              lead={
                <div
                  className={
                    'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
                    (reversed ? 'bg-slate-100 text-slate-400' : 'bg-emerald-50 text-emerald-600')
                  }
                >
                  <ReceiptText className="h-5 w-5" />
                </div>
              }
              title={
                <span className={reversed ? 'line-through text-slate-400' : ''}>
                  {money(p.amount)}
                </span>
              }
              subtitle={`${dateTime(p.receivedAt)} · ${methodLabel(p.method)} · chek №${p.receiptNo}`}
              right={
                reversed ? (
                  <Badge tone="neutral">Bekor qilingan</Badge>
                ) : expanded ? (
                  <ChevronDown className="h-5 w-5 shrink-0 text-slate-400" />
                ) : (
                  <ChevronRight className="h-5 w-5 shrink-0 text-slate-400" />
                )
              }
            />

            {expanded && (
              <div className="border-t border-slate-100 bg-slate-50/60 px-4 py-3">
                {p.parts.map((a) => (
                  <div
                    key={`${a.periodMonth}-${a.categoryCode}`}
                    className="flex items-baseline gap-2 py-0.5"
                  >
                    <span className="min-w-0 flex-1 truncate text-[13px] text-slate-600">
                      {monthLabel(a.periodMonth)} · {a.categoryName}
                    </span>
                    <span className="shrink-0 text-[13px] font-semibold">{sum(a.amount)}</span>
                  </div>
                ))}

                {p.unallocated > 0 && (
                  <div className="flex items-baseline gap-2 py-0.5">
                    <span className="min-w-0 flex-1 text-[13px] text-slate-600">
                      Avans (keyingi oylarga)
                    </span>
                    <span className="shrink-0 text-[13px] font-semibold">{sum(p.unallocated)}</span>
                  </div>
                )}

                {p.note && <p className="mt-1 text-[12px] text-slate-400">Izoh: {p.note}</p>}

                {reversed && (
                  <p className="mt-2 rounded-xl bg-red-50 p-2.5 text-[12px] leading-relaxed text-red-700">
                    To'lov bekor qilingan ({dateTime(p.reversal.reversedAt)}, chek №
                    {p.reversal.receiptNo}). Sabab: {p.reversal.reason}
                  </p>
                )}

                <button
                  type="button"
                  onClick={() => take(p)}
                  disabled={busy === p.paymentId}
                  className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white py-3 text-[14px] font-semibold disabled:opacity-50"
                >
                  <FileDown className="h-4 w-4" />
                  {busy === p.paymentId ? 'Yuklanmoqda…' : 'Chekni yuklab olish (PDF)'}
                </button>
                {error?.id === p.paymentId && (
                  <p className="mt-2 text-[12px] text-red-600">{error.message}</p>
                )}
              </div>
            )}
          </div>
        )
      })}
    </Card>
  )
}
