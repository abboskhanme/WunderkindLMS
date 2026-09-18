/**
 * Moliya sozlamalari — endi ikki ustunli KATALOGLAR MARKAZI (2026-09-18).
 *
 * IKKI USTUNLI SHAKL — MIJOZ YUBORGAN EDUSCHOOL EKRANIDAN
 * -----------------------------------------------------------
 * Mijoz EduSchool'ning o'z "Finance settings" ekranini yubordi: chapda
 * kataloglar ro'yxati (tugma, tanlangani belgilangan), o'ngda tanlangan
 * katalogning o'zi. Bu yerda o'sha TUZILMA olindi — ko'rinish EMAS (CLAUDE.md:
 * "bizning vizual tilimiz — iOS/Apple minimalizmi — bizniki bo'lib qoladi").
 * Ranglar, shrift, karta uslubi — hammasi `BillingUi.tsx` va shu papkadagi
 * boshqa sahifalar bilan bir xil.
 *
 * NEGA SAHIFALAR QAYTADAN YOZILMADI: chap ustundagi to'rtta katalog
 * (`CategoriesPage`, `SubscriptionsPage`, `DiscountsPage`, `DebtorStatusesPage`)
 * — MAVJUD sahifalar, shu yerga O'ZGARTIRILMAY import qilib qo'yilgan. Ular
 * o'z marshrutlarida (`/admin/billing/categories` va h.k.) ALOHIDA ham
 * ishlayveradi — bu yerda faqat IKKINCHI kirish nuqtasi qo'shildi, birinchisi
 * yo'qolmadi. Vazifa navigatsiya, funksiya emas: sahifalarning ichki mantig'i,
 * so'rovlari, formalari — bittasi ham qayta yozilmagan.
 *
 * NEGA HAMMASI BIR XIL "№ / Nomi / Amallar" JADVALIGA SOLINMADI: EduSchool
 * ekranida har bir katalog xuddi shu jadval ko'rinishida (nomlangan
 * yozuvlar ro'yxati — qo'shish/tahrirlash/o'chirish). Bizda esa faqat
 * UCHTASI (`To'lov toifalari`, `Qarzdor holatlari`, endi `Tranzaksiya turi`
 * ham — pastga qarang) haqiqatan ham shunday — flat, nomlangan katalog.
 * Qolgan ikkitasi boshqacha:
 *   - `Obunalar` — o'quvchi bo'yicha guruhlangan obuna YOZUVLARI, nomlangan
 *     TUR emas (finance-parity.md §2.14.3: "subscription plans... declined —
 *     a school bills per category per month").
 *   - `Chegirmalar` — chegirma SO'ROVLARI navbati (direktor tasdig'i bilan),
 *     nomlangan chegirma TURLARI emas (`discount_types` hali qurilmagan —
 *     §2.14.3 F14.02, P2).
 * Ularni zo'rlab bitta jadval shakliga tiqish sahifaning haqiqiy vazifasini
 * yashirardi va qayta yozishni talab qilardi — CLAUDE.md "additive, rebuild
 * qilma" qoidasiga zid. Shu sabab o'ng panel — har bir katalog o'zining
 * TABIIY ko'rinishida, faqat CHAP panel bir xil.
 *
 * XILLAR (pill-tab) — FAQAT "TRANZAKSIYA TURI" BO'LIMIDA: EduSchool'ning
 * namunasida faqat shu katalog Kirim/Chiqim/Bonus/Jarima pillarini
 * ko'rsatadi; qolgan besh bo'limning birontasida ham "xil" tushunchasi
 * yo'q, shuning uchun ular flat holicha qoladi — pill faqat o'sha bitta
 * bo'limning O'ZIDA (`TransactionTypesPage.tsx`).
 *
 * "TRANZAKSIYA TURI" ENDI BOR — 2026-09-18, MIJOZ YUBORGAN KASSA KIRIM
 * SHAKLI SABABLI, LEKIN TO'LIQ EDUSCHOOL DARAXTI EMAS
 * -----------------------------------------------------------------------
 * Quyidagi jadvaldagi eski yozuv ("Tranzaksiya turi → yo'q, Accounts.cs
 * yopiq") hamon TO'G'RI — faqat TORROQ doirada: `docs/modules/
 * existing-module-gaps.md` §3.4 "declined" qarori EduSchool'ning to'liq
 * tahrirlanadigan daraxtiga tegishli (`parentId`, `color`, `hasImpactOn`,
 * `isPrePayment`/`isSalary` — bularning BIRORTASI yo'q va bo'lmaydi).
 * Mijoz keyinroq (2026-09-18) kassaning "Kirim" shaklidan aniq skrinshot
 * yubordi: "Tranzaksiya turi *" — majburiy dropdown (Do'ppi uchun, Kitob
 * uchun to'lov, ...). Bu — `Accounts.cs`ga BOG'LANMAGAN, faqat
 * `cash_box_transactions` qatoriga yopishtiriladigan YORLIQ (hisobot hamon
 * akkaunt bo'yicha yig'iladi) — shuning uchun `Accounts.cs` YOPIQ qoladi,
 * lekin yorliq katalogi (`transaction_types`, kind: `in`/`out`) qo'shildi.
 * Bonus/Jarima pillari BU YERDA YO'Q: ular uchun bu katalog ALLAQACHON
 * mavjud (`adjustment_reasons`, F11.02, HR bo'limidagi `AdjustmentReasonsModal.tsx`)
 * — ikkinchi, bog'lanmagan nusxa yaratish o'rniga o'sha joyida qoladi.
 * Batafsil: `TransactionTypesPage.tsx` va `SchoolLms.Domain/TransactionTypes.cs`
 * boshidagi izoh.
 *
 * XARITALASH — ULARNING O'N TASI, BIZNING BESHTAMIZ (finance-parity.md §2.14):
 *   Tranzaksiya turi     → **Tranzaksiya turi** (Kirim/Chiqim, yuqoridagi
 *                          izoh) — Bonus/Jarima QISMI hamon boshqa joyda.
 *                          "To'lov toifalari" ENG YAQIN ikkinchi analog,
 *                          lekin aynan o'rnini bosmaydi.
 *   To'lov usuli         → yo'q, xuddi shu sababdan YOPIQ (`PaymentMethod`).
 *   Abonement            → Obunalar (ustuvor, operatsion shaklda).
 *   Chegirma             → Chegirmalar (navbat shaklida, tur katalogisiz).
 *   Pul birligi          → yo'q — so'm yolg'iz (§2.0: "declined — so'm only").
 *   Coin birligi         → yo'q — gamifikatsiya, bizda mavjud emas.
 *   Tizim obunasi        → yo'q — EduSchool o'zining SaaS to'lovi, bizga
 *                          aloqasi yo'q ("not applicable", §2.0).
 *   Rejali xarajat       → Rejalashtirilgan chiqimlar (F6.01, 2026-09-18 da
 *                          qurildi). ("Chiqimlar" bunga TENG EMAS — u haqiqiy
 *                          chiqim yozuvi, shablon emas, va operatsion ekran —
 *                          pastga qarang.)
 *   Soliq                → yo'q — soliq stavkalari `hr.md` §2.5 da, alohida.
 *   Qarzdorlik holatlari → Qarzdor holatlari (§2.14.1'da AYNAN shu nom bilan).
 *
 * BU YERGA QO'SHILMAGANLAR — OPERATSION EKRANLAR, KATALOG EMAS
 * -----------------------------------------------------------------
 * Mijoz yuborgan EduSchool ekranida ham bularning birontasi yo'q — ular
 * boshqa joyda: kunlik ish jarayoni yoki hisobot, "ma'lumotnoma" emas.
 *   - Umumiy (`/admin/finance`) — direktor paneli, Moliya bo'limining o'zi.
 *     `navigation.ts` AMALLAR guruhida hamon alohida yozuvga ega.
 *   - Kassa kuni (`/admin/finance/cash-day`) — kunlik hisobot; mijozning
 *     o'zi so'rab menyudan OLIB TASHLATGAN (b499848 izohi). Munosib uyi —
 *     Umumiy panelidagi Z-hisobot/Nomuvofiqlik tablari yonida.
 *   - Qaytarimlar (`/admin/finance/refunds`) — ikki qavatli tasdiq
 *     jarayoni; tabiiy uyi — Tranzaksiyalar/o'quvchi balansi yonida.
 *   - Chiqimlar (`/admin/billing/expenses`) — chiqim yozish + tasdiq
 *     navbati, HAQIQIY operatsiya (shablon emas). Tabiiy uyi — Kassa/
 *     Tranzaksiyalar yonida, kunlik ishlatiladigan ekranlar qatorida.
 *   `navigation.ts` ga tegishli o'zgarish shu PR qamrovidan tashqarida
 *   (taqiqlangan fayl) — hisobotda qanday yetib borish yozilgan.
 */
