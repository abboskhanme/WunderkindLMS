/**
 * MOLIYA → KASSA KUNI.
 *
 * Kassir va direktor har kuni ochadigan yagona ekran: "hozir kassada qancha
 * pul bor va bugun nima bo'ldi". Shu paytgacha bu savolga javob FOYDA-ZARAR
 * hisobotidan qidirilardi — u esa boshqa savolga javob beradi (daromad
 * qachon TAN OLINGAN, pul qachon KELGAN emas).
 *
 * Manba: `GET /api/admin/finance/cash-day` va `.../cash-day/calendar`
 * (`SchoolLms.Server/Controllers/CashDayController.cs`).
 *
 * PULNI FRONTEND HISOBLAMAYDI. Bu faylda birorta `reduce`, `+` yoki `-` yo'q:
 * ochilish, kirim, chiqim, yopilish, turlar va toifalar kesimi, avans va
 * ochiq smenadagi kutilgan naqd — hammasi serverdan tayyor keladi
 * (`BillingDtos.cs` 2-qoidasi, `financeReports.ts` sarlavhasi).
 *
 * STORNO YASHIRILMAYDI. Qaytarilgan to'lov o'z KUNIDA chiqim bo'lib turadi
 * va jadvalda ajratib ko'rsatiladi — SPEC §4.1: xato o'chirilmaydi, ustiga
 * qarshi yozuv qo'yiladi va ikkalasi ham ko'rinib turadi.
 *
 * RUXSAT (SPEC §4.3): faqat `admin` va `superadmin`. Ekran "kassa kuni" deb
 * atalsa ham kassir uni ko'rmaydi — panel butun maktabning kunini ochib
 * beradi (hamma kassir, bank, chiqimlar, maosh). Kassirning o'z kuni unga
 * "Mening smenam" ekranida ochiq.
 */
import { useCallback, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  ArrowDownRight,
  ArrowUpRight,
  Banknote,
  CreditCard,
  Landmark,
  FileBarChart,
  Receipt,
  RefreshCw,
  Undo2,
  Wallet,
} from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  getCashDay,
  getCashMonth,
  type CashDay,
  type CashDayAccount,
  type CashDayMovement,
  type CashMonth,
} from '@/api/services/cashDay'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { CashDayCalendar } from './CashDayCalendar'
import { ReportState } from './ReportState'
import {
  accountLabel,
  formatDateTime,
  formatSignedMoney,
  paymentMethodLabel,
  signClass,
} from './reportLabels'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq. */
const ALLOWED_ROLES = ['admin', 'superadmin']

const todayStr = new Date().toISOString().slice(0, 10)

