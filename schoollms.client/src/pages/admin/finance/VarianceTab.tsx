/**
 * HAL QILINMAGAN NOMUVOFIQLIKLAR (SPEC §4.6) — P1-18.
 *
 * Bu tab bannerning davomi: banner "nechta" deydi, bu yerda "qaysi biri va
 * nima uchun" ko'rinadi.
 *
 * ROSTGO'YLIK QOIDASI. Nomuvofiqlikni SABAB yozib yopish `finance_flags`
 * jadvaliga tegadi, uni esa P1-14 yozadi va u hali ulanmagan. Shu sababli:
 *   · jurnal ISHLASA — "Sabab yozib hal qilish" tugmasi chiqadi va sabab
 *     serverga yoziladi (o'chirish emas, yopish);
 *   · jurnal ULANMAGAN bo'lsa — tugma UMUMAN CHIZILMAYDI. Bosilganda hech
 *     narsa qilmaydigan yoki faqat brauzerda "yopiladigan" tugma bu ekranda
 *     eng xavfli narsa bo'lardi: direktor nomuvofiqlikni hal qildim deb
 *     o'ylaydi, serverda esa hech nima o'zgarmaydi.
 *
 * Smena qatorlari HAR DOIM ko'rinadi — ular `cash_shifts.variance` dan
 * keladi (bazada generated column, tahrirlab bo'lmaydi), ya'ni jurnalsiz
 * ham haqiqiy raqam.
 */
import { useState } from 'react'
import { AlertTriangle, ArrowDownCircle, ArrowUpCircle, Info, ShieldCheck } from 'lucide-react'
import { resolveFinanceFlag, type FinanceFlag } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { formatDateTime, formatSignedMoney } from './reportLabels'
import type { VarianceWatch } from './useVarianceWatch'

/** Sabab shundan qisqa bo'lsa — bu izoh emas, imzo. */
const MIN_REASON = 10

/**
 * Beshinchi tur — buzilgan to'lov va'dasi (§3.5). U bazada SAQLANMAYDI,
 * har so'rovda hisoblanadi, shuning uchun uni "sabab yozib yopib" bo'lmaydi
 * (server 404 beradi). U qarz to'langanda yoki Qarzdorlar tabida yangi
 * amal / yangi va'da yozilganda o'z-o'zidan yo'qoladi.
 */
const BROKEN_PROMISE = 'broken_promise'

/**
 * Nom SERVERDAN keladi (`AnomalyFlagDto.KindLabel`). Bu yerda ikkinchi
 * lug'at saqlanmaydi: avvalgi nusxa allaqachon ayrilib ketgan edi —
 * unda `quick_reversal` yozilgan, server esa `fast_reversal` yuboradi,
 * ya'ni o'sha qator ekranda xom kalit bo'lib chiqardi.
 */
function flagKindLabel(flag: FinanceFlag): string {
  return flag.kindLabel || flag.kind
}

interface Props {
  watch: VarianceWatch
  /** Sabab yozib hal qilish — admin va direktor amali (SPEC §4.3). */
  canResolve: boolean
}

