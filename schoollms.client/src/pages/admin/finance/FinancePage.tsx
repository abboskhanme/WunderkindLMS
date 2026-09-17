import { useCallback, useEffect, useState } from 'react'
import { Download, History } from 'lucide-react'
import type { Role, SalaryReportRow } from '@/types'
import { getSalaryReport } from '@/api/services/finance'
import { formatMoney, exportToCsv, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { AuditHistoryModal } from '@/components/audit/AuditHistoryModal'
import type { AuditFilters } from '@/api/services/audit'
import { useAuth } from '@/context/auth-context'
import { TeacherSalaryDetailModal } from './TeacherSalaryDetailModal'
import { PnlTab } from './PnlTab'
import { CashFlowTab } from './CashFlowTab'
import { DebtorsTab } from './DebtorsTab'
import { CashDayPage } from './CashDayPage'
import { ZReportTab } from './ZReportTab'
import { VarianceTab } from './VarianceTab'
import { VarianceBanner } from './VarianceBanner'
import { useVarianceWatch } from './useVarianceWatch'

const todayStr = new Date().toISOString().slice(0, 10)
const yearOf = (d: string) => Number(d.slice(0, 4))

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * DIREKTOR MOLIYA PANELI.
 *
 * P1-21 da eski uchta tab olib tashlandi va ular bilan birga "Yangi amal",
 * "Tahrirlash", "O'chirish" va "Oylik to'lovni hisoblash" tugmalari ham:
 *
 *   Umumiy      -> "Foyda va zarar" + "Pul oqimi" (jurnaldan hisoblanadi)
 *   O'quvchilar -> "Qarzdorlar" (invoices + payment_allocations dan)
 *
 * Sabab bitta: eski tablar `finance_transactions` va o'quvchi qatoridagi
 * saqlangan qoldiqdan o'qirdi — ikkalasi ham endi yo'q. Pulni tahrirlash va
 * o'chirish esa aynan mijoz aytgan firibgarlik edi (docs/TASKS.md §1.2);
 * yangi modelda pul yozuvi o'zgarmas va faqat storno bilan tuzatiladi.
 *
 * "O'qituvchilar" (maosh) tabi QOLDI: uning o'rnini bosadigan yangi hisobot
 * yo'q. Manbasi almashdi — server uni endi `expenses` dan hisoblaydi.
 *
 * "KASSA KUNI" TABI — 2026-09-18, MENYU YOZUVSIZ QOLGANDAN KEYIN
 * -----------------------------------------------------------------
 * `navigation.ts` Moliya menyusi EduSchool ro'yxatiga moslashtirilgach
 * (commit 7270989) bu ekran hech qanday menyuda qolmadi — faqat URL orqali
 * ochilardi. EduSchool'da ham alohida menyu yozuvi yo'q: kunlik hisobot
 * shu direktor panelining o'zida, Z-hisobot va Nomuvofiqlik bilan bir
 * oilada (ular ham aynan shu sababdan — smena/kassa nazorati — shu yerda).
 * `CashDayPage` O'ZGARTIRILMAY import qilingan: uning o'z sana/kalendar
 * boshqaruvi bor (bitta kun + oy, `from`/`to` DAVRIDAN farqli), shuning
 * uchun `periodTabs` ga QO'SHILMAGAN — pastdagi umumiy davr tanlagich bu
 * tabda ma'nosiz bo'lardi, sahifaning o'zidagi sana maydoni ishlatiladi.
 */
type Tab = 'teachers' | 'pnl' | 'cashflow' | 'debtors' | 'cashday' | 'zreport' | 'variance'

const reportTabs: { value: Tab; label: string }[] = [
  { value: 'pnl', label: 'Foyda va zarar' },
  { value: 'cashflow', label: 'Pul oqimi' },
  { value: 'debtors', label: 'Qarzdorlar' },
  { value: 'cashday', label: 'Kassa kuni' },
  { value: 'zreport', label: 'Z-hisobot' },
  { value: 'variance', label: 'Nomuvofiqlik' },
]

/** Yuqoridagi sana oralig'i faqat shu tablarda ma'noga ega. */
const periodTabs: string[] = ['teachers', 'pnl', 'cashflow']

/**
 * Yangi hisobotlar FAQAT admin va direktorga ko'rinadi (SPEC §4.3):
 * `/api/admin/finance/*` kassirga ham, `finance` ruxsatli oddiy xodimga ham
 * 403 beradi. Ruxsati yo'q odamga tugma CHIZILMAYDI — 403 ni ekranda
 * ko'rsatish emas.
 */
const reportRoles: Role[] = ['admin', 'superadmin']

/** Qoldiq/qarz summasini belgisiga qarab ranglash */
function balanceClass(v: number): string {
  return v > 0 ? 'text-red-600' : v < 0 ? 'text-emerald-600' : 'text-slate-400'
}

/**
 * `initialTab` — menyudan TO'G'RIDAN-TO'G'RI bitta tabga kirish uchun
 * (`Qarzdorlar bilan ishlash`, `Moliya hisobotlari (P&L)`, `Pul oqimi`
 * EduSchool'da alohida menyu yozuvi, bizda esa shu sahifaning tablari).
 * Ekran ikkiga bo'linmaydi — bitta sahifa, boshlang'ich tabi boshqa.
 * Xuddi shu naqsh `StudentsPage` da `Arxiv o'quvchilar` uchun ishlatilgan.
 */
export function FinancePage({ initialTab }: { initialTab?: Tab } = {}) {
  const { user } = useAuth()
  const canSeeReports = !!user && reportRoles.includes(user.role)

  // Ruxsati yo'q xodim uchun yagona ochiq tab — maosh hisoboti.
  const [tab, setTab] = useState<Tab>(
    canSeeReports ? (initialTab ?? 'pnl') : 'teachers')
  const [from, setFrom] = useState(`${yearOf(todayStr)}-01-01`)
  const [to, setTo] = useState(todayStr)

  const [salaryReport, setSalaryReport] = useState<SalaryReportRow[]>([])
  const [loading, setLoading] = useState(false)

  const [audit, setAudit] = useState<{ filters: AuditFilters; title: string } | null>(null)
  const [detailTeacher, setDetailTeacher] = useState<SalaryReportRow | null>(null)

  // Hal qilinmagan nomuvofiqlik hisoblagichi (SPEC §4.6). Banner ham,
  // "Nomuvofiqlik" tabi ham SHU bitta manbadan o'qiydi — ekranda ikkita
  // har xil raqam paydo bo'lmasligi uchun.
  const variance = useVarianceWatch(canSeeReports)

  const load = useCallback(() => {
    // Boshqa tablar o'z ma'lumotini o'zi oladi.
    if (tab !== 'teachers') return
    setLoading(true)
    getSalaryReport(from, to)
      .then(setSalaryReport)
      .finally(() => setLoading(false))
  }, [from, to, tab])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda ma'lumotni qayta yuklash (maqsadli, useAsync bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const handleExportTeachers = () => {
    exportToCsv(
      'oqituvchilar-maoshi.csv',
      ["O'qituvchi", 'Oylik', 'Hisoblangan', 'Berilgan', 'Qoldiq'],
      salaryReport.map((r) => [
        r.teacherName,
        String(r.salary),
        String(r.expected),
        String(r.totalPaid),
        String(r.remaining),
      ]),
    )
  }

  // Tanlangan davrning kalendar oylari (har o'qituvchining hisoblangan oyi boshlanish oyiga
  // qarab farq qilishi mumkin — bu faqat davr uzunligini ko'rsatadi).
  const periodMonths = (() => {
    const [fy, fm] = from.slice(0, 7).split('-').map(Number)
    const [ty, tm] = to.slice(0, 7).split('-').map(Number)
    return Math.max(1, (ty - fy) * 12 + (tm - fm) + 1)
  })()

  const teacherTotals = {
    expected: salaryReport.reduce((a, r) => a + r.expected, 0),
    paid: salaryReport.reduce((a, r) => a + r.totalPaid, 0),
    remaining: salaryReport.reduce((a, r) => a + Math.max(0, r.remaining), 0),
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Moliya</h1>
          <p className="text-sm text-slate-400">Hisobotlar va o'qituvchilar maoshi</p>
        </div>
        <Button
          variant="secondary"
          onClick={() => setAudit({ filters: {}, title: "Moliya o'zgarishlar tarixi" })}
        >
          <History className="h-4 w-4" /> Tarix
        </Button>
      </div>

      {/*
        HAL QILINMAGAN NOMUVOFIQLIK HISOBLAGICHI (SPEC §4.6).
        Sahifa ochilishi bilan ko'rinadi va YOPIB BO'LMAYDI — bannerda "x"
        tugmasi yo'q, uni yashiradigan holat ham yo'q. Batafsil izoh:
        `VarianceBanner.tsx`.
      */}
      {canSeeReports && (
        <VarianceBanner
          watch={variance}
          onOpen={() => setTab('variance')}
          hideOpenAction={tab === 'variance'}
        />
      )}

      {/* Bo'limlar (tablar) */}
      <div className="flex flex-wrap items-center gap-2">
        <button
          onClick={() => setTab('teachers')}
          className={cn(
            'rounded-lg px-4 py-2 text-sm font-medium transition-colors',
            tab === 'teachers'
              ? 'bg-brand-600 text-white'
              : 'bg-white text-slate-600 hover:bg-slate-100',
          )}
        >
          O'qituvchilar
        </button>

        {/* Hisobotlar — faqat admin va direktor (SPEC §4.3). */}
        {canSeeReports && (
          <>
            <span className="mx-1 hidden h-6 w-px bg-slate-200 sm:block" />
            {reportTabs.map((t) => (
              <button
                key={t.value}
                onClick={() => setTab(t.value)}
                className={cn(
                  'rounded-lg px-4 py-2 text-sm font-medium transition-colors',
                  tab === t.value
                    ? 'bg-brand-600 text-white'
                    : 'bg-white text-slate-600 hover:bg-slate-100',
                )}
              >
                {t.label}
                {t.value === 'variance' && variance.count > 0 && (
                  <span
                    className={cn(
                      'ml-2 rounded-full px-1.5 py-0.5 text-xs font-semibold',
                      tab === t.value ? 'bg-white text-red-700' : 'bg-red-100 text-red-700',
                    )}
                  >
                    {variance.count}
                  </span>
                )}
              </button>
            ))}
          </>
        )}
      </div>

      {/* Davr tanlash (maosh, P&L va pul oqimi uchun) */}
      {periodTabs.includes(tab) && (
        <Card className="flex flex-wrap items-center gap-3 p-4">
          <span className="text-sm font-medium text-slate-600">Davr:</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
          <span className="text-slate-400">—</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={control} />
        </Card>
      )}

      {/* ============ O'QITUVCHILAR (maosh) ============ */}
      {tab === 'teachers' &&
        (loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <>
            {salaryReport.length > 0 && (
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <SummaryCard label="Jami hisoblangan" value={formatMoney(teacherTotals.expected)} />
                <SummaryCard
                  label="Jami berilgan"
                  value={formatMoney(teacherTotals.paid)}
                  valueClass="text-emerald-600"
                />
                <SummaryCard
                  label="Jami qoldiq"
                  value={formatMoney(teacherTotals.remaining)}
                  valueClass="text-red-600"
                />
              </div>
            )}
            <Card className="p-0">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
                <div>
                  <h2 className="font-semibold text-slate-800">O'qituvchilar maoshi</h2>
                  <p className="text-sm text-slate-400">
                    Davr bo'yicha — {periodMonths} oy · batafsil uchun o'qituvchini bosing
                  </p>
                </div>
                <Button variant="secondary" onClick={handleExportTeachers} disabled={salaryReport.length === 0}>
                  <Download className="h-4 w-4" /> CSV
                </Button>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">O'qituvchi</th>
                      <th className="px-4 py-3 text-right">Oylik</th>
                      <th className="px-4 py-3 text-right">Hisoblangan</th>
                      <th className="px-4 py-3 text-right">Berilgan</th>
                      <th className="px-4 py-3 text-right">Qoldiq</th>
                      <th className="px-4 py-3 text-right">Tarix</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {salaryReport.map((r) => (
                      <tr
                        key={r.teacherId}
                        onClick={() => setDetailTeacher(r)}
                        className="cursor-pointer hover:bg-slate-50/60"
                      >
                        <td className="px-4 py-3 font-medium text-brand-700">{r.teacherName}</td>
                        <td className="px-4 py-3 text-right text-slate-600">{formatMoney(r.salary)}</td>
                        <td className="px-4 py-3 text-right text-slate-600">{formatMoney(r.expected)}</td>
                        <td className="px-4 py-3 text-right font-medium text-emerald-600">
                          {formatMoney(r.totalPaid)}
                        </td>
                        <td className={cn('px-4 py-3 text-right font-medium', balanceClass(r.remaining))}>
                          {r.remaining < 0 ? `+${formatMoney(-r.remaining)}` : formatMoney(r.remaining)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <button
                            type="button"
                            title="O'zgarishlar tarixi"
                            onClick={(e) => {
                              e.stopPropagation()
                              setAudit({ filters: { teacherId: r.teacherId }, title: `Tarix — ${r.teacherName}` })
                            }}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <History className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                    {salaryReport.length === 0 && (
                      <tr>
                        <td colSpan={6} className="px-4 py-10 text-center text-slate-400">
                          Ma'lumot yo'q
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          </>
        ))}

      {/* ================= P1-18 — HISOBOTLAR ================= */}
      {canSeeReports && tab === 'pnl' && <PnlTab from={from} to={to} />}
      {canSeeReports && tab === 'cashflow' && <CashFlowTab from={from} to={to} />}
      {canSeeReports && tab === 'debtors' && <DebtorsTab />}
      {canSeeReports && tab === 'cashday' && <CashDayPage />}
      {canSeeReports && tab === 'zreport' && <ZReportTab />}
      {canSeeReports && tab === 'variance' && (
        <VarianceTab watch={variance} canResolve={canSeeReports} />
      )}

      <AuditHistoryModal
        open={!!audit}
        onClose={() => setAudit(null)}
        title={audit?.title}
        filters={audit?.filters ?? {}}
      />

      <TeacherSalaryDetailModal
        teacher={detailTeacher}
        from={from}
        to={to}
        onClose={() => setDetailTeacher(null)}
      />
    </div>
  )
}

function SummaryCard({
  label,
  value,
  valueClass = 'text-slate-800',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <Card className="p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-1 text-lg font-semibold', valueClass)}>{value}</p>
    </Card>
  )
}