export function CashDayPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const allowed = user !== null && ALLOWED_ROLES.includes(user.role)

  const [date, setDate] = useState(todayStr)
  const [month, setMonth] = useState(todayStr.slice(0, 7))

  const day = useAsync<CashDay | null>(
    () => (allowed ? getCashDay(date) : Promise.resolve(null)),
    [allowed, date],
  )
  const calendar = useAsync<CashMonth | null>(
    () => (allowed ? getCashMonth(month) : Promise.resolve(null)),
    [allowed, month],
  )

  /**
   * Kun tanlanadigan YAGONA yo'l — sana maydoni, "Bugun" tugmasi va kalendar
   * katakchasi ham shuni chaqiradi. Oy ham birga ko'chadi, aks holda
   * kalendar tanlangan kundan boshqa oyni ko'rsatib turardi.
   */
  const selectDate = useCallback((next: string) => {
    setDate(next)
    setMonth(next.slice(0, 7))
  }, [])

  /**
   * Kalendar o'qlari FAQAT oyni ko'chiradi — tanlangan kun joyida qoladi.
   * "Oldingi oyga qarayman" va "kun tanlayman" ikki xil harakat.
   */
  const shiftMonth = useCallback((delta: number) => {
    setMonth((current) => {
      const [y, m] = current.split('-').map(Number)
      const moved = new Date(Date.UTC(y, m - 1 + delta, 1))
      return `${moved.getUTCFullYear()}-${String(moved.getUTCMonth() + 1).padStart(2, '0')}`
    })
  }, [])

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  const data = day.data

  return (
    <div className="space-y-5">
      {/* --- Sarlavha va kun tanlagich --- */}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Kassa kuni</h1>
          <p className="mt-0.5 text-sm text-slate-500">
            {formatDate(date)} — kassa va bank hisobining kunlik harakati. Manba: buxgalteriya
            jurnali (ledger).
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <input
            type="date"
            value={date}
            onChange={(e) => selectDate(e.target.value)}
            aria-label="Kun"
            className={control}
          />
          <Button variant="ghost" onClick={() => selectDate(todayStr)} disabled={date === todayStr}>
            Bugun
          </Button>
          {/*
            §2.8 F8.01 — kundan hisobotga o'tish. EduSchool'da bu "jurnal"
            tabi; bizda kunning harakatlari shu sahifaning o'zida turibdi,
            shuning uchun havola DAVR hisobotiga olib boradi: o'sha kun
            tanlangan holda toifalar, usullar va grafik ochiladi.
          */}
          <Button
            variant="secondary"
            onClick={() => navigate(`/admin/finance/reports?from=${date}&to=${date}`)}
            title="Shu kunni davr hisobotida ochish"
          >
            <FileBarChart className="h-4 w-4" />
            Hisobotda ochish
          </Button>
          <Button
            variant="secondary"
            onClick={() => {
              day.refetch()
              calendar.refetch()
            }}
            disabled={day.loading}
          >
            <RefreshCw className="h-4 w-4" />
            Yangilash
          </Button>
        </div>
      </div>

      {/* --- Hozir kassada kim turibdi --- */}
      {data && <OpenShifts day={data} />}

      <ReportState loading={day.loading} error={day.error} onRetry={day.refetch}>
        {data && (
          <div className="space-y-5">
            {/* --- Kunning to'rt raqami --- */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard
                label="Kun boshidagi qoldiq"
                value={formatMoney(data.total.opening)}
                icon={Wallet}
                iconBg="bg-slate-100"
                iconColor="text-slate-500"
                hint="Naqd + bank, shu kungacha bo'lgan hamma yozuvdan"
              />
              <StatCard
                label="Kirim"
                value={formatMoney(data.total.inflow)}
                icon={ArrowUpRight}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
                hint="To'lovlar"
              />
              <StatCard
                label="Chiqim"
                value={formatMoney(data.total.outflow)}
                icon={ArrowDownRight}
                iconBg="bg-red-50"
                iconColor="text-red-600"
                hint="Chiqimlar va storno"
              />
              <StatCard
                label="Kun oxiridagi qoldiq"
                value={formatMoney(data.total.closing)}
                icon={Banknote}
                iconBg="bg-brand-50"
                iconColor="text-brand-600"
                hint={`Sof: ${formatSignedMoney(data.total.net)}`}
              />
            </div>

            {/* --- Naqd va bank alohida --- */}
            <AccountsCard accounts={data.accounts} total={data.total} />

            {/* --- Kalendar: kun tanlash --- */}
            <CashDayCalendar
              month={calendar.data}
              loading={calendar.loading}
              selected={date}
              today={todayStr}
              onSelect={selectDate}
              onShift={shiftMonth}
            />

            {/* --- Turlar, to'lov usullari va toifalar --- */}
            <div className="grid gap-4 lg:grid-cols-3">
              <TypesCard day={data} />
              <MethodsCard day={data} />
              <CategoriesCard day={data} />
            </div>

            {/* --- Kunning eng yiriklari --- */}
            <TopFiveCard day={data} />

            {/* --- Kunning hamma harakati --- */}
            <MovementsCard day={data} />
          </div>
        )}
      </ReportState>
    </div>
  )
}

/* ------------------------------------------------------------------ */

/**
 * Hozir ochiq smenalar. "Kutilgan naqd" — smena boshidagi qoldiq + shu
 * smenaning naqd tushumi; karta va o'tkazma SANALMAYDI, chunki ular bankka
 * tushadi (SPEC §8.1 Q13). Raqamni server beradi.
 */