import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ArrowLeftRight, CalendarClock, Check, Layers, Settings, ShieldCheck, Tag, Users } from 'lucide-react'
import type { BillingSettingsInput } from '@/api/services/billingCatalog'
import { getBillingSettings, updateBillingSettings } from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, Notice } from './BillingUi'
import { useBillingAccess } from './access'
import { CategoriesPage } from './CategoriesPage'
import { SubscriptionsPage } from './SubscriptionsPage'
import { DiscountsPage } from './DiscountsPage'
import { DebtorStatusesPage } from '../finance/DebtorStatusesPage'
import { ExpenseTemplatesPage } from './ExpenseTemplatesPage'
import { TransactionTypesPage } from './TransactionTypesPage'

type SectionKey =
  | 'settings'
  | 'categories'
  | 'subscriptions'
  | 'discounts'
  | 'debtor-statuses'
  | 'expense-templates'
  | 'transaction-types'

/**
 * Chap ustun — tartib ataylab shunday: `settings` BIRINCHI, chunki bu
 * sahifaning marshruti (`/admin/billing/settings`) va nomi ("Moliya
 * sozlamalari") aynan shu forma atrofida qurilgan — kataloglar UNGA
 * QO'SHILDI, u kataloglarga emas. Ro'yxatning "ustida" alohida bo'lim
 * qilib chiqarish (masalan sarlavha darajasida) ortiqcha ierarxiya
 * qo'shardi; EduSchool'ning o'zi ham bir xil darajadagi tugmalar qatorini
 * ko'rsatadi — biz shu bir xillikni saqlaymiz, faqat birinchisini forma
 * egallaydi.
 */
