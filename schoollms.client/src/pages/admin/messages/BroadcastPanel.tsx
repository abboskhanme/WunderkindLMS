import { useEffect, useState } from 'react'
import { Send, Megaphone, Users, AlertTriangle } from 'lucide-react'
import type { Broadcast, TelegramParent, TelegramStatus } from '@/types'
import {
  getBroadcasts,
  getTelegramRegistrations,
  getTelegramStatus,
  sendBroadcast,
} from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatDate } from '@/lib/utils'

/** Bitta sinf ota-onalariga Telegram bot orqali e'lon yuborish + tarix + ro'yxatdagi ota-onalar. */
export function BroadcastPanel({ className }: { className: string }) {
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const [broadcasts, setBroadcasts] = useState<Broadcast[]>([])
  const [parents, setParents] = useState<TelegramParent[]>([])
  const [status, setStatus] = useState<TelegramStatus | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getTelegramStatus().then(setStatus)
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf almashganda e'lon/ro'yxatni qayta yuklash (maqsadli)
    setLoading(true)
    setResult(null)
    Promise.all([getBroadcasts(className), getTelegramRegistrations(className)])
      .then(([b, p]) => {
        setBroadcasts(b)
        setParents(p)
      })
      .finally(() => setLoading(false))
  }, [className])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    const t = text.trim()
    if (!t || sending) return
    setSending(true)
    setResult(null)
    try {
      const b = await sendBroadcast(className, t)
      setText('')
      setBroadcasts((prev) => [b, ...prev])
      setResult(
        b.recipientCount === 0
          ? "Bu sinfda Telegramda ro'yxatdan o'tgan ota-ona yo'q — e'lon saqlandi, lekin hech kimga yuborilmadi."
          : `E'lon yuborildi: ${b.sentCount}/${b.recipientCount} ota-onaga yetkazildi.`,
      )
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="space-y-4">
      {status && !status.configured && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Telegram bot hali sozlanmagan. E'lon saqlanadi, lekin yuborilmaydi. Yuborish uchun
            serverda <code className="rounded bg-amber-100 px-1">appsettings.json</code> da{' '}
            <code className="rounded bg-amber-100 px-1">Telegram:BotToken</code> ni to'ldiring.
          </p>
        </div>
      )}

      <Card>
        <div className="mb-2 flex items-center gap-2">
          <Megaphone className="h-5 w-5 text-brand-600" />
          <p className="font-semibold text-slate-800">{className} sinfi ota-onalariga e'lon</p>
        </div>
        <form onSubmit={handleSend} className="space-y-3">
          <textarea
            className="h-28 w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            placeholder="E'lon matnini yozing — bot orqali ro'yxatdagi ota-onalarga yuboriladi"
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
          <div className="flex items-center justify-between gap-3">
            <span className="inline-flex items-center gap-1.5 text-sm text-slate-500">
              <Users className="h-4 w-4" />
              {parents.length} ta ro'yxatdagi ota-ona
            </span>
            <Button type="submit" disabled={!text.trim() || sending}>
              <Send className="h-4 w-4" /> Yuborish
            </Button>
          </div>
          {result && <p className="text-sm font-medium text-emerald-700">{result}</p>}
        </form>
      </Card>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Card>
            <p className="mb-3 font-semibold text-slate-800">Yuborilgan e'lonlar</p>
            <div className="space-y-3">
              {broadcasts.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">Hali e'lon yuborilmagan</p>
              )}
              {broadcasts.map((b) => (
                <div key={b.id} className="rounded-lg border border-slate-100 p-3">
                  <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{b.text}</p>
                  <div className="mt-1.5 flex items-center justify-between text-xs text-slate-400">
                    <span>{formatDate(b.createdAt)}</span>
                    <span>
                      {b.sentCount}/{b.recipientCount} yetkazildi
                    </span>
                  </div>
                </div>
              ))}
            </div>
          </Card>

          <Card>
            <p className="mb-1 font-semibold text-slate-800">Ro'yxatdagi ota-onalar</p>
            <p className="mb-3 text-xs text-slate-400">
              Ota-ona Telegram botda raqamini ulashib ro'yxatdan o'tadi
              {status?.botUsername ? ` (@${status.botUsername})` : ''}.
            </p>
            <div className="space-y-2">
              {parents.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">
                  Bu sinfda ro'yxatdan o'tgan ota-ona yo'q
                </p>
              )}
              {parents.map((p) => (
                <div
                  key={`${p.studentId}-${p.chatId}`}
                  className="flex items-center justify-between gap-2 rounded-lg border border-slate-100 px-3 py-2 text-sm"
                >
                  <div>
                    <p className="font-medium text-slate-700">{p.studentName}</p>
                    <p className="text-xs text-slate-400">
                      {p.parentName || 'Ota-ona'} · {p.phone}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}
    </div>
  )
}