function OpenShifts({ day }: { day: CashDay }) {
  if (day.openShifts.length === 0) {
    return (
      <Card className="border-slate-200 bg-slate-50/70">
        <p className="text-sm text-slate-500">
          Hozir ochiq smena yo'q — kassa yopiq. To'lov qabul qilish uchun kassir smena ochishi
          kerak (SPEC §4.2).
        </p>
      </Card>
    )
  }

  return (
    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      {day.openShifts.map((shift) => (
        <Card key={shift.shiftId} className="border-emerald-200 bg-emerald-50/50">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate font-medium text-slate-800">{shift.cashierName}</p>
              <p className="mt-0.5 text-xs text-slate-500">
                {formatDateTime(shift.openedAt)} dan beri kassada · {shift.paymentsCount} ta to'lov
              </p>
            </div>
            <span className="shrink-0 rounded-md bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
              Ochiq
            </span>
          </div>

          <div className="mt-3 grid grid-cols-3 gap-2">
            <Figure label="Ochilish" value={formatMoney(shift.openingFloat)} />
            <Figure label="Naqd tushum" value={formatMoney(shift.cashSoFar)} />
            <Figure
              label="Javonda bo'lishi kerak"
              value={formatMoney(shift.expectedCashSoFar)}
              valueClass="text-emerald-700"
            />
          </div>

          <p className="mt-2 text-xs text-slate-400">
            Naqdsiz (karta / o'tkazma / onlayn): {formatMoney(shift.nonCashSoFar)} — bankka tushadi,
            kassada sanalmaydi.
          </p>
        </Card>
      ))}
    </div>
  )
}

/** Naqd va bank alohida: ochilish → kirim → chiqim → yopilish. */
function AccountsCard({ accounts, total }: { accounts: CashDayAccount[]; total: CashDayAccount }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Hisoblar kesimida</h2>
        <p className="text-sm text-slate-400">
          Naqd — kassada sanaladi, bank — karta, o'tkazma va onlayn to'lovlar
        </p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-4 py-3">Hisob</th>
              <th className="px-4 py-3 text-right">Kun boshi</th>
              <th className="px-4 py-3 text-right">Kirim</th>
              <th className="px-4 py-3 text-right">Chiqim</th>
              <th className="px-4 py-3 text-right">Sof</th>
              <th className="px-4 py-3 text-right">Kun oxiri</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {accounts.map((a) => (
              <tr key={a.account} className="hover:bg-slate-50/60">
                <td className="flex items-center gap-2 px-4 py-3 font-medium text-slate-700">
                  {a.account === 'cash' ? (
                    <Banknote className="h-4 w-4 text-emerald-600" />
                  ) : (
                    <Landmark className="h-4 w-4 text-slate-400" />
                  )}
                  {accountLabel(a.account)}
                </td>
                <td className="px-4 py-3 text-right text-slate-500">{formatMoney(a.opening)}</td>
                <td className="px-4 py-3 text-right text-emerald-600">{formatMoney(a.inflow)}</td>
                <td className="px-4 py-3 text-right text-red-600">{formatMoney(a.outflow)}</td>
                <td className={cn('px-4 py-3 text-right font-medium', signClass(a.net))}>
                  {formatSignedMoney(a.net)}
                </td>
                <td className="px-4 py-3 text-right font-semibold text-slate-800">
                  {formatMoney(a.closing)}
                </td>
              </tr>
            ))}
            <tr className="bg-slate-50/70">
              <td className="px-4 py-3 font-semibold text-slate-800">Jami</td>
              <td className="px-4 py-3 text-right text-slate-600">{formatMoney(total.opening)}</td>
              <td className="px-4 py-3 text-right text-emerald-700">{formatMoney(total.inflow)}</td>
              <td className="px-4 py-3 text-right text-red-700">{formatMoney(total.outflow)}</td>
              <td className={cn('px-4 py-3 text-right font-semibold', signClass(total.net))}>
                {formatSignedMoney(total.net)}
              </td>
              <td className="px-4 py-3 text-right font-semibold text-slate-800">
                {formatMoney(total.closing)}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </Card>
  )
}

/**
 * Turlar kesimi. "Tur" — hisoblar rejasidagi qarshi hisob (to'lov →
 * o'quvchi to'lovlari, chiqim → maosh / kommunal / ijara …). Storno ALOHIDA
 * qator bo'lib turadi va asl turga qo'shib yuborilmaydi.
 */
