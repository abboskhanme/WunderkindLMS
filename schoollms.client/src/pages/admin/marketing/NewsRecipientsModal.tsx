/**
 * Yangilikning Telegram xabari kimga yetdi (mijoz, 2026-10-12: "Telegram: 120/150 bunda aynan
 * kimlarga borgan bormaganini bilsa bo'ladimi"). E'lonlardagi "Batafsil" oynasi bilan bir xil:
 * har bir chat — ota-ona (farzandi bilan) yoki xodim (lavozimi bilan) va holati; Telegramga
 * ulanmaganlar ham. Muammolilar tepada.
 */
import { useEffect, useMemo, useState } from 'react'
import { CheckCircle2, CircleSlash, XCircle } from 'lucide-react'
import type { NewsAdminDto, NewsRecipient, NewsRecipients } from '@/api/services/news'
import { getNewsRecipients } from '@/api/services/news'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { telegramSummary } from './newsLabels'

type Filter = 'all' | NewsRecipient['status']

const STATUS: Record<NewsRecipient['status'], { label: string; tone: string; icon: typeof CheckCircle2 }> = {
  sent: { label: 'Yetkazildi', tone: 'text-emerald-700', icon: CheckCircle2 },
  failed: { label: 'Yetmadi', tone: 'text-red-600', icon: XCircle },
  no_telegram: { label: 'Telegramga ulanmagan', tone: 'text-amber-700', icon: CircleSlash },
}

const KIND: Record<NewsRecipient['kind'], string> = {
  parent: 'Ota-ona',
  employee: 'Xodim',
  student: "O'quvchi",
}

export function NewsRecipientsModal({ news, onClose }: { news: NewsAdminDto | null; onClose: () => void }) {
  const [data, setData] = useState<NewsRecipients | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')

  useEffect(() => {
    if (!news) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- boshqa yangilik ochilganda holatni tozalaymiz (maqsadli)
    setData(null)
    setError(null)
    setFilter('all')
    setQuery('')
    getNewsRecipients(news.id)
      .then((d) => active && setData(d))
      .catch(() => active && setError("Ro'yxatni yuklab bo'lmadi"))
    return () => {
      active = false
    }
  }, [news])

  const counts = useMemo(() => {
    const c = { all: 0, sent: 0, failed: 0, no_telegram: 0 }
    for (const r of data?.recipients ?? []) {
      c.all++
      c[r.status]++
    }
    return c
  }, [data])

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (data?.recipients ?? []).filter(
      (r) =>
        (filter === 'all' || r.status === filter) &&
        (!q || r.name.toLowerCase().includes(q) || (r.detail ?? '').toLowerCase().includes(q)),
    )
  }, [data, filter, query])

  return (
    <Modal open={news !== null} onClose={onClose} title="Telegram: kimga yetdi" size="lg">
      {error ? (
        <p className="py-6 text-center text-sm text-red-600">{error}</p>
      ) : !data || !news ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div>
            <p className="font-semibold text-slate-800">{news.title}</p>
            <p className="text-xs text-slate-500">{telegramSummary(news)}</p>
          </div>

          {!data.hasDetails ? (
            <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-500">
              Bu yangilik oluvchilar ro'yxati saqlana boshlashidan oldin (12.10.2026 gacha) yuborilgan — faqat umumiy
              son bor.
            </p>
          ) : (
            <>
              <div className="flex flex-wrap items-center gap-2">
                {([
                  ['all', `Hammasi · ${counts.all}`],
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
                  placeholder="Ism, farzand yoki sinf"
                  className="ml-auto w-52 rounded-lg border border-slate-200 px-3 py-1.5 text-sm outline-none focus:border-brand-400"
                />
              </div>
              <div className="max-h-[45vh] overflow-y-auto rounded-xl border border-slate-100">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-400">
                    <tr>
                      <th className="px-3 py-2 font-medium">Kim</th>
                      <th className="px-3 py-2 font-medium">Turi</th>
                      <th className="px-3 py-2 font-medium">Farzandi / lavozimi</th>
                      <th className="px-3 py-2 font-medium">Holat</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {rows.map((r, i) => {
                      const st = STATUS[r.status]
                      return (
                        <tr key={`${r.chatId ?? 'none'}-${i}`}>
                          <td className="px-3 py-2 font-medium text-slate-800">{r.name || '—'}</td>
                          <td className="px-3 py-2 text-slate-500">{KIND[r.kind]}</td>
                          <td className="px-3 py-2 text-slate-600">{r.detail || '—'}</td>
                          <td className={cn('px-3 py-2', st.tone)}>
                            <span className="inline-flex items-center gap-1 whitespace-nowrap text-xs font-medium">
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
