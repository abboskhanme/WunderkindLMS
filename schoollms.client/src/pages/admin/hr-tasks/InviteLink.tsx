import { useState } from 'react'
import { Check, Copy, Link2 } from 'lucide-react'
import { hrErrorMessage, issueInvite } from '@/api/services/hrTasks'
import type { HrEmployee } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { copyToClipboard } from '@/components/hr-tasks/hooks'
import { cn } from '@/lib/utils'

/**
 * Telegram link (hr-tasks.md §2): our one-time link-code mechanism — the backend returns
 * the `https://t.me/<bot>?start…` URL. Without a configured bot only the code comes back
 * and that is copied.
 */
export function InviteCopyButton({
  employee,
  iconOnly = false,
  className,
  onDone,
}: {
  employee: HrEmployee
  iconOnly?: boolean
  className?: string
  onDone: (message: string, tone: 'success' | 'error') => void
}) {
  const [busy, setBusy] = useState(false)
  const [copied, setCopied] = useState(false)

  async function handleCopy(): Promise<void> {
    setBusy(true)
    try {
      const invite = await issueInvite(employee.id)
      const text = invite.inviteUrl ?? invite.inviteCode
      if (!text) {
        onDone('Taklif kodini olib bo‘lmadi', 'error')
        return
      }
      await copyToClipboard(text)
      onDone(
        invite.inviteUrl
          ? 'Havola nusxalandi'
          : 'Telegram bot sozlanmagan — kod nusxalandi. Havola uchun Sozlamalar → Integratsiyalar bo‘limida botni ulang.',
        invite.inviteUrl ? 'success' : 'error',
      )
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1800)
    } catch (err) {
      onDone(hrErrorMessage(err), 'error')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Button
      type="button"
      variant={iconOnly ? 'ghost' : 'secondary'}
      className={cn(iconOnly ? 'p-2' : 'px-3', className)}
      disabled={busy}
      aria-label="Taklif havolasini nusxalash"
      title="Taklif havolasini nusxalash"
      onClick={() => void handleCopy()}
    >
      {copied ? <Check className="h-4 w-4 text-emerald-600" /> : <Copy className="h-4 w-4" />}
      {!iconOnly && (copied ? 'Nusxalandi' : 'Havola')}
    </Button>
  )
}

/** The link row shown after saving + "Nusxa olish". */
export function InviteLinkBox({ code, url }: { code: string | null; url: string | null }) {
  const [copied, setCopied] = useState(false)
  const text = url ?? code ?? ''

  async function handleCopy(): Promise<void> {
    if (!text) return
    await copyToClipboard(text)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1800)
  }

  return (
    <div className="space-y-2 rounded-xl border border-emerald-200 bg-emerald-50 p-3">
      <p className="flex items-center gap-2 text-sm font-medium text-emerald-800">
        <Link2 className="h-4 w-4" />
        Taklif havolasi
      </p>
      <p className="text-xs text-emerald-800/80">
        Xodimga shu havolani yuboring — u bosgach bot uni avtomatik taniydi. Havola bir martalik.
      </p>
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <code className="min-w-0 flex-1 truncate rounded-lg border border-emerald-200 bg-white px-3 py-2 font-mono text-xs text-slate-700">
          {text || 'Kod berilmadi'}
        </code>
        <Button type="button" variant="secondary" className="shrink-0 px-3" disabled={!text} onClick={() => void handleCopy()}>
          {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
          {copied ? 'Nusxalandi' : 'Nusxa olish'}
        </Button>
      </div>
      {!url && code && (
        <p className="text-xs text-amber-700">
          Telegram bot hali ulanmagan, shuning uchun faqat kod ko‘rsatilmoqda. Botni Sozlamalar → Integratsiyalar
          bo‘limida ulang.
        </p>
      )}
    </div>
  )
}
