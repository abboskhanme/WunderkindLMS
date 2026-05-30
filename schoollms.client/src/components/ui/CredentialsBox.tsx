import { useState } from 'react'
import { Check, Copy, KeyRound, RefreshCw } from 'lucide-react'
import type { Credentials } from '@/types'
import { copyText } from '@/lib/utils'

interface Props {
  credentials: Credentials | null
  loading?: boolean
  /** Berilsa — "Yangi parol yaratish" tugmasi ko'rsatiladi (parol xavfsizlik uchun saqlanmaydi). */
  onReset?: () => Promise<void>
}

/**
 * O'quvchi/o'qituvchi profilida tizimga kirish ma'lumotlarini (login/parol) ko'rsatadi.
 *
 * Parol XAVFSIZLIK uchun bazada saqlanmaydi — faqat hash. Shuning uchun ochiq parol odatda
 * bo'sh keladi; admin "Yangi parol yaratish" tugmasi orqali yangi parol generatsiya qiladi
 * va u BIR MARTA shu yerda ko'rsatiladi (topshirib olgach yo'qoladi).
 */
export function CredentialsBox({ credentials, loading, onReset }: Props) {
  const [resetting, setResetting] = useState(false)

  const handleReset = async () => {
    if (!onReset || resetting) return
    setResetting(true)
    try {
      await onReset()
    } finally {
      setResetting(false)
    }
  }

  return (
    <div className="mt-4 rounded-xl border border-brand-100 bg-brand-50/50 p-4">
      <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-brand-700">
        <KeyRound className="h-4 w-4" /> Tizimga kirish ma'lumotlari
      </div>
      {loading || !credentials ? (
        <p className="text-sm text-slate-400">Yuklanmoqda...</p>
      ) : (
        <div className="space-y-2">
          <CredRow label="Login" value={credentials.login} />
          {credentials.password ? (
            <CredRow label="Parol" value={credentials.password} />
          ) : (
            <p className="px-1 text-xs text-slate-400">
              Parol xavfsizlik uchun saqlanmaydi. Yangi parol yarating — u faqat bir marta ko'rsatiladi.
            </p>
          )}
          {onReset && (
            <button
              type="button"
              onClick={handleReset}
              disabled={resetting}
              className="mt-1 inline-flex items-center gap-2 rounded-lg border border-brand-200 bg-white px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-50 disabled:opacity-60"
            >
              <RefreshCw className={`h-4 w-4 ${resetting ? 'animate-spin' : ''}`} />
              {resetting ? 'Yaratilmoqda...' : 'Yangi parol yaratish'}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

function CredRow({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false)

  const onCopy = async () => {
    if (await copyText(value)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }
  }

  return (
    <div className="flex items-center justify-between gap-3 rounded-lg bg-white px-3 py-2">
      <span className="text-xs uppercase tracking-wide text-slate-400">{label}</span>
      <div className="flex items-center gap-2">
        <code className="select-all text-sm font-medium text-slate-800">{value || '—'}</code>
        <button
          type="button"
          onClick={onCopy}
          title="Nusxalash"
          className="rounded p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
        >
          {copied ? <Check className="h-4 w-4 text-emerald-600" /> : <Copy className="h-4 w-4" />}
        </button>
      </div>
    </div>
  )
}
