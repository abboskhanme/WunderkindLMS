import { useEffect, useState } from 'react'
import { AlertTriangle, Loader2 } from 'lucide-react'
import {
  applyWithdrawal, previewWithdrawal,
  type WithdrawalPreview,
} from '@/api/services/studentWithdrawals'
import { financeErrorMessage } from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { cn, formatMoney } from '@/lib/utils'

const MONTHS = ['Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun', 'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr']

function monthLabel(ym: string): string {
  const [y, m] = ym.split('-').map(Number)
  return m >= 1 && m <= 12 ? `${MONTHS[m - 1]} ${y}` : ym
}

function todayISO(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

interface Props {
  open: boolean
  studentId: string
  studentName: string
  onClose: () => void
  /** Bajarildi — natija bilan (balans va hisobdan chiqarilgan summa). */
  onDone: (result: WithdrawalPreview) => void
}

/**
 * "Chiqib ketdi — abonementni bekor qilish" (mijoz, 2026-10-06). Oxirgi o'qigan kun tanlanadi:
 * o'sha oy o'qilgan dars kunlari bo'yicha qoladi, qolgani va keyingi oylar hisobdan chiqariladi,
 * ortiqcha to'lov avansga qaytadi. Server hisoblaydi — bu oyna faqat ko'rsatadi.
 */
export function WithdrawModal({ open, studentId, studentName, onClose, onDone }: Props) {
  const [lastDay, setLastDay] = useState(todayISO())
  /** null — server tanlovi (shu kundan keyin davom etadigan hammasi). */
  const [selected, setSelected] = useState<string[] | null>(null)
  const [note, setNote] = useState('')
  const [preview, setPreview] = useState<WithdrawalPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !lastDay) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      setLoading(true)
      setError(null)
      previewWithdrawal(studentId, { lastDay, subscriptionIds: selected ?? undefined }, controller.signal)
        .then((p) => {
          setPreview(p)
          if (selected === null) setSelected(p.subscriptions.filter((s) => s.selected).map((s) => s.id))
        })
        .catch((e: unknown) => {
          if (controller.signal.aborted) return
          setPreview(null)
          setError(financeErrorMessage(e, "Hisoblab bo'lmadi."))
        })
        .finally(() => {
          if (!controller.signal.aborted) setLoading(false)
        })
    }, 250)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [open, studentId, lastDay, selected])

  const toggle = (id: string) =>
    setSelected((cur) => {
      const list = cur ?? []
      return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]
    })

  const submit = async () => {
    if (!selected?.length) return
    setSaving(true)
    setError(null)
    try {
      const result = await applyWithdrawal(studentId, { lastDay, subscriptionIds: selected, note: note.trim() || undefined })
      onDone(result)
    } catch (e) {
      setError(financeErrorMessage(e, "Bekor qilib bo'lmadi."))
    } finally {
      setSaving(false)
    }
  }

  const nothingSelected = (selected?.length ?? 0) === 0

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Chiqib ketdi — abonementni bekor qilish"
      footer={
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={submit} disabled={saving || loading || nothingSelected || !preview} className="bg-red-600 hover:bg-red-700">
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            {preview && preview.totalWriteOff > 0
              ? `Bekor qilish — ${formatMoney(preview.totalWriteOff)}`
              : 'Abonementni yopish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-5">
        <p className="text-sm text-slate-600">
          <b className="font-semibold text-slate-800">{studentName}</b> — oxirgi o'qigan kunini tanlang. O'sha oy o'qilgan
          dars kunlari uchun to'lov qoladi, qolgani va keyingi oylar hisobdan chiqariladi. Kassaga tegilmaydi.
        </p>

        <div className="grid gap-4 sm:grid-cols-2">
          <DatePicker label="Oxirgi o'qigan kun" value={lastDay} onChange={setLastDay} max={todayISO()} required />
          <div>
            <p className="mb-1.5 text-sm font-medium text-slate-700">Abonementlar</p>
            {preview && preview.subscriptions.length === 0 ? (
              <p className="text-sm text-slate-400">Shu kundan keyin davom etadigan abonement yo'q.</p>
            ) : (
              <div className="space-y-1.5">
                {preview?.subscriptions.map((s) => (
                  <label key={s.id} className="flex cursor-pointer items-start gap-2 text-sm text-slate-700">
                    <input
                      type="checkbox"
                      className="mt-0.5 h-4 w-4 rounded border-slate-300"
                      checked={selected?.includes(s.id) ?? s.selected}
                      onChange={() => toggle(s.id)}
                    />
                    <span>
                      {s.planName ?? s.categoryName}
                      <span className="block text-xs text-slate-400">{formatMoney(s.monthlyAmount)} / oy</span>
                    </span>
                  </label>
                ))}
              </div>
            )}
          </div>
        </div>

        {error && (
          <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
          </div>
        )}

        {/* ---------- Natija ---------- */}
        <div className={cn('rounded-xl border border-slate-200', loading && 'opacity-60')}>
          {preview && preview.lines.length > 0 ? (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                    <th className="px-3 py-2 font-medium">Oy</th>
                    <th className="px-3 py-2 font-medium">Abonement</th>
                    <th className="px-3 py-2 text-right font-medium">Hisoblangan</th>
                    <th className="px-3 py-2 text-right font-medium">Qoladi</th>
                    <th className="px-3 py-2 text-right font-medium">Bekor qilinadi</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.lines.map((l) => (
                    <tr key={l.invoiceId} className="border-b border-slate-50 last:border-0">
                      <td className="whitespace-nowrap px-3 py-2 text-slate-700">{monthLabel(l.month)}</td>
                      <td className="px-3 py-2 text-slate-600">
                        {l.categoryName}
                        {l.lessonDays > 0 && (
                          <span className="block text-xs text-slate-400">
                            {l.studiedDays} / {l.lessonDays} dars kuni o'qidi
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-slate-600">{formatMoney(l.payable)}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-slate-700">{formatMoney(l.keep)}</td>
                      <td className="whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums text-red-600">
                        −{formatMoney(l.writeOff)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="px-4 py-6 text-center text-sm text-slate-400">
              {loading ? 'Hisoblanmoqda…' : "Bekor qilinadigan summa yo'q — abonement shu kundan yopiladi."}
            </p>
          )}
        </div>

        {preview && (
          <div className="grid gap-3 sm:grid-cols-3">
            <Summary label="Hisobdan chiqariladi" value={formatMoney(preview.totalWriteOff)} tone="bad" />
            <Summary
              label="Avansga qaytadi"
              value={formatMoney(preview.totalReleased)}
              hint={preview.totalReleased > 0 ? "Ortiqcha to'lov — boshqa qarzni yopadi yoki qaytariladi" : undefined}
            />
            <Summary
              label="Balans"
              value={`${formatMoney(preview.balanceBefore)} → ${formatMoney(preview.balanceAfter)}`}
              tone={preview.balanceAfter < 0 ? 'bad' : 'good'}
            />
          </div>
        )}

        <Textarea
          label="Izoh (ixtiyoriy)"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={2}
          maxLength={300}
          placeholder="Masalan: boshqa shaharga ko'chib ketdi"
        />
      </div>
    </Modal>
  )
}

function Summary({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: 'good' | 'bad' }) {
  return (
    <div className="rounded-xl bg-slate-50 px-3 py-2.5">
      <p className="text-xs text-slate-400">{label}</p>
      <p
        className={cn(
          'mt-0.5 text-sm font-semibold tabular-nums',
          tone === 'bad' ? 'text-red-600' : tone === 'good' ? 'text-emerald-600' : 'text-slate-800',
        )}
      >
        {value}
      </p>
      {hint && <p className="mt-0.5 text-xs text-slate-400">{hint}</p>}
    </div>
  )
}
