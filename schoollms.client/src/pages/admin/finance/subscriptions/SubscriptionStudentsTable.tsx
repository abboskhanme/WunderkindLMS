/**
 * O'quvchilar × abonementlar jadvali — sinf sahifasida ham, karta/abonement ro'yxatida ham bir xil.
 * Toifa ustunlari (o'qish, avtobus, …) ro'yxatda uchraganlaridan, server bergan tartibda.
 */
import { Link } from 'react-router-dom'
import {
  NO_PLAN,
  type SubscriptionPlanColumn,
  type SubscriptionStudentPlan,
  type SubscriptionStudentRow,
} from '@/api/services/subscriptionAnalytics'
import { cn, formatMoney } from '@/lib/utils'
import { classPath, compactMoney, shortDate } from './subscriptionFormat'

interface CategoryColumn {
  code: string
  name: string
}

export function SubscriptionStudentsTable({
  rows,
  plans,
  from,
  to,
  search,
  showClass = false,
}: {
  rows: SubscriptionStudentRow[]
  plans: SubscriptionPlanColumn[]
  from: string
  to: string
  /** Davr query'si — sinf havolalari uchun. */
  search: string
  showClass?: boolean
}) {
  const seen = new Set(rows.flatMap((s) => s.subscriptions.map((x) => x.categoryCode)))
  const categories: CategoryColumn[] = []
  for (const p of plans) {
    if (p.planId === NO_PLAN || !seen.has(p.categoryCode) || categories.some((c) => c.code === p.categoryCode)) continue
    categories.push({ code: p.categoryCode, name: p.categoryName })
  }
  if (!categories.some((c) => c.code === 'tuition')) {
    const tuition = plans.find((p) => p.categoryCode === 'tuition')
    categories.unshift({ code: 'tuition', name: tuition?.categoryName ?? "O'qish" })
  }
  const noPlan = rows.some((r) => r.noPlanBilled !== 0)
  const columns = categories.length + 5 + (showClass ? 1 : 0) + (noPlan ? 1 : 0)

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[48rem] text-sm tabular-nums">
        <thead className="bg-slate-50 text-xs text-slate-400">
          <tr>
            <th className="w-10 px-5 py-2 text-left font-medium">#</th>
            <th className="px-3 py-2 text-left font-medium">F.I.SH</th>
            {showClass && <th className="px-3 py-2 text-left font-medium">Sinf</th>}
            {categories.map((c) => (
              <th key={c.code} className="px-3 py-2 text-left font-medium">
                {c.name}
              </th>
            ))}
            {noPlan && (
              <th className="px-3 py-2 text-right font-medium" title="Shu oylarga faktura bor, lekin uni qamragan abonement yo'q">
                Abonementsiz hisob
              </th>
            )}
            <th className="px-3 py-2 text-right font-medium">Hisoblangan</th>
            <th className="px-3 py-2 text-right font-medium">To'langan</th>
            <th className="px-5 py-2 text-right font-medium">Qarz</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s, i) => (
            <tr key={s.studentId} className="border-t border-slate-100 align-top hover:bg-slate-50/60">
              <td className="px-5 py-2.5 text-slate-400">{i + 1}</td>
              <td className="px-3 py-2.5">
                <Link
                  to={`/admin/students/${s.studentId}`}
                  className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                >
                  {s.fullName}
                </Link>
                {!s.inSchool && <div className="text-xs text-slate-400">maktabdan ketgan</div>}
              </td>
              {showClass && (
                <td className="whitespace-nowrap px-3 py-2.5">
                  <Link to={classPath(s.classId, search)} className="text-slate-600 hover:text-brand-600 hover:underline">
                    {s.className}
                  </Link>
                </td>
              )}
              {categories.map((c) => {
                const subs = s.subscriptions.filter((x) => x.categoryCode === c.code)
                return (
                  <td key={c.code} className="px-3 py-2.5">
                    {subs.length === 0 ? (
                      c.code === 'tuition' && s.inSchool ? (
                        <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">Yo'q</span>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )
                    ) : (
                      <div className="space-y-1.5">
                        {subs.map((x) => (
                          <PlanChip key={x.subscriptionId} sub={x} from={from} to={to} />
                        ))}
                      </div>
                    )}
                  </td>
                )
              })}
              {noPlan && (
                <td
                  className={cn('px-3 py-2.5 text-right', s.noPlanBilled ? 'font-medium text-amber-700' : 'text-slate-300')}
                  title={formatMoney(s.noPlanBilled)}
                >
                  {s.noPlanBilled ? compactMoney(s.noPlanBilled) : '—'}
                </td>
              )}
              <td className="px-3 py-2.5 text-right text-slate-700" title={formatMoney(s.billed)}>
                {s.billed ? compactMoney(s.billed) : '—'}
              </td>
              <td className="px-3 py-2.5 text-right text-emerald-700" title={formatMoney(s.paid)}>
                {s.paid ? compactMoney(s.paid) : '—'}
              </td>
              <td
                className={cn('px-5 py-2.5 text-right', s.debt > 0 ? 'font-semibold text-red-600' : 'text-slate-300')}
                title={formatMoney(s.debt)}
              >
                {s.debt ? compactMoney(s.debt) : '—'}
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={columns} className="px-5 py-10 text-center text-slate-400">
                Mos o'quvchi topilmadi.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

/** Bitta abonement: nomi, oylik narxi va — davrni to'liq qoplamasa — muddati. */
function PlanChip({ sub, from, to }: { sub: SubscriptionStudentPlan; from: string; to: string }) {
  const startsInside = sub.startsOn > from
  const endsInside = sub.endsOn !== null && sub.endsOn < to
  const term =
    startsInside && endsInside
      ? `${shortDate(sub.startsOn)} – ${shortDate(sub.endsOn!)}`
      : startsInside
        ? `${shortDate(sub.startsOn)} dan`
        : endsInside
          ? `${shortDate(sub.endsOn!)} gacha`
          : null
  const title = [
    `${sub.planName} — ${formatMoney(sub.monthlyAmount)} / oy`,
    `Muddat: ${shortDate(sub.startsOn)} – ${sub.endsOn ? shortDate(sub.endsOn) : 'ochiq'}`,
    sub.detail ? `Izoh: ${sub.detail}` : null,
    `Davrda hisoblangan: ${formatMoney(sub.billed)}, to'langan: ${formatMoney(sub.paid)}`,
  ]
    .filter(Boolean)
    .join('\n')

  return (
    <div title={title} className="rounded-lg bg-slate-50 px-2.5 py-1.5 ring-1 ring-slate-100">
      <div className="text-sm font-medium leading-tight text-slate-800">{sub.planName}</div>
      <div className="mt-0.5 flex flex-wrap gap-x-2 text-xs text-slate-500">
        <span>{compactMoney(sub.monthlyAmount)} / oy</span>
        {term && <span className="text-sky-700">{term}</span>}
        {sub.detail && <span className="max-w-[12rem] truncate text-slate-400">{sub.detail}</span>}
      </div>
    </div>
  )
}
