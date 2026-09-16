import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, ChevronLeft, ChevronRight, Download, Info } from 'lucide-react'
import {
  getTurnstileDailyReport,
  type TurnstileDailyReport,
} from '@/api/services/turnstileAnalytics'
import { cn, exportToCsv } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DateInput, PageHead, Tile } from './shared'
import { shortDate, today } from './helpers'

/**
 * Kunlik davomat hisoboti (#13).
 *
 * Bir kun, bir sahifa: har sinf bo'yicha kutilgan, turniketdan o'tgan, jurnalda
 * "bor" belgilangan — va ular ORASIDAGI FARQ.
 *
 * Farq — hisobotning butun mag'zi. Turniket ko'rgan, jurnal esa "yo'q" degan
 * bola yo buzilgan jurnal, yo maktabdan chiqib ketgan bola. Shuning uchun
 * sahifada raqamdan tashqari NOMLAR ham bor: jamlanma farq nol bo'lib, ichida
 * ikkita qarama-qarshi xato turgan bo'lishi mumkin.
 *
 * "Tekshirilmagan" (jurnalda umuman belgilanmagan) hech qachon "bor" ga
 * qo'shilmaydi — Bosh sahifadagi davomat blokidagi qoidaning aynan o'zi.
 */

const shiftDate = (iso: string, days: number) => {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const journalStatusLabel: Record<string, string> = {
  present: 'Jurnalda bor',
  absent: 'Jurnalda yo\'q',
  unchecked: 'Belgilanmagan',
}

export function DailyAttendanceReportPage() {
  const [date, setDate] = useState(today())
  const [report, setReport] = useState<TurnstileDailyReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getTurnstileDailyReport(date)
      .then(setReport)
      .catch((e) => setError(e?.response?.data?.message ?? "Hisobotni yuklab bo'lmadi"))
      .finally(() => setLoading(false))
  }, [date])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sana o'zgarganda hisobotni qayta yuklash (maqsadli, loyihadagi mavjud naqsh)
  useEffect(load, [load])

  const exportClasses = () =>
    exportToCsv(
      `kunlik-davomat-${date}.csv`,
      ['Sinf', 'Kutilgan', 'Turniket', 'Jurnalda bor', 'Jurnalda yo\'q', 'Belgilanmagan', 'Farq'],
      (report?.classes ?? []).map((c) => [
        c.className,
        String(c.expected),
        String(c.turnstileEntered),
        String(c.journalPresent),
        String(c.journalAbsent),
        String(c.unchecked),
        String(c.gap),
      ]),
    )

  const mismatchCount = report?.mismatchTotal ?? 0

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <PageHead
          title="Kunlik davomat hisoboti"
          hint="Turniket nima ko'rdi, jurnal nima dedi — va ular orasidagi farq"
        />
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => setDate((d) => shiftDate(d, -1))}>
            <ChevronLeft className="h-4 w-4" /> Oldingi kun
          </Button>
          <DateInput value={date} onChange={setDate} title="Sana" />
          <Button variant="secondary" onClick={() => setDate((d) => shiftDate(d, 1))}>
            Keyingi kun <ChevronRight className="h-4 w-4" />
          </Button>
          <Button variant="secondary" onClick={exportClasses} disabled={!report?.classes.length}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>
      )}

      {report && !report.schoolDay && (
        <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
          {shortDate(report.date)} — o'quv kuni emas (yakshanba, bayram yoki ta'til).
        </div>
      )}

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Tile label="Kutilgan" value={report?.expected ?? 0} hint={`Biriktirilgan: ${report?.linked ?? 0}`} />
        <Tile
          label="Turniketdan o'tdi"
          value={report?.turnstileEntered ?? 0}
          tone="good"
          hint={`${report?.turnstilePct ?? 0}%`}
        />
        <Tile
          label="Jurnalda bor"
          value={report?.journalPresent ?? 0}
          tone="good"
          hint={`${report?.journalPct ?? 0}%`}
        />
        <Tile label="Jurnalda yo'q" value={report?.journalAbsent ?? 0} tone="warn" />
        <Tile
          label="Belgilanmagan"
          value={report?.unchecked ?? 0}
          tone={(report?.unchecked ?? 0) > 0 ? 'bad' : 'neutral'}
          hint="Davomat olinmagan — 'bor' ga qo'shilmaydi"
        />
        <Tile
          label="Farq (turniket − jurnal)"
          value={(report?.gap ?? 0) > 0 ? `+${report?.gap}` : (report?.gap ?? 0)}
          tone={(report?.gap ?? 0) === 0 ? 'neutral' : 'bad'}
          hint={`Nomuvofiqlik: ${mismatchCount} ta`}
        />
      </div>

      <Card className="p-0">
        <h2 className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-700">
          Sinflar bo'yicha
        </h2>
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : !report || report.classes.length === 0 ? (
          <p className="py-10 text-center text-slate-400">Bu kunda dars bo'lgan sinf yo'q</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3 text-center">Kutilgan</th>
                  <th className="px-4 py-3 text-center">Turniket</th>
                  <th className="px-4 py-3 text-center">Jurnalda bor</th>
                  <th className="px-4 py-3 text-center">Jurnalda yo'q</th>
                  <th className="px-4 py-3 text-center">Belgilanmagan</th>
                  <th className="px-4 py-3 text-center">Farq</th>
                  <th className="px-4 py-3 text-center">Nomuvofiqlik</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {report.classes.map((c) => (
                  <tr key={c.classId || c.className} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{c.className || 'Sinfsiz'}</td>
                    <td className="px-4 py-3 text-center text-slate-600">{c.expected}</td>
                    <td className="px-4 py-3 text-center text-slate-600">
                      {c.turnstileEntered}
                      <span className="ml-1 text-xs text-slate-300">{c.turnstilePct}%</span>
                    </td>
                    <td className="px-4 py-3 text-center text-slate-600">
                      {c.journalPresent}
                      <span className="ml-1 text-xs text-slate-300">{c.journalPct}%</span>
                    </td>
                    <td className="px-4 py-3 text-center text-slate-500">{c.journalAbsent || '—'}</td>
                    <td
                      className={cn(
                        'px-4 py-3 text-center',
                        c.unchecked > 0 ? 'font-medium text-rose-600' : 'text-slate-300',
                      )}
                    >
                      {c.unchecked || '—'}
                    </td>
                    <td
                      className={cn(
                        'px-4 py-3 text-center font-semibold',
                        c.gap === 0 ? 'text-slate-300' : c.gap > 0 ? 'text-amber-600' : 'text-sky-600',
                      )}
                    >
                      {c.gap === 0 ? '0' : c.gap > 0 ? `+${c.gap}` : c.gap}
                    </td>
                    <td className="px-4 py-3 text-center text-xs text-slate-500">
                      {c.turnstileOnly + c.journalOnly === 0 ? (
                        <span className="text-slate-300">—</span>
                      ) : (
                        <>
                          {c.turnstileOnly > 0 && <span className="text-amber-600">↑{c.turnstileOnly}</span>}
                          {c.turnstileOnly > 0 && c.journalOnly > 0 && ' · '}
                          {c.journalOnly > 0 && <span className="text-sky-600">↓{c.journalOnly}</span>}
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Nomuvofiqliklar — hisobotning asosiy qismi: raqam emas, ISMLAR. */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
          <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
            <AlertTriangle className="h-4 w-4 text-amber-500" />
            Turniket va jurnal kelishmagan o'quvchilar
          </h2>
          <span
            className="flex items-center gap-1 text-xs text-slate-400"
            title="Qurilma ID biriktirilmagan o'quvchilar bu ro'yxatga kirmaydi — turniket ularni ko'ra olmaydi"
          >
            <Info className="h-3.5 w-3.5" />
            Faqat qurilmaga biriktirilganlar
          </span>
        </div>

        {!report || report.mismatches.length === 0 ? (
          <p className="py-10 text-center text-slate-400">
            Nomuvofiqlik yo'q — turniket va jurnal bir xil gapiryapti
          </p>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">F.I.SH</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Nima bo'lgan</th>
                    <th className="px-4 py-3 text-center">Kirgan</th>
                    <th className="px-4 py-3 text-center">Chiqqan</th>
                    <th className="px-4 py-3">Jurnal</th>
                    <th className="px-4 py-3">Sabab</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {report.mismatches.map((m) => (
                    <tr key={`${m.studentId}-${m.kind}`} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-800">{m.fullName}</td>
                      <td className="px-4 py-3 text-slate-500">{m.className || '—'}</td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            m.kind === 'turnstile-only'
                              ? 'bg-amber-50 text-amber-700'
                              : 'bg-sky-50 text-sky-700',
                          )}
                        >
                          {m.kind === 'turnstile-only'
                            ? "Turniket ko'rdi, jurnal yo'q dedi"
                            : "Jurnal bor dedi, turniket ko'rmadi"}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-center text-slate-600">{m.checkIn || '—'}</td>
                      <td className="px-4 py-3 text-center text-slate-600">{m.checkOut || '—'}</td>
                      <td className="px-4 py-3 text-slate-500">
                        {journalStatusLabel[m.journalStatus] ?? m.journalStatus}
                      </td>
                      <td className="px-4 py-3 text-slate-500">{m.reason || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {report.mismatchTotal > report.mismatches.length && (
              <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
                Jami {report.mismatchTotal} ta — birinchi {report.mismatches.length} tasi ko'rsatilmoqda.
              </p>
            )}
          </>
        )}
      </Card>
    </div>
  )
}
