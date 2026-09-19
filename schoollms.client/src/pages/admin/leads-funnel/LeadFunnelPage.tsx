/**
 * Buyurtmalar voronkasi (docs/modules/existing-module-gaps.md §4, #9).
 *
 * ALOHIDA SAHIFA — lidlar doskasining ichidagi tab emas va uning fayllariga
 * (`pages/admin/leads/*`) umuman tegmaydi: doska dizayni mijoz talabi bilan
 * muzlatilgan (CLAUDE.md, "PROTECTED DESIGN: the Leads board").
 *
 * Hech bir foiz bu yerda hisoblanmaydi — barcha yig'indi va konversiya serverdan
 * keladi (`GET /api/admin/leads/funnel`).
 */
import { useMemo, useState } from 'react'
import { Filter, Target, TrendingDown, Users, XCircle } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getLeadFunnel } from '@/api/services/leadFunnel'
import { getStages } from '@/api/services/stages'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { cn } from '@/lib/utils'

/** Ustun rangi → progress chizig'i uchun sinf. Tailwind dinamik sinf yasay olmaydi. */
const BAR: Record<string, string> = {
  slate: 'bg-slate-400',
  blue: 'bg-blue-500',
  emerald: 'bg-emerald-500',
  amber: 'bg-amber-500',
  violet: 'bg-violet-500',
  rose: 'bg-rose-500',
  cyan: 'bg-cyan-500',
  orange: 'bg-orange-500',
}

/** Yo'qotish ustunlari tanlovi brauzerda eslab qolinadi — bu sozlama emas, shaxsiy qulaylik. */
const STORAGE_KEY = 'wklms.leadFunnel.lostStages'

function readLost(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as string[]) : []
  } catch {
    return []
  }
}

