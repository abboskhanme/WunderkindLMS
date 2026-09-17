/**
 * Moliya sozlamalari — "to'lov muddati kuni", "muddati o'tgan deb hisoblash kuni"
 * va ikki qavatli nazorat chegarasi (F14.01, finance-parity.md §2.14).
 *
 * IKKI QAVATLI NAZORAT — CHEGARA MAYDONI ALOHIDA
 * ------------------------------------------------
 * Sahifaning o'zi admin va direktorga ochiq (`BillingGuard`, `canOpen`), lekin
 * `expenseApprovalThreshold` ni faqat DIREKTOR tahrirlaydi (SPEC §4.5,
 * finance-parity.md §2.14.3: "superadmin only for the threshold — it is a
 * dual-control parameter"). Admin buni FAQAT o'qiydi — maydon `disabled` bo'lib
 * ko'rsatiladi, tugma emas, chunki bu qiymatni ko'rish o'zi foydali (admin
 * chiqim yozayotganda chegarani bilishi kerak — `ExpenseFormModal` xuddi shu
 * qiymatni ko'rsatadi). Haqiqiy darvoza SERVERDA
 * (`BillingSettingsService.UpdateAsync`, `threshold_requires_director`) — bu
 * yerdagi `disabled` shunchaki "bosdim — 403 oldim" holatining oldini oladi.
 *
 * OLDINGA QARAB — ORQAGA EMAS
 * ----------------------------
 * Bu forma allaqachon hisoblangan hisob-fakturalarga yoki tasdiq kutayotgan
 * chiqimlarga TEGMAYDI — pastdagi izoh shuni ochiq aytadi (tafsilot:
 * `BillingSettingsService.cs` fayl boshidagi izoh).
 */
import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, ShieldCheck } from 'lucide-react'
import type { BillingSettingsInput } from '@/api/services/billingCatalog'
import { getBillingSettings, updateBillingSettings } from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { formatDate, formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, Notice } from './BillingUi'
import { useBillingAccess } from './access'

export function BillingSettingsPage() {
  return (
    <BillingGuard>
      <BillingSettingsView />
    </BillingGuard>
  )
}

