/**
 * `blocked` (§7.3, §7.4) — the attempt is in progress on another browser.
 *
 * The server recognises the device by an HttpOnly cookie it set on `start`; this
 * screen is the whole client side of that. It names the other device with the
 * server's coarse label ("Chrome · Windows") and nothing more — no candidate
 * name, no exam title: whoever holds a forwarded link on a second phone learns
 * only that the test is running somewhere else.
 *
 * The way out is the school's "Qurilma qulfini ochish" (§7.4.5). After it, the
 * next `state` call re-binds the attempt to whichever browser asks, answers
 * intact — hence the re-check button.
 */
import { Loader2, MonitorSmartphone, RefreshCw, ShieldAlert } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'

interface DeviceLockProps {
  deviceLabel: string | null
  checking: boolean
  onRecheck: () => void
}

export function DeviceLock({ deviceLabel, checking, onRecheck }: DeviceLockProps) {
  return (
    <Card className="flex flex-col items-center gap-4 py-8 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-amber-50">
        <ShieldAlert className="h-7 w-7 text-amber-500" />
      </div>

      <div className="space-y-1.5">
        <h1 className="text-lg font-semibold text-slate-800">Test boshqa qurilmada ochilgan</h1>
        <p className="text-base text-slate-500">
          Xavfsizlik uchun test faqat boshlangan qurilma va brauzerda davom ettiriladi.
        </p>
      </div>

      {deviceLabel && (
        <div className="inline-flex max-w-full items-center gap-2 rounded-full bg-slate-100 px-3.5 py-2 text-sm font-medium text-slate-700">
          <MonitorSmartphone className="h-4 w-4 shrink-0 text-slate-400" />
          <span className="truncate">{deviceLabel}</span>
        </div>
      )}

      <ol className="w-full space-y-2.5 rounded-xl bg-slate-50 p-4 text-left text-sm text-slate-600">
        <li className="flex gap-2.5">
          <span className="font-semibold text-slate-400">1.</span>
          <span>Testni boshlagan qurilmada shu havolani oching.</span>
        </li>
        <li className="flex gap-2.5">
          <span className="font-semibold text-slate-400">2.</span>
          <span>
            U qurilmadan foydalanib bo'lmasa, maktabga murojaat qiling — qulfni ochib berishadi.
            Saqlangan javoblaringiz yo'qolmaydi.
          </span>
        </li>
      </ol>

      <Button variant="secondary" onClick={onRecheck} disabled={checking} className="h-11 px-5">
        {checking ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
        Qayta tekshirish
      </Button>
    </Card>
  )
}