const SECTIONS: Array<{ key: SectionKey; label: string; icon: typeof Layers }> = [
  { key: 'settings', label: 'Sozlamalar', icon: Settings },
  { key: 'categories', label: "To'lov toifalari", icon: Layers },
  { key: 'subscriptions', label: 'Obunalar', icon: Users },
  { key: 'discounts', label: 'Chegirmalar', icon: ShieldCheck },
  { key: 'debtor-statuses', label: 'Qarzdor holatlari', icon: Tag },
  { key: 'expense-templates', label: 'Rejalashtirilgan chiqimlar', icon: CalendarClock },
  { key: 'transaction-types', label: 'Tranzaksiya turi', icon: ArrowLeftRight },
]

function isSectionKey(value: string | null): value is SectionKey {
  return !!value && SECTIONS.some((s) => s.key === value)
}

export function BillingSettingsPage() {
  return (
    <BillingGuard>
      <BillingSettingsHub />
    </BillingGuard>
  )
}

/**
 * Ikki ustunli qobiq: chap — tanlov, o'ng — tanlangan bo'limning o'zi.
 * Tanlov `?tab=` orqali URL'da saqlanadi (`FinancialReportsPage.tsx` dagi
 * bilan bir xil naqsh) — havola ulashish yoki orqaga qaytish tanlovni
 * yo'qotmaydi. Yangi marshrut QO'SHILMAGAN: hammasi shu bitta
 * `/admin/billing/settings` ustida, `App.tsx` ga tegilmagan.
 */
function BillingSettingsHub() {
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')
  const active: SectionKey = isSectionKey(requested) ? requested : 'settings'

  const select = (key: SectionKey) => {
    setParams(key === 'settings' ? {} : { tab: key }, { replace: true })
  }

  return (
    <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
      <nav className="flex gap-1.5 overflow-x-auto pb-1 lg:w-56 lg:shrink-0 lg:flex-col lg:overflow-visible lg:pb-0">
        {SECTIONS.map(({ key, label, icon: Icon }) => (
          <button
            key={key}
            type="button"
            onClick={() => select(key)}
            className={cn(
              'flex shrink-0 items-center gap-2.5 rounded-xl px-3 py-2.5 text-left text-sm font-medium transition-colors lg:w-full',
              active === key
                ? 'bg-brand-50 text-brand-700'
                : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
            )}
          >
            <Icon className="h-4 w-4 shrink-0" />
            <span className="whitespace-nowrap">{label}</span>
          </button>
        ))}
      </nav>

      <div className="min-w-0 flex-1">
        {active === 'settings' && <BillingSettingsForm />}
        {active === 'categories' && <CategoriesPage />}
        {active === 'subscriptions' && <SubscriptionsPage />}
        {active === 'discounts' && <DiscountsPage />}
        {active === 'debtor-statuses' && <DebtorStatusesPage />}
        {active === 'expense-templates' && <ExpenseTemplatesPage />}
        {active === 'transaction-types' && <TransactionTypesPage />}
      </div>
    </div>
  )
}

function BillingSettingsForm() {
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