function TypesCard({ day }: { day: CashDay }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Turlar bo'yicha</h2>
        <p className="text-sm text-slate-400">Storno alohida qatorda — netlanmaydi</p>
      </div>

      {day.byType.length === 0 ? (
        <p className="p-6 text-center text-sm text-slate-400">Bu kunda pul harakati yo'q.</p>
      ) : (
        <table className="w-full text-left text-sm">
          <tbody className="divide-y divide-slate-100">
            {day.byType.map((row) => (
              <tr key={row.key} className={cn(row.isReversal && 'bg-amber-50/60')}>
                <td className="px-4 py-2.5 text-slate-600">
                  {row.label}
                  {row.isReversal && <Undo2 className="ml-1.5 inline h-3.5 w-3.5 text-amber-600" />}
                </td>
                <td className="px-4 py-2.5 text-right text-slate-400">{row.count} ta</td>
                <td className={cn('px-4 py-2.5 text-right font-medium', signClass(row.amount))}>
                  {formatSignedMoney(row.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  )
}

/**
 * To'lov usullari kesimi (§2.8 F8.02) — "pul qanday keldi".
 *
 * FAQAT to'lovlar: chiqimda usul saqlanmaydi (`expenses` da bunday ustun
 * yo'q), shuning uchun bu yerdagi chiqim — shu kuni STORNO qilingan qism.
 * Chiqimning usul kesimini o'ylab topish yolg'on javob bo'lardi; chiqim
 * "Turlar bo'yicha" da hisob kesimida turibdi.
 */
function MethodsCard({ day }: { day: CashDay }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">To'lov usullari</h2>
        <p className="text-sm text-slate-400">Faqat to'lovlar — chiqimda usul saqlanmaydi</p>
      </div>

      {day.byMethod.length === 0 ? (
        <p className="p-6 text-center text-sm text-slate-400">Bu kunda to'lov bo'lmagan.</p>
      ) : (
        <table className="w-full text-left text-sm">
          <tbody className="divide-y divide-slate-100">
            {day.byMethod.map((row) => (
              <tr key={row.method}>
                <td className="px-4 py-2.5 text-slate-600">
                  {row.label}
                  {row.outflow !== 0 && (
                    <span className="ml-1.5 text-xs text-amber-600">
                      (storno {formatMoney(row.outflow)})
                    </span>
                  )}
                </td>
                <td className="px-4 py-2.5 text-right text-slate-400">{row.count} ta</td>
                <td className={cn('px-4 py-2.5 text-right font-medium', signClass(row.amount))}>
                  {formatSignedMoney(row.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  )
}

/**
 * Toifalar kesimi — "nimaga to'landi". Taqsimlanmagan qism alohida qatorda:
 * u AVANS (pul keldi, hali hech qaysi hisob-fakturaga biriktirilmadi), uni
 * yashirish ekranda qo'shilmaydigan ikki raqam qoldirardi.
 */
function CategoriesCard({ day }: { day: CashDay }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Toifalar bo'yicha</h2>
        <p className="text-sm text-slate-400">
          Nimaga to'landi. Storno o'z kunida minus bilan ko'rinadi
        </p>
      </div>

      {day.byCategory.length === 0 && day.unallocatedTotal === 0 ? (
        <p className="p-6 text-center text-sm text-slate-400">Bu kunda taqsimlangan to'lov yo'q.</p>
      ) : (
        <table className="w-full text-left text-sm">
          <tbody className="divide-y divide-slate-100">
            {day.byCategory.map((row) => (
              <tr key={row.categoryId}>
                <td className="px-4 py-2.5 text-slate-600">{row.categoryName}</td>
                <td className={cn('px-4 py-2.5 text-right font-medium', signClass(row.amount))}>
                  {formatSignedMoney(row.amount)}
                </td>
              </tr>
            ))}

            {day.unallocatedTotal !== 0 && (
              <tr className="bg-slate-50/70">
                <td className="px-4 py-2.5 text-slate-500">
                  Taqsimlanmagan (avans)
                  <span className="ml-1.5 text-xs text-slate-400">
                    hali hisob-fakturaga biriktirilmagan
                  </span>
                </td>
                <td className={cn('px-4 py-2.5 text-right font-medium', signClass(day.unallocatedTotal))}>
                  {formatSignedMoney(day.unallocatedTotal)}
                </td>
              </tr>
            )}

            <tr className="bg-slate-50/70">
              <td className="px-4 py-2.5 font-semibold text-slate-800">Taqsimlangan jami</td>
              <td className="px-4 py-2.5 text-right font-semibold text-slate-800">
                {formatMoney(day.allocatedTotal)}
              </td>
            </tr>
          </tbody>
        </table>
      )}
    </Card>
  )
}

/** Kunning eng yirik beshta harakati — MODUL bo'yicha, ya'ni katta chiqim ham tushadi. */
function TopFiveCard({ day }: { day: CashDay }) {
  if (day.topFive.length === 0) return null

  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Kunning eng yiriklari</h2>
        <p className="text-sm text-slate-400">Summasi bo'yicha, kirim va chiqim birgalikda</p>
      </div>

      <ol className="divide-y divide-slate-100">
        {day.topFive.map((m, index) => (
          <li key={m.entryId} className="flex items-center gap-3 px-4 py-3">
            <span className="w-5 shrink-0 text-sm font-semibold text-slate-300">{index + 1}</span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-slate-700">{m.title}</p>
              <p className="truncate text-xs text-slate-400">{movementSubtitle(m)}</p>
            </div>
            <span className={cn('shrink-0 text-sm font-semibold', signClass(m.signed))}>
              {formatSignedMoney(m.signed)}
            </span>
          </li>
        ))}
      </ol>
    </Card>
  )
}

/** Kunning hamma harakati — storno ajratib ko'rsatilgan holda. */
function MovementsCard({ day }: { day: CashDay }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Kun harakatlari</h2>
        <p className="text-sm text-slate-400">
          {day.movementsTotal} ta yozuv
          {day.movementsTruncated && ` · ro'yxatda eng yangi ${day.movements.length} tasi`}
          {' · '}storno sariq qatorda
        </p>
      </div>

      {day.movements.length === 0 ? (
        <p className="p-6 text-center text-sm text-slate-400">
          Bu kunda kassa va bank hisobida harakat bo'lmagan.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">Vaqt</th>
                <th className="px-4 py-3">Nima</th>
                <th className="px-4 py-3">Hisob</th>
                <th className="px-4 py-3">Chek</th>
                <th className="px-4 py-3">Kim</th>
                <th className="px-4 py-3 text-right">Summa</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {day.movements.map((m) => (
                <tr key={m.entryId} className={cn(m.isReversal ? 'bg-amber-50/70' : 'hover:bg-slate-50/60')}>
                  <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                    {formatDateTime(m.createdAt)}
                  </td>
                  <td className="px-4 py-3">
                    <p className="font-medium text-slate-800">
                      {m.isReversal && (
                        <Undo2 className="mr-1.5 inline h-3.5 w-3.5 text-amber-600" />
                      )}
                      {m.title}
                    </p>
                    {m.memo && <p className="mt-0.5 text-xs text-slate-400">{m.memo}</p>}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                    <span className="inline-flex items-center gap-1.5">
                      {m.account === 'cash' ? (
                        <Banknote className="h-3.5 w-3.5 text-emerald-600" />
                      ) : (
                        <CreditCard className="h-3.5 w-3.5 text-slate-400" />
                      )}
                      {accountLabel(m.account)}
                    </span>
                    {m.method && (
                      <span className="ml-1.5 text-xs text-slate-400">
                        ({paymentMethodLabel(m.method)})
                      </span>
                    )}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                    {typeof m.receiptNo === 'number' ? (
                      <span className="inline-flex items-center gap-1">
                        <Receipt className="h-3.5 w-3.5 text-slate-300" />№{m.receiptNo}
                      </span>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td className="px-4 py-3 text-slate-500">{m.actorName ?? '—'}</td>
                  <td className={cn('whitespace-nowrap px-4 py-3 text-right font-semibold', signClass(m.signed))}>
                    {formatSignedMoney(m.signed)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

/* ------------------------------------------------------------------ */

/** "Naqd · Chek №41 · Kassir Aziza" ko'rinishidagi ikkinchi qator. */
function movementSubtitle(m: CashDayMovement): string {
  const parts = [accountLabel(m.account)]
  if (m.method) parts.push(paymentMethodLabel(m.method))
  if (typeof m.receiptNo === 'number') parts.push(`Chek №${m.receiptNo}`)
  if (m.actorName) parts.push(m.actorName)
  return parts.join(' · ')
}

function Figure({
  label,
  value,
  valueClass = 'text-slate-800',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-xl bg-white/70 p-2">
      <p className="text-[11px] font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-0.5 text-sm font-semibold', valueClass)}>{value}</p>
    </div>
  )
}

export default CashDayPage
