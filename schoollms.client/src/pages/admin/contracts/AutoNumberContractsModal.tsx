import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle2 } from 'lucide-react'
import type { AutoNumberContractsResult } from '@/api/services/studentContracts'
import { autoNumberContracts } from '@/api/services/studentContracts'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

/**
 * HAMMAGA KETMA-KET SHARTNOMA RAQAMI (mijoz, 2026-10-29: "1a sinfidagi ro'yxatdagi birinchi
 * o'quvchidan (WK-1) 11b sinfidagi oxirgi o'quvchigacha (WK-467)").
 *
 * Avval REJA ko'rsatiladi (server hech narsa yozmaydi): kim qaysi raqamni oladi, kimda raqam
 * allaqachon bor. "Raqam qo'yish" bosilgandagina yoziladi — hammasi bitta tranzaksiyada.
 * Tartib sinflar ro'yxatidagidek (1-A … 11-B), sinf ichida F.I.SH. bo'yicha.
 */

interface Props {
  open: boolean
  onClose: () => void
  onDone: (result: AutoNumberContractsResult) => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const actionLabels = {
  new: 'Yangi',
  draft: 'Qoralamaga',
  skip: 'Raqami bor',
  taken: 'Band',
} as const

export function AutoNumberContractsModal({ open, onClose, onDone }: Props) {
  const [prefix, setPrefix] = useState('WK-')
  const [start, setStart] = useState('1')
  const [plan, setPlan] = useState<AutoNumberContractsResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const startNumber = Number.parseInt(start, 10)

  // Prefiks yoki boshlang'ich raqam o'zgarsa — reja qayta hisoblanadi (hech narsa yozilmaydi).
  useEffect(() => {
    if (!open) return
    if (!Number.isFinite(startNumber) || startNumber < 1) {
      setPlan(null)
      return
    }
    let cancelled = false
    const timer = setTimeout(async () => {
      setLoading(true)
      setError(null)
      try {
        const data = await autoNumberContracts({ prefix, start: startNumber, apply: false })
        if (!cancelled) setPlan(data)
      } catch (e) {
        if (!cancelled) {
          setPlan(null)
          setError(errorText(e, "Rejani olib bo'lmadi"))
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 300)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [open, prefix, startNumber])

  const apply = async () => {
    if (!plan || plan.assigned === 0 || plan.conflicts > 0 || saving) return
    if (
      !confirm(
        `${plan.assigned} ta o'quvchiga ${plan.firstNumber} dan ${plan.lastNumber} gacha raqam qo'yilsinmi?`,
      )
    )
      return
    setSaving(true)
    setError(null)
    try {
      const result = await autoNumberContracts({ prefix, start: startNumber, apply: true })
      onDone(result)
    } catch (e) {
      setError(errorText(e, "Raqam qo'yib bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const canApply = !!plan && plan.assigned > 0 && plan.conflicts === 0 && !loading && !saving

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Shartnoma raqamlarini avtomatik qo'yish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Yopish
          </Button>
          <Button onClick={apply} disabled={!canApply}>
            {saving ? 'Yozilmoqda...' : `Raqam qo'yish${plan ? ` (${plan.assigned})` : ''}`}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Prefiks"
            value={prefix}
            onChange={(e) => setPrefix(e.target.value)}
            placeholder="WK-"
          />
          <Input
            label="Boshlang'ich raqam"
            type="number"
            min={1}
            value={start}
            onChange={(e) => setStart(e.target.value)}
          />
        </div>

        <p className="text-sm text-slate-500">
          Tartib sinflar ro'yxatidagidek (1-A … 11-B), sinf ichida F.I.SH. bo'yicha. Raqami bor
          o'quvchi o'zgarmaydi. Avval quyidagi reja ko'rsatiladi — «Raqam qo'yish» bosilmaguncha hech
          narsa yozilmaydi.
        </p>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
        {loading && !plan && <Loader label="Reja tayyorlanmoqda..." />}

        {plan && (
          <>
            <div
              className={cn(
                'flex items-start gap-2 rounded-xl p-3 text-sm',
                plan.conflicts > 0 ? 'bg-red-50 text-red-700' : 'bg-emerald-50 text-emerald-800',
              )}
            >
              {plan.conflicts > 0 ? (
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              ) : (
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
              )}
              <div>
                {plan.assigned > 0 ? (
                  <p className="font-medium">
                    {plan.assigned} ta o'quvchi: {plan.firstNumber} — {plan.lastNumber}
                  </p>
                ) : (
                  <p className="font-medium">Raqam qo'yiladigan o'quvchi yo'q</p>
                )}
                {plan.skipped > 0 && <p>{plan.skipped} ta o'quvchida raqam allaqachon bor — o'zgarmaydi.</p>}
                {plan.conflicts > 0 && (
                  <p>{plan.conflicts} ta raqam boshqa shartnomada band — prefiks yoki boshlang'ich raqamni o'zgartiring.</p>
                )}
                {plan.withoutClass > 0 && (
                  <p className="text-amber-700">
                    {plan.withoutClass} ta faol o'quvchining sinfi yo'q — ular raqamlanmaydi.
                  </p>
                )}
              </div>
            </div>

            <div className="max-h-80 overflow-y-auto rounded-xl border border-slate-100">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Raqam</th>
                    <th className="px-3 py-2">O'quvchi</th>
                    <th className="px-3 py-2">Sinf</th>
                    <th className="px-3 py-2" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {plan.rows.map((r) => (
                    <tr key={r.studentId} className={r.action === 'skip' ? 'text-slate-400' : ''}>
                      <td className="whitespace-nowrap px-3 py-2 font-medium">
                        {r.number ?? `№ ${r.existingNumber}`}
                      </td>
                      <td className="px-3 py-2">{r.studentName}</td>
                      <td className="whitespace-nowrap px-3 py-2">{r.className}</td>
                      <td
                        className={cn(
                          'whitespace-nowrap px-3 py-2 text-xs',
                          r.action === 'taken' && 'font-semibold text-red-600',
                        )}
                      >
                        {actionLabels[r.action]}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}
