/**
 * TO'LOV — hisob-fakturalar, qarz va cheklar.
 *
 * QOIDALAR (veb-portaldagi `FinanceView.tsx` bilan bir xil, chunki bu
 * ikkalasi bitta `/api/student/billing` javobini ko'rsatadi):
 *   1. Hisob-fakturalar OY bo'yicha, oy ichida TOIFA kesimida — ota-ona nima
 *      uchun to'layotganini bilishi kerak.
 *   2. To'lovlar tarixi (cheklar bilan) ota-onaga KO'RSATILMAYDI (mijoz,
 *      2026-09-29) — faqat qarz va hisob-fakturalar.
 *   3. Ekranda birorta ichki id yo'q.
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
  ChevronDown, ChevronRight, ReceiptText, TriangleAlert, Wallet,
} from 'lucide-react'
import { Badge, Card, EmptyState, Row } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth, money, sum } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getBilling } from '../../lib/parentApi'
import { AsyncBlock, balanceText, lineStatus, monthLabel } from './shared'
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
          {/* "To'lovlar tarixi" ota-onaga ko'rsatilmaydi (mijoz, 2026-09-29: "ota ona profilida
              to'lov tarixi ko'rinmasin"). Kassir va admin uni o'quvchi kartochkasida ko'radi. */}
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
          To'lov maktab kassasida qabul qilinadi. Chekni kassada olasiz.
        </p>
      )}
    </Card>
  )
}

/* ------------------------------------------------------- to'lanmaganlari */

function DebtCard({ lines }) {
  // Stays on screen when nothing is owed (client, 2026-10-07: the same screen for everyone).
  if (lines.length === 0) {
    return (
      <Card title="Nimalar to'lanmagan">
        <EmptyState title="To'lanmagan narsa yo'q" note="Barcha chiqarilgan hisob-fakturalar to'langan." />
      </Card>
    )
  }

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
            <div className="shrink-0 text-right">
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
          <div key={m.periodMonth} className="border-t border-slate-100">
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
                  <div className="shrink-0 text-right">
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
