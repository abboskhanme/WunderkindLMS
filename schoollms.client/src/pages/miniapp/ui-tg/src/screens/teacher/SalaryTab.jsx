/**
 * MAOSH — bitta oy, ochiq hisob.
 *
 * O'qituvchining savoli har doim bitta oy haqida: "shu oyga qancha yozildi,
 * qancha berildi, qancha qoldi". Shuning uchun ekran oy o'tkagichi bilan
 * boshlanadi, o'quv yilining umumiy yig'indisi esa pastda turadi.
 *
 * Oy o'qi maosh daftaridan emas, CHORAKLARDAN quriladi: daftar faqat hisob
 * bo'lgan oylarni qaytaradi, o'qituvchi esa kelasi oyga ham qarab qo'yishi
 * mumkin — u yerda "hali hisoblanmagan" deb yozilgani jim bo'sh ekrandan yaxshi.
 */
import { useMemo, useState } from 'react'
import { Wallet } from 'lucide-react'
import {
  Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen, StatGrid, Stepper,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { academicMonths, todayISO } from '../../lib/weeks'
import { dayMonth, money, monthTitle, shortSum } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const STATUS = {
  paid: { tone: 'success', text: "To'liq to'langan" },
  partial: { tone: 'brand', text: "Qisman to'langan" },
  unpaid: { tone: 'danger', text: "To'lanmagan" },
}

/** "2026-09" → "Sentabr 2026". */
function labelOf(ym) {
  const [y, m] = ym.split('-').map(Number)
  return monthTitle(y, m - 1)
}

export function SalaryTab({ meta }) {
  const axis = useMemo(() => academicMonths(meta.quarters), [meta.quarters])
  const current = todayISO().slice(0, 7)
  const startIndex = Math.max(0, axis.indexOf(current))
  const [index, setIndex] = useState(startIndex)

  const q = useAsync(() => teacherApi.salary(), [])

  const ym = axis[index] || current
  const step = (delta) => {
    haptic()
    setIndex((i) => Math.min(axis.length - 1, Math.max(0, i + delta)))
  }

  const d = q.data
  const month = d ? (d.months || []).find((m) => m.month === ym) || null : null

  return (
    <Screen>
      <Hero
        title="Maosh"
        subtitle={d ? `Oylik stavka: ${money(d.salary)}` : "O'quv yili bo'yicha hisob"}
      >
        {axis.length > 0 && (
          <>
            <Stepper
              label={labelOf(ym)}
              onPrev={() => step(-1)}
              onNext={() => step(1)}
              disabledNext={index >= axis.length - 1}
            />
            {month && (
              <div className="mt-4">
                <HeroProgress
                  value={month.paid}
                  max={month.expected}
                  lead={shortSum(month.paid)}
                  note={`${shortSum(month.expected)} so'mdan to'landi`}
                />
              </div>
            )}
          </>
        )}
      </Hero>

      <AsyncBlock query={q} loadingLabel="Maosh yuklanmoqda…">
        {(data) => (
          <>
            {month ? (
              <>
                <StatGrid
                  items={[
                    { label: 'Hisoblangan', value: shortSum(month.expected), note: "so'm" },
                    { label: "To'langan", value: shortSum(month.paid), note: "so'm" },
                    { label: 'Qoldiq', value: shortSum(month.remaining), note: "so'm" },
                    { label: 'Oylik stavka', value: shortSum(data.salary), note: "so'm / oy" },
                  ]}
                />
                <Card
                  title={`${labelOf(ym)} to'lovlari`}
                  action={
                    <Badge tone={(STATUS[month.status] || STATUS.unpaid).tone}>
                      {(STATUS[month.status] || STATUS.unpaid).text}
                    </Badge>
                  }
                >
                  <MonthPayments payments={data.payments} ym={ym} />
                </Card>
              </>
            ) : (
              <Card title={labelOf(ym)}>
                <EmptyState
                  icon={<Wallet className="h-9 w-9" />}
                  title="Bu oyda hisob yo'q"
                  note="Bu oyga maosh hisoblanmagan: oy hali boshlanmagan yoki chorak jadvalidan tashqarida (ta'til)."
                />
              </Card>
            )}

            <Card
              title="O'quv yili bo'yicha"
              action={
                <Badge tone={data.remaining > 0 ? 'danger' : 'success'}>
                  {data.remaining > 0 ? `${shortSum(data.remaining)} qoldi` : "Qoldiq yo'q"}
                </Badge>
              }
            >
              {(data.months || []).length === 0 ? (
                <EmptyState
                  title="Hisoblangan oy yo'q"
                  note="Maosh dars jadvali va toifa narxidan hisoblanadi. Sizga hali jadval biriktirilmagan bo'lishi mumkin."
                />
              ) : (
                (data.months || []).map((m) => {
                  const st = STATUS[m.status] || STATUS.unpaid
                  return (
                    <Row
                      key={m.month}
                      title={labelOf(m.month)}
                      subtitle={`${shortSum(m.paid)} / ${shortSum(m.expected)} so'm`}
                      right={<Badge tone={st.tone}>{st.text}</Badge>}
                      onClick={
                        axis.indexOf(m.month) >= 0
                          ? () => {
                              haptic()
                              setIndex(axis.indexOf(m.month))
                            }
                          : undefined
                      }
                    />
                  )
                })
              )}
            </Card>

            <div className="mx-4 mt-3 rounded-card bg-white px-4 py-4">
              <SummaryLine label="Jami hisoblangan" value={money(data.totalExpected)} />
              <SummaryLine label="Jami olingan" value={money(data.totalPaid)} />
              <SummaryLine label="Qoldiq" value={money(data.remaining)} strong last />
            </div>
            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

function MonthPayments({ payments, ym }) {
  const list = (payments || []).filter((p) => (p.month || p.date || '').startsWith(ym))
  if (list.length === 0) {
    return (
      <EmptyState
        title="Bu oyda to'lov yo'q"
        note="Maosh berilganda buxgalteriya uni jurnalga yozadi va shu ro'yxatda darhol ko'rinadi."
      />
    )
  }
  return list.map((p, i) => (
    <Row
      key={`${p.date}-${i}`}
      title={p.note || `${labelOf(ym)} oyligi`}
      subtitle={dayMonth(p.date)}
      right={<span className="shrink-0 text-[15px] font-bold text-emerald-600">+{shortSum(p.amount)}</span>}
    />
  ))
}

function SummaryLine({ label, value, strong, last }) {
  return (
    <div
      className={
        'flex items-center justify-between py-2 ' + (last ? '' : 'border-b border-slate-100')
      }
    >
      <span className="text-[14px] text-slate-500">{label}</span>
      <span className={'text-[15px] ' + (strong ? 'font-extrabold' : 'font-semibold')}>{value}</span>
    </div>
  )
}