export function LeadFunnelPage() {
  const [lost, setLost] = useState<string[]>(readLost)
  const [pickerOpen, setPickerOpen] = useState(false)

  const lostKey = lost.join(',')
  const stagesQ = useAsync(() => getStages(), [])
  const funnelQ = useAsync(() => getLeadFunnel(lost), [lostKey])

  const funnel = funnelQ.data
  const maxReached = useMemo(
    () => Math.max(1, ...(funnel?.stages.map((s) => s.reachedCount) ?? [1])),
    [funnel],
  )

  const toggleLost = (stageId: string) => {
    const next = lost.includes(stageId) ? lost.filter((id) => id !== stageId) : [...lost, stageId]
    setLost(next)
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    } catch {
      // Brauzer xotirasi yopiq bo'lsa — tanlov shu sessiyada ishlaydi, xolos.
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Buyurtmalar voronkasi</h1>
          <p className="text-sm text-slate-400">
            Lidlar bosqichma-bosqich qanday siljiyapti va qayerda to'xtab qolyapti
          </p>
        </div>
        <button
          type="button"
          onClick={() => setPickerOpen((v) => !v)}
          className={cn(
            'inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition',
            pickerOpen
              ? 'border-brand-300 bg-brand-50 text-brand-700'
              : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
          )}
        >
          <Filter className="h-4 w-4" />
          Yo'qotish ustunlari
          {lost.length > 0 && (
            <span className="rounded bg-rose-100 px-1.5 text-xs font-medium text-rose-700">
              {lost.length}
            </span>
          )}
        </button>
      </div>

      {pickerOpen && (
        <Card className="space-y-3">
          <p className="text-sm text-slate-600">
            Qaysi ustunlar <b>yo'qotilgan</b> lid degani? Belgilangan ustunlar voronkadan
            chiqariladi va pastda "yo'qotish sabablari" bo'lib ko'rinadi.
          </p>
          <div className="flex flex-wrap gap-2">
            {(stagesQ.data ?? []).map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => toggleLost(s.id)}
                className={cn(
                  'rounded-full border px-3 py-1.5 text-sm transition',
                  lost.includes(s.id)
                    ? 'border-rose-200 bg-rose-50 text-rose-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                )}
              >
                {s.title}
              </button>
            ))}
          </div>
          <p className="text-xs text-slate-400">
            Tanlov faqat shu brauzerda saqlanadi — bazada "yo'qotildi" degan maydon yo'q.
          </p>
        </Card>
      )}

      <Card className="border-amber-200/70 bg-amber-50/60">
        <p className="text-sm text-amber-900">
          <b>Bu — hozirgi holat surati.</b> Lidda na yaratilgan vaqt, na bosqich o'zgarishi
          tarixi saqlanadi, shuning uchun "shu oyda nechta lid kirdi" va "bosqichda o'rtacha
          necha kun turdi" degan raqamlarni chiqarib bo'lmaydi. "Yetib kelgan" soni lid faqat
          oldinga siljiydi degan taxminga tayanadi.
        </p>
      </Card>

      {funnelQ.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : funnelQ.error ? (
        <Card>
          <p className="text-sm text-rose-600">{funnelQ.error}</p>
        </Card>
      ) : (
        funnel && (
          <>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <StatCard
                label="Jami lidlar"
                value={funnel.totalLeads}
                icon={Users}
                hint={
                  funnel.orphanCount > 0
                    ? `${funnel.orphanCount} ta lid ustunsiz qolgan`
                    : 'Barchasi ustunga biriktirilgan'
                }
              />
              <StatCard
                label="Voronkada"
                value={funnel.funnelLeads}
                icon={Target}
                iconBg="bg-blue-50"
                iconColor="text-blue-600"
                hint={`${funnel.stages.length} ta bosqich`}
              />
              <StatCard
                label="Yo'qotilgan"
                value={funnel.lostCount}
                icon={XCircle}
                iconBg="bg-rose-50"
                iconColor="text-rose-600"
                hint={lost.length === 0 ? 'Ustunlar belgilanmagan' : `${lost.length} ta ustun`}
              />
              <StatCard
                label="Umumiy konversiya"
                value={
                  funnel.overallConversionPercent === null
                    ? '—'
                    : `${funnel.overallConversionPercent}%`
                }
                icon={TrendingDown}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
                hint="Birinchi bosqichdan oxirgisigacha"
              />
            </div>

            <Card className="space-y-5">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">
                Bosqichlar
              </h2>
              {funnel.stages.length === 0 ? (
                <p className="text-sm text-slate-400">
                  Bosqich yo'q — lidlar doskasida ustun qo'shing.
                </p>
              ) : (
                funnel.stages.map((s) => (
                  <div key={s.stageId} className="space-y-1.5">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <span className="text-sm font-medium text-slate-800">{s.title}</span>
                      <span className="text-sm text-slate-500">
                        {s.reachedCount} ta yetib kelgan
                        <span className="text-slate-300"> · </span>
                        {s.currentCount} ta shu yerda
                      </span>
                    </div>
                    <div className="h-2.5 w-full overflow-hidden rounded-full bg-slate-100">
                      <div
                        className={cn('h-full rounded-full', BAR[s.color] ?? BAR.slate)}
                        style={{ width: `${(s.reachedCount / maxReached) * 100}%` }}
                      />
                    </div>
                    <p className="text-xs text-slate-400">
                      {s.stepConversionPercent === null ? (
                        'Voronkaning oxiri'
                      ) : (
                        <>
                          Keyingi bosqichga: <b className="text-slate-600">{s.movedOnCount} ta</b>{' '}
                          ({s.stepConversionPercent}%)
                          <span className="text-slate-300"> · </span>
                          shu bosqichda to'xtadi: {s.dropOffPercent}%
                        </>
                      )}
                    </p>
                  </div>
                ))
              )}
            </Card>

            {funnel.losses.length > 0 && (
              <Card className="p-0">
                <h2 className="px-5 pt-5 text-sm font-semibold uppercase tracking-wide text-slate-400">
                  Yo'qotish sabablari
                </h2>
                <p className="px-5 pb-3 pt-1 text-xs text-slate-400">
                  Sabab — ustun nomining o'zi: bazada alohida "yo'qotish sababi" maydoni yo'q.
                </p>
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-5 py-3">Sabab (ustun)</th>
                      <th className="px-5 py-3 text-center">Lid</th>
                      <th className="px-5 py-3 text-center">Ulush</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {funnel.losses.map((l) => (
                      <tr key={l.stageId}>
                        <td className="px-5 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={l.title}>
                        {l.title}
                      </span>
                    </td>
                        <td className="px-5 py-3 text-center text-slate-600">{l.count}</td>
                        <td className="px-5 py-3 text-center text-slate-500">{l.sharePercent}%</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </Card>
            )}

            {funnel.grades.length > 0 && (
              <Card className="p-0">
                <h2 className="px-5 pt-5 text-sm font-semibold uppercase tracking-wide text-slate-400">
                  Sinflar kesimi
                </h2>
                <p className="px-5 pb-3 pt-1 text-xs text-slate-400">
                  Lidda "qayerdan bildi" (manba) maydoni yo'q, shuning uchun kesim maqsadli
                  sinf bo'yicha.
                </p>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-5 py-3">Sinf</th>
                        <th className="px-5 py-3 text-center">Jami</th>
                        <th className="px-5 py-3 text-center">Voronkada</th>
                        <th className="px-5 py-3 text-center">Oxirgi bosqich</th>
                        <th className="px-5 py-3 text-center">Yo'qotilgan</th>
                        <th className="px-5 py-3 text-center">Konversiya</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {funnel.grades.map((g) => (
                        <tr key={g.targetGrade}>
                          <td className="px-5 py-3 font-medium text-slate-800">
                            {g.targetGrade}-sinf
                          </td>
                          <td className="px-5 py-3 text-center text-slate-600">{g.total}</td>
                          <td className="px-5 py-3 text-center text-slate-600">
                            {g.inFunnelCount}
                          </td>
                          <td className="px-5 py-3 text-center text-slate-600">
                            {g.reachedFinalCount}
                          </td>
                          <td className="px-5 py-3 text-center text-slate-600">{g.lostCount}</td>
                          <td className="px-5 py-3 text-center text-slate-500">
                            {g.conversionPercent === null ? '—' : `${g.conversionPercent}%`}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Card>
            )}
          </>
        )
      )}
    </div>
  )
}
