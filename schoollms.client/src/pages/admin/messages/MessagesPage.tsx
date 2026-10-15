/**
 * Xabarlar — ota-onalarga Telegram orqali e'lon (mijoz, 2026-10-11: guruh chati Future'ga
 * ko'chdi, push olib tashlandi — "bizda hamma narsa telegram orqali"). 2026-10-13 dan ikkinchi
 * yorliq: qoida bo'yicha o'zi ketadigan xabarlar (AutoMessagesPanel).
 */
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { MessageClass } from '@/types'
import { getMessageClasses } from '@/api/services/messages'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { BroadcastPanel } from './BroadcastPanel'
import { AutoMessagesPanel } from './AutoMessagesPanel'

const TABS = [
  { key: 'broadcast', label: "E'lonlar" },
  { key: 'auto', label: 'Avtomatik xabarlar' },
] as const

export function MessagesPage() {
  const [classes, setClasses] = useState<MessageClass[]>([])
  const [loading, setLoading] = useState(true)
  const [params, setParams] = useSearchParams()
  const tab = params.get('tab') === 'auto' ? 'auto' : 'broadcast'

  useEffect(() => {
    getMessageClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xabarlar</h1>
          <p className="text-sm text-slate-400">Ota-onalarga Telegram orqali e'lon va yuborilganlar tarixi</p>
        </div>
        <div className="flex rounded-xl bg-slate-100 p-1 text-sm font-medium">
          {TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setParams(t.key === 'broadcast' ? {} : { tab: t.key }, { replace: true })}
              className={cn(
                'rounded-lg px-4 py-1.5 transition-colors',
                tab === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {t.label}
            </button>
          ))}
        </div>
      </div>
      {tab === 'auto' ? (
        <AutoMessagesPanel />
      ) : loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <BroadcastPanel classes={classes} />
      )}
    </div>
  )
}
