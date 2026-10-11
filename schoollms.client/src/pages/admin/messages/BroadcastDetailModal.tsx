/**
 * Yuborilgan e'lon batafsil (mijoz, 2026-10-11: "yuborilgan elonlar haqida batafsil malumot chiqib
 * turishi. masalan kimga"): matn, kim yubordi, qachon va har bir o'quvchi — ota-onasiga yetdimi,
 * yetmadimi yoki Telegramga ulanmaganmi. Muammolilar ro'yxat boshida.
 */
import { useEffect, useMemo, useState } from 'react'
import { CheckCircle2, CircleSlash, XCircle } from 'lucide-react'
import type { BroadcastDetail, BroadcastRecipient } from '@/types'
import { getBroadcastDetail } from '@/api/services/messages'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { RichText } from '@/components/ui/RichText'
import { cn, formatDate } from '@/lib/utils'

type Filter = 'all' | BroadcastRecipient['status']

const STATUS: Record<BroadcastRecipient['status'], { label: string; tone: string; icon: typeof CheckCircle2 }> = {
  sent: { label: 'Yetkazildi', tone: 'text-emerald-700', icon: CheckCircle2 },
  failed: { label: 'Yetmadi', tone: 'text-red-600', icon: XCircle },
  no_telegram: { label: "Telegramga ulanmagan", tone: 'text-amber-700', icon: CircleSlash },
}

export function BroadcastDetailModal({ id, onClose }: { id: string | null; onClose: () => void }) {
  const [data, setData] = useState<BroadcastDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')

  useEffect(() => {
    if (!id) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi e'lon ochilganda holatni tozalaymiz (maqsadli)
    setData(null)
    setError(null)
    setFilter('all')
    setQuery('')
    getBroadcastDetail(id)
      .then((d) => active && setData(d))
      .catch(() => active && setError("E'lonni yuklab bo'lmadi"))
    return () => {
      active = false
    }
  }, [id])

  const counts = useMemo(() => {
    const c = { all: 0, sent: 0, failed: 0, no_telegram: 0 }
    const seen = new Set<string>()
    for (const r of data?.recipients ?? []) {
      c[r.status]++
      if (!seen.has(r.studentId)) {
        seen.add(r.studentId)
        c.all++
      }
    }
    return c
  }, [data])

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (data?.recipients ?? []).filter(
      (r) =>
        (filter === 'all' || r.status === filter) &&
        (!q || r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q) || r.parentName.toLowerCase().includes(q)),
    )
  }, [data, filter, query])

  const b = data?.broadcast

  return (
    <Modal open={id !== null} onClose={onClose} title="E'lon batafsil" size="lg">
      {error ? (
        <p className="py-6 text-center text-sm text-red-600">{error}</p>
      ) : !data || !b ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
            <span className="rounded-md bg-slate-100 px-2 py-0.5 font-medium text-slate-700">{b.className}</span>
            <span>{b.senderName}</span>
            <span>{formatDate(b.createdAt)}</span>
            <span>
              {b.sentCount}/{b.recipientCount} chatga yetkazildi
            </span>
          </div>
          <RichText html={data.html} className="rounded-xl bg-slate-50 px-4 py-3 text-sm text-slate-700" />

          {!data.hasDetails ? (
            <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-500">
              Bu e'lon oluvchilar ro'yxati saqlana boshlashidan oldin (11.10.2026 gacha) yuborilgan — faqat umumiy son bor.
            </p>
          ) : (
            <>
              <div className="flex flex-wrap items-center gap-2">
                {([
                  ['all', `Hammasi · ${counts.all} o'quvchi`],
                  ['sent', `Yetkazildi · ${counts.sent}`],
                  ['failed', `Yetmadi · ${counts.failed}`],
                  ['no_telegram', `Telegram yo'q · ${counts.no_telegram}`],
                ] as const).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setFilter(key)}
                    className={cn(
                      'rounded-lg px-3 py-1.5 text-xs font-medium transition-colors',
                      filter === key ? 'bg-brand-600 text-white' : 'border border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    {label}
                  </button>
                ))}
                <input
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="O'quvchi, sinf yoki ota-ona"
                  className="ml-auto w-52 rounded-lg border border-slate-200 px-3 py-1.5 text-sm outline-none focus:border-brand-400"
                />
              </div>
              <div className="max-h-[45vh] overflow-y-auto rounded-xl border border-slate-100">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-400">
                    <tr>
                      <th className="px-3 py-2 font-medium">O'quvchi</th>
                      <th className="px-3 py-2 font-medium">Sinf</th>
                      <th className="px-3 py-2 font-medium">Ota-ona (Telegram)</th>
                      <th className="px-3 py-2 font-medium">Holat</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {rows.map((r, i) => {
                      const st = STATUS[r.status]
                      return (
                        <tr key={`${r.studentId}-${r.chatId ?? i}`}>
                          <td className="px-3 py-2 font-medium text-slate-800">{r.fullName}</td>
                          <td className="px-3 py-2 text-slate-500">{r.className || '—'}</td>
                          <td className="px-3 py-2 text-slate-600">{r.parentName || (r.chatId ? 'Ota-ona' : '—')}</td>
                          <td className={cn('px-3 py-2', st.tone)}>
                            <span className="inline-flex items-center gap-1 text-xs font-medium">
                              <st.icon className="h-3.5 w-3.5" /> {st.label}
                            </span>
                          </td>
                        </tr>
                      )
                    })}
                    {rows.length === 0 && (
                      <tr>
                        <td colSpan={4} className="px-3 py-6 text-center text-slate-400">
                          Mos qator yo'q
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      )}
    </Modal>
  )
}