function BillingSettingsView() {
  const { canManageBillingSettings, isDirector } = useBillingAccess()

  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [updatedByName, setUpdatedByName] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [dueDay, setDueDay] = useState(10)
  const [overdueDay, setOverdueDay] = useState(15)
  const [threshold, setThreshold] = useState(0)

  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getBillingSettings()
      .then((s) => {
        setDueDay(s.paymentDueDay)
        setOverdueDay(s.overdueAfterDay)
        setThreshold(s.expenseApprovalThreshold)
        setUpdatedAt(s.updatedAt)
        setUpdatedByName(s.updatedByName ?? null)
        setLoaded(true)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Sozlamalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => load(), [load])

  const dueDayError =
    dueDay < 1 || dueDay > 28 ? "1 dan 28 gacha bo'lishi kerak." : null
  const overdueDayError =
    overdueDay < 1 || overdueDay > 28
      ? "1 dan 28 gacha bo'lishi kerak."
      : overdueDay < dueDay
        ? "To'lov muddati kunidan kichik bo'lishi mumkin emas."
        : null
  const thresholdError = threshold < 0 ? "Manfiy bo'lishi mumkin emas." : null

  const valid = !dueDayError && !overdueDayError && !thresholdError

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!valid || saving) return
    setSaving(true)
    setSaveError(null)
    setSaved(false)
    // Chegara HAR DOIM yuboriladi — o'zgartirilmagan bo'lsa ham. Server buni
    // joriy qiymati bilan solishtiradi va faqat HAQIQIY o'zgarishda direktorlik
    // talab qiladi (`billingCatalog.ts` — `BillingSettingsInput` izohi).
    const input: BillingSettingsInput = {
      paymentDueDay: dueDay,
      overdueAfterDay: overdueDay,
      expenseApprovalThreshold: threshold,
    }
    try {
      const updated = await updateBillingSettings(input)
      setDueDay(updated.paymentDueDay)
      setOverdueDay(updated.overdueAfterDay)
      setThreshold(updated.expenseApprovalThreshold)
      setUpdatedAt(updated.updatedAt)
      setUpdatedByName(updated.updatedByName ?? null)
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } catch (err) {
      setSaveError(billingErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Moliya sozlamalari</h1>
        <p className="text-sm text-slate-400">
          To'lov muddati, qarzdorlik va ikki qavatli nazorat chegarasi — butun maktab bo'ylab amal
          qiladi.
        </p>
      </div>

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={false}
          emptyText=""
          onRetry={load}
        >
          {loaded && (
            <form onSubmit={handleSubmit} className="max-w-2xl space-y-5 p-5">
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div>
                  <Input
                    label="To'lov muddati kuni"
                    type="number"
                    min={1}
                    max={28}
                    required
                    disabled={!canManageBillingSettings}
                    value={dueDay}
                    onChange={(e) => setDueDay(Number(e.target.value))}
                  />
                  <p className="mt-1 text-xs text-slate-400">
                    Har oyning shu kunigacha hisob-faktura to'lanishi kerak (1–28). Yangi qiymat
                    faqat KEYINGI hisoblashga qo'llanadi — eski hisob-fakturalarning to'lov
                    muddati o'zgarmaydi.
                  </p>
                  {dueDayError && <p className="mt-1 text-xs text-red-600">{dueDayError}</p>}
                </div>

                <div>
                  <Input
                    label="Muddati o'tgan deb hisoblash kuni"
                    type="number"
                    min={1}
                    max={28}
                    required
                    disabled={!canManageBillingSettings}
                    value={overdueDay}
                    onChange={(e) => setOverdueDay(Number(e.target.value))}
                  />
                  <p className="mt-1 text-xs text-slate-400">
                    Shu kundan keyin to'lanmagan hisob-faktura qarzdorlar ro'yxatiga tushadi
                    (to'lov muddati kunidan kichik bo'lmasligi kerak).
                  </p>
                  {overdueDayError && (
                    <p className="mt-1 text-xs text-red-600">{overdueDayError}</p>
                  )}
                </div>
              </div>

              <div className="rounded-xl border border-slate-200 p-4">
                <div className="mb-2 flex items-center gap-2">
                  <ShieldCheck className="h-4 w-4 text-brand-600" />
                  <span className="text-sm font-medium text-slate-700">
                    Chiqim tasdiq chegarasi (ikki qavatli nazorat)
                  </span>
                </div>
                <Input
                  type="number"
                  min={0}
                  step={1000}
                  required
                  disabled={!isDirector}
                  value={threshold}
                  onChange={(e) => setThreshold(Number(e.target.value))}
                />
                <p className="mt-1 text-xs text-slate-400">
                  {formatMoney(threshold)} dan yuqori chiqim ikkinchi, boshqa shaxsning
                  tasdig'isiz jurnalga tushmaydi. Tasdiq kutayotgan eski chiqimlarning holati
                  o'zgarmaydi — chegara faqat yangi chiqimga qo'llanadi.{' '}
                  {!isDirector &&
                    "Faqat direktor o'zgartira oladi — bu ikki qavatli nazorat parametri."}
                </p>
                {thresholdError && (
                  <p className="mt-1 text-xs text-red-600">{thresholdError}</p>
                )}
              </div>

              {saveError && <Notice>{saveError}</Notice>}

              {updatedByName && updatedAt && (
                <p className="text-xs text-slate-400">
                  Oxirgi o'zgartirdi: {updatedByName}, {formatDate(updatedAt)}
                </p>
              )}

              {canManageBillingSettings && (
                <div className="flex items-center gap-3">
                  <Button type="submit" disabled={!valid || saving}>
                    {saving ? 'Saqlanmoqda...' : 'Saqlash'}
                  </Button>
                  {saved && (
                    <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                      <Check className="h-4 w-4" /> Saqlandi
                    </span>
                  )}
                </div>
              )}
            </form>
          )}
        </AsyncBlock>
      </Card>
    </div>
  )
}