export function VarianceTab({ watch, canResolve }: Props) {
  const [resolving, setResolving] = useState<FinanceFlag | null>(null)

  const shortage = watch.shifts
    .filter((s) => (s.variance ?? 0) < 0)
    .reduce((sum, s) => sum + (s.variance ?? 0), 0)
  const surplus = watch.shifts
    .filter((s) => (s.variance ?? 0) > 0)
    .reduce((sum, s) => sum + (s.variance ?? 0), 0)

  return (
    <ReportState
      loading={watch.loading}
      error={watch.error}
      isEmpty={watch.count === 0}
      emptyTitle="Hal qilinmagan nomuvofiqlik yo'q"
      emptyHint={
        watch.flagsNote ??
        "Yopilgan smenalarning hammasida sanalgan naqd kutilganiga teng chiqqan."
      }
      onRetry={watch.refetch}
    >
      <div className="space-y-6">
        {watch.flagsNote && (
          <Card className="border-amber-200 bg-amber-50/60">
            <div className="flex items-start gap-3">
              <Info className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
              <div className="text-sm text-amber-900">
                <p className="font-medium">Anomaliya jurnali hali ulanmagan</p>
                <p className="mt-0.5 text-amber-800/80">{watch.flagsNote}</p>
                <p className="mt-1 text-amber-800/80">
                  Shu sababli sabab yozib yopish vaqtincha mavjud emas — quyidagi qatorlar
                  faqat ko'rish uchun. Nomuvofiqlik raqami bazada hisoblanadi va uni hech
                  kim o'zgartira olmaydi.
                </p>
              </div>
            </div>
          </Card>
        )}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <StatCard
            label="Hal qilinmagan"
            value={String(watch.count)}
            icon={AlertTriangle}
            iconBg="bg-red-50"
            iconColor="text-red-600"
          />
          <StatCard
            label="Kam chiqqan"
            value={formatMoney(Math.abs(shortage))}
            icon={ArrowDownCircle}
            iconBg="bg-red-50"
            iconColor="text-red-600"
            hint="Sanalgan naqd kutilganidan kam"
          />
          <StatCard
            label="Ortiqcha chiqqan"
            value={formatMoney(surplus)}
            icon={ArrowUpCircle}
            iconBg="bg-amber-50"
            iconColor="text-amber-600"
            hint="Sanalgan naqd kutilganidan ko'p"
          />
        </div>

        {watch.shifts.length > 0 && (
          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Nomuvofiqligi bor smenalar</h2>
              <p className="text-sm text-slate-400">
                Manba — `cash_shifts.variance`: bazada hisoblanadi, tahrirlab bo'lmaydi
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Kassir</th>
                    <th className="px-4 py-3">Yopilgan</th>
                    <th className="px-4 py-3 text-right">Kutilgan</th>
                    <th className="px-4 py-3 text-right">Sanalgan</th>
                    <th className="px-4 py-3 text-right">Farq</th>
                    <th className="px-4 py-3">Yopgan</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {watch.shifts.map((s) => {
                    const variance = s.variance ?? 0
                    return (
                      <tr
                        key={s.id}
                        className={cn(
                          // Farqli qator ALBATTA ajratib ko'rsatiladi: kam
                          // chiqqani qizil, ortiqchasi sariq — ikkovi ham
                          // tekshirishga arziydi, lekin bir xil emas.
                          variance < 0 ? 'bg-red-50/70' : 'bg-amber-50/70',
                        )}
                      >
                        <td className="px-4 py-3 font-medium text-slate-800">{s.cashierName}</td>
                        <td className="px-4 py-3 text-slate-500">
                          {s.closedAt ? formatDateTime(s.closedAt) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right text-slate-600">
                          {typeof s.expectedCash === 'number' ? formatMoney(s.expectedCash) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right text-slate-600">
                          {typeof s.countedCash === 'number' ? formatMoney(s.countedCash) : '—'}
                        </td>
                        <td
                          className={cn(
                            'px-4 py-3 text-right font-semibold',
                            variance < 0 ? 'text-red-700' : 'text-amber-700',
                          )}
                        >
                          {formatSignedMoney(variance)}
                        </td>
                        <td className="px-4 py-3 text-slate-500">{s.closedByName ?? '—'}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </Card>
        )}

        {watch.flagsAvailable && watch.flags.length > 0 && (
          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Anomaliya jurnali</h2>
              <p className="text-sm text-slate-400">
                Tungi skaner topgan belgilar · qator o'chirilmaydi, faqat sabab bilan yopiladi
              </p>
            </div>
            <div className="divide-y divide-slate-100">
              {watch.flags.map((f) => (
                <div key={f.id} className="flex flex-wrap items-center gap-3 p-4">
                  <AlertTriangle className="h-5 w-5 shrink-0 text-red-500" />
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-slate-800">{flagKindLabel(f)}</p>
                    <p className="text-sm text-slate-500">{f.summary}</p>
                    <p className="mt-0.5 text-xs text-slate-400">
                      {formatDateTime(f.detectedAt)}
                      {typeof f.amount === 'number' && ` · ${formatSignedMoney(f.amount)}`}
                    </p>
                  </div>
                  {f.kind === BROKEN_PROMISE ? (
                    <p className="max-w-56 text-xs text-slate-400">
                      Qarzdorlar tabida yangi amal yozing — qarz to'lansa, belgi o'zi yo'qoladi.
                    </p>
                  ) : (
                    canResolve && (
                      <Button variant="secondary" onClick={() => setResolving(f)}>
                        <ShieldCheck className="h-4 w-4" /> Sabab yozib hal qilish
                      </Button>
                    )
                  )}
                </div>
              ))}
            </div>
          </Card>
        )}
      </div>

      {resolving && (
        <ResolveModal
          flag={resolving}
          onClose={() => setResolving(null)}
          onResolved={() => {
            setResolving(null)
            watch.refetch()
          }}
        />
      )}
    </ReportState>
  )
}

/**
 * Sabab yozib yopish oynasi. Sabab MAJBURIY va qisqa bo'lishi mumkin emas —
 * "ok", "tuzatildi" kabi imzolar hisobotni foydasiz qiladi.
 */
function ResolveModal({
  flag,
  onClose,
  onResolved,
}: {
  flag: FinanceFlag
  onClose: () => void
  onResolved: () => void
}) {
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const tooShort = reason.trim().length < MIN_REASON

  const submit = async () => {
    if (tooShort) return
    setSaving(true)
    setError(null)
    try {
      await resolveFinanceFlag(flag.id, reason.trim())
      onResolved()
    } catch (e) {
      setError(e instanceof Error ? e.message : "Saqlab bo'lmadi.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Nomuvofiqlikni hal qilish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={tooShort || saving}>
            {saving ? 'Saqlanmoqda...' : 'Sabab bilan yopish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-xl bg-slate-50 p-3 text-sm">
          <p className="font-medium text-slate-700">{flagKindLabel(flag)}</p>
          <p className="text-slate-500">{flag.summary}</p>
        </div>

        <Textarea
          label="Sabab"
          required
          rows={4}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Nima bo'lgani va qanday tekshirilgani — kamida bitta to'liq jumla"
        />
        <p className={cn('text-xs', tooShort ? 'text-amber-600' : 'text-slate-400')}>
          Kamida {MIN_REASON} ta belgi. Yozilgan sabab o'chirilmaydi va hisobotda qoladi.
        </p>

        {error && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
        )}
      </div>
    </Modal>
  )
}
