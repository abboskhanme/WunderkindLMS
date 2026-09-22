/**
 * Ota-ona / o'quvchi portalidagi yangiliklar lentasi — SM-11,
 * `docs/modules/sales-marketing.md` §3.4, §5.5.
 *
 * WHY THE NAME IS `PortalNewsPage` AND NOT `NewsPage` (§6.3). `App.tsx`
 * already imports the admin `NewsPage` from `pages/admin/marketing/`; two
 * default-shaped exports with one name in a single router file is how the
 * wrong component ends up on a route.
 *
 * THE BODY IS PLAIN TEXT (§3.3 N2). It is stored verbatim and rendered with
 * `whitespace-pre-line` — no markdown renderer, no HTML, no
 * `dangerouslySetInnerHTML`. A sanitiser plus three renderers is three
 * chances at stored XSS; a school announcement does not need bold text badly
 * enough to buy that.
 *
 * THE ENDPOINT DECIDES THE AUDIENCE, NOT THIS PAGE (§5.5). `/api/student/news`
 * is gated `student,parent,admin` and picks `for_parent` or `for_student` from
 * the CALLER'S ROLE, so the same component serves both `/parent/yangiliklar`
 * and `/student/yangiliklar` and there is no audience parameter to get wrong.
 *
 * NOT ROUTED YET. The route and the sidebar entry belong to the sequential
 * wiring pass (SM-12, §7.1) — until it lands this page is unreachable, which
 * is what `MENU-PARITY.md` asks for: no half-wired menu entry.
 */
import { AlertTriangle, Newspaper, RefreshCw, UserRound } from 'lucide-react'
import { api } from '@/api/client'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatDate } from '@/lib/utils'

/**
 * `NewsFeedDto` — §5.5. Deliberately smaller than `NewsAdminDto`: no audience
 * array, no Telegram counters, no author id. A parent does not need to know
 * who else received it.
 */
export interface PortalNewsItem {
  id: string
  title: string
  body: string
  imageUrl: string | null
  /** ISO 8601. Never null here — the feed only returns published items. */
  publishedAt: string
  authorName: string
}

/**
 * The reader endpoints are not pinned to an envelope in §5.5, while the admin
 * list (§5.4) answers `{ total, rows }`. Both shapes are accepted so the page
 * does not depend on which one SM-6 ships.
 */
type NewsFeedResponse = PortalNewsItem[] | { rows?: PortalNewsItem[] | null } | null

/** §5.5: `take` defaults to 20 and is capped at 50. The page asks for the cap. */
const TAKE = 50

/** `GET /api/student/news?take=50` — published, this reader's audience, newest first. */
async function getPortalNews(): Promise<PortalNewsItem[]> {
  const { data } = await api.get<NewsFeedResponse>('/student/news', { params: { take: TAKE } })
  const rows = Array.isArray(data) ? data : (data?.rows ?? [])
  // The server sorts; sorting again is free and keeps "newest first" true even
  // if an envelope or a cache ever reorders the rows on the way here.
  return [...rows].sort((a, b) => (a.publishedAt < b.publishedAt ? 1 : -1))
}

export function PortalNewsPage() {
  const { data, loading, error, refetch } = useAsync<PortalNewsItem[]>(() => getPortalNews(), [])

  const header = (
    <div>
      <h1 className="text-xl font-semibold text-slate-800">Yangiliklar</h1>
      <p className="text-sm text-slate-400">Maktab e'lon qilgan xabarlar — eng yangisi yuqorida.</p>
    </div>
  )

  if (loading) {
    return (
      <div className="space-y-6">
        {header}
        <Loader label="Yangiliklar yuklanmoqda..." />
      </div>
    )
  }

  if (error) {
    return (
      <div className="space-y-6">
        {header}
        <Card className="flex flex-col items-center gap-3 py-12 text-center">
          <AlertTriangle className="h-8 w-8 text-amber-500" />
          <p className="font-medium text-slate-700">Ma'lumotni olib bo'lmadi</p>
          <p className="max-w-md text-sm text-slate-400">
            Internet aloqasini tekshirib, qayta urinib ko'ring. Muammo takrorlansa maktab
            ma'muriyatiga murojaat qiling.
          </p>
          <Button variant="secondary" onClick={refetch}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </Card>
      </div>
    )
  }

  const items = data ?? []

  return (
    <div className="space-y-6">
      {header}

      {items.length === 0 ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <Newspaper className="h-8 w-8 text-slate-300" />
          <p className="font-medium text-slate-600">Hozircha yangilik yo'q</p>
          <p className="max-w-md text-sm text-slate-400">
            Maktab yangilik e'lon qilganda u shu yerda, eng yangisi birinchi bo'lib ko'rinadi.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {items.map((item) => (
            <NewsArticle key={item.id} item={item} />
          ))}
        </div>
      )}
    </div>
  )
}

/**
 * One item. The banner is optional (§3.3 N7) and the whole body is on the
 * page — this is not a card in a tab, so there is nothing to expand.
 */
function NewsArticle({ item }: { item: PortalNewsItem }) {
  return (
    <Card>
      {item.imageUrl && (
        <img
          src={item.imageUrl}
          alt=""
          loading="lazy"
          className="mb-4 max-h-80 w-full rounded-xl object-cover"
        />
      )}
      <h2 className="text-lg font-semibold break-words text-slate-800">{item.title}</h2>
      <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-slate-400">
        <span className="inline-flex items-center gap-1.5">
          <Newspaper className="h-3.5 w-3.5" />
          {formatDate(item.publishedAt)}
        </span>
        {item.authorName && (
          <span className="inline-flex items-center gap-1.5">
            <UserRound className="h-3.5 w-3.5" />
            {item.authorName}
          </span>
        )}
      </p>
      {/* §3.3 N2 — plain text; only the line breaks are preserved. */}
      <p className="mt-3 text-sm leading-relaxed break-words whitespace-pre-line text-slate-600">
        {item.body}
      </p>
    </Card>
  )
}
