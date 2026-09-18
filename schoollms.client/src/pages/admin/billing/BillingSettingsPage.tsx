/**
 * MOLIYA SOZLAMALARI — F14.01 (docs/modules/finance-parity.md §2.14).
 *
 * EduSchool'da `FINANCE_SETTINGS` bo'limi bitta katta forma; ozimizniki
 * hozircha faqat uchta qiymatni ochadi — to'lov muddati, muddati o'tgan deb
 * hisoblash kuni va chiqim tasdiq chegarasi. Bu uchtasi allaqachon
 * `billing_settings` jadvalida bor (`SchoolLms.Domain/Billing.cs`,
 * migratsiya YO'Q kerak); ilgari faqat qo'lda `UPDATE SQL` bilan
 * o'zgarardi — ExpenseService, InvoiceService faqat O'QIYDI
 * (docs/PENDING_WIRING.md §E). Bu ekran birinchi YOZUVCHI.
 *
 * RUXSAT (SPEC §4.3): admin va direktor (`FinanceAction.ManageBillingSettings`,
 * server darvozasi — `[FinanceRole(...)]` `BillingCatalogController` da).
 * Qolgan katalog toifalari (transaction type, payment method, currency)
 * §2.0 da DECLINED — bizda yopiq `Accounts.cs` va so'm-yagona.
 *
 * MARSHRUT ULANMAGAN — `CLAUDE.md` qattiq qoidasi: `App.tsx` va
 * `navigation.ts` bu vazifada TAHRIRLANMAYDI. Komponent nomlangan eksport,
 * o'z rolini o'zi tekshiradi (P1-17 uslubi) — keyingi navigatsiya
 * vazifasi uni `/admin/billing/settings` ga ulashi kifoya.
 */
import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, Lock, Settings2 } from 'lucide-react'
import {
  getBillingSettings,
  updateBillingSettings,
  type BillingSettings,
} from '@/api/services/billingCatalog'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Notice } from '@/pages/admin/billing/BillingUi'

const ALLOWED_ROLES = ['admin', 'superadmin']

function formatMoney(v: number): string {
  return v.toLocaleString('ru-RU').replace(/,/g, ' ')
}

export function BillingSettingsPage() {
  const { user } = useAuth()
  const allowed = user !== null && ALLOWED_ROLES.includes(user.role)

  const [settings, setSettings] = useState<BillingSettings | null>(null)
  const [form, setForm] = useState({
    paymentDueDay: 10,
    overdueAfterDay: 15,
    expenseApprovalThreshold: 5_000_000,
  })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // Ruxsati yo'q foydalanuvchi uchun `loading` HECH QACHON ishlatilmaydi —
    // pastdagi render bloki `!allowed` bo'lsa Loader'gacha yetmasdan
    // "yopiq" kartani qaytaradi (`TransactionsPage.tsx` dagi bir xil naqsh).
    if (!allowed) return
    getBillingSettings()
      .then((s) => {
        setSettings(s)
        setForm({
          paymentDueDay: s.paymentDueDay,
          overdueAfterDay: s.overdueAfterDay,
          expenseApprovalThreshold: s.expenseApprovalThreshold,
        })
      })
      .catch((e: unknown) => {
        const message = (e as { response?: { data?: { message?: string } } })?.response?.data
          ?.message
        setError(message ?? "Sozlamalarni yuklab bo'lmadi")
      })
      .finally(() => setLoading(false))
  }, [allowed])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setSaved(false)
    setError(null)
    try {
      const next = await updateBillingSettings(form)
      setSettings(next)
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
    } catch (e: unknown) {
      const message = (e as { response?: { data?: { message?: string } } })?.response?.data
        ?.message
      setError(message ?? "Saqlab bo'lmadi")
    } finally {
      setSaving(false)
    }
  }

  if (!allowed) {
    return (
      <Card className="mx-auto max-w-lg text-center">
        <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-slate-100 text-slate-400">
          <Lock className="h-6 w-6" />
        </div>
        <h2 className="text-base font-semibold text-slate-800">Bu bo'lim sizga yopiq</h2>
        <p className="mt-2 text-sm text-slate-500">
          Moliya sozlamalari — faqat administrator va direktorga ochiq (SPEC §4.3).
        </p>
      </Card>
    )
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div>
        <h1 className="flex items-center gap-2 text-xl font-semibold text-slate-800">
          <Settings2 className="h-5 w-5 text-slate-400" />
          Moliya sozlamalari
        </h1>
        <p className="mt-0.5 text-sm text-slate-500">
          To'lov muddati, qarzdorlik va chiqim tasdig'i chegarasi — butun maktab bo'ylab amal
          qiladi.
        </p>
      </div>

      {error && <Notice>{error}</Notice>}

      <Card>
        <form onSubmit={onSubmit} className="max-w-xl space-y-4">
          <Input
            label="To'lov muddati (oyning shu kuni)"
            type="number"
            min={1}
            max={28}
            required
            value={form.paymentDueDay}
            onChange={(e) => setForm((f) => ({ ...f, paymentDueDay: Number(e.target.value) }))}
          />
          <p className="-mt-3 text-xs text-slate-400">
            Hisob-faktura shu kungacha to'lanishi kutiladi (1–28). Oylik hisoblash shundan
            <code className="mx-1 rounded bg-slate-100 px-1">due_on</code>
            ni chiqaradi.
          </p>

          <Input
            label="Muddati o'tgan deb hisoblash kuni"
            type="number"
            min={form.paymentDueDay}
            max={28}
            required
            value={form.overdueAfterDay}
            onChange={(e) => setForm((f) => ({ ...f, overdueAfterDay: Number(e.target.value) }))}
          />
          <p className="-mt-3 text-xs text-slate-400">
            To'lov muddati kunidan kichik bo'lmasligi kerak (1–28). Qarzdorlar ro'yxati va
            "muddati o'tgan" belgisi shu kundan keyin yoqiladi.
          </p>

          <Input
            label="Chiqim tasdiq chegarasi (so'm)"
            type="number"
            min={0}
            step={1000}
            required
            value={form.expenseApprovalThreshold}
            onChange={(e) =>
              setForm((f) => ({ ...f, expenseApprovalThreshold: Number(e.target.value) }))
            }
          />
          <p className="-mt-3 text-xs text-slate-400">
            Shu summadan katta chiqim direktorning (yaratuvchidan boshqa shaxsning) tasdig'isiz
            jurnalga tushmaydi (SPEC §4.5). Hozir: {formatMoney(form.expenseApprovalThreshold)}{' '}
            so'm.
          </p>

          <div className="flex items-center gap-3 pt-2">
            <Button type="submit" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
            {saved && (
              <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                <Check className="h-4 w-4" /> Saqlandi
              </span>
            )}
          </div>

          {settings?.updatedByName && (
            <p className="pt-1 text-xs text-slate-400">
              Oxirgi o'zgartirish: {settings.updatedByName},{' '}
              {new Date(settings.updatedAt).toLocaleString('uz-UZ')}
            </p>
          )}
        </form>
      </Card>
    </div>
  )
}
