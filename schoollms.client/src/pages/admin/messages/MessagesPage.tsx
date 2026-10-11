/**
 * Xabarlar — ota-onalarga Telegram orqali e'lon (mijoz, 2026-10-11: guruh chati Future'ga
 * ko'chdi, push olib tashlandi — "bizda hamma narsa telegram orqali").
 */
import { useEffect, useState } from 'react'
import type { MessageClass } from '@/types'
import { getMessageClasses } from '@/api/services/messages'
import { Loader } from '@/components/ui/Loader'
import { BroadcastPanel } from './BroadcastPanel'

export function MessagesPage() {
  const [classes, setClasses] = useState<MessageClass[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getMessageClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Xabarlar</h1>
        <p className="text-sm text-slate-400">Ota-onalarga Telegram orqali e'lon va yuborilganlar tarixi</p>
      </div>
      {loading ? <Loader label="Yuklanmoqda..." /> : <BroadcastPanel classes={classes} />}
    </div>
  )
}
