/**
 * MAKTAB YANGILIKLARI — the parent panel's news section (SM-11, §3.4).
 *
 * WHY IT FETCHES ITS OWN DATA. The Bosh tab loads five requests in one
 * `Promise.all` and one failure takes the whole tab to the error state — the
 * right call for the data that answers "is my child at school". News is not
 * that data: a feed that is empty, slow or unreachable must never hide today's
 * attendance, so it keeps its own `useAsync` and its own three states inside
 * this card.
 *
 * NO SIXTH TAB (§3.4). The panel has no router; the whole feature is this one
 * section — three items, tap one to read it, `Barchasi` for the rest.
 *
 * THE BODY IS PLAIN TEXT (§3.3 N2). It is rendered with `whitespace-pre-line`
 * and nothing else: no markdown, no HTML, no `dangerouslySetInnerHTML`. That
 * decision is what keeps a school announcement from becoming a stored-XSS
 * vector in three different clients.
 */
import { useState } from 'react'
import { ChevronDown, Newspaper } from 'lucide-react'
import { Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dateTime } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getNews } from '../../lib/parentApi'
import { AsyncBlock } from './shared'

/** Items shown before `Barchasi` is tapped (§3.4). */
const PREVIEW = 3

export function NewsCard() {
  const state = useAsync(() => getNews(), [])
  const [expanded, setExpanded] = useState(false)
  // One open item at a time: two expanded bodies in a narrow webview turn
  // the card into a wall of text you have to scroll past.
  const [openId, setOpenId] = useState(null)

  const items = state.data ?? []
  const shown = expanded ? items : items.slice(0, PREVIEW)

  const toggleAll = () => {
    haptic('light')
    setExpanded((v) => !v)
  }

  return (
    <Card
      title="Maktab yangiliklari"
      action={
        items.length > PREVIEW ? (
          <button
            type="button"
            onClick={toggleAll}
            className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
          >
            {expanded ? 'Kamroq' : 'Barchasi'}
          </button>
        ) : null
      }
    >
      <AsyncBlock state={state} loadingLabel="Yangiliklar yuklanmoqda…">
        {(rows) =>
          rows.length === 0 ? (
            <EmptyState
              icon={<Newspaper className="h-8 w-8" />}
              title="Hozircha yangilik yo'q"
              note="Maktab yangilik e'lon qilsa, shu yerda ko'rinadi."
            />
          ) : (
            shown.map((item) => (
              <NewsItem
                key={item.id}
                item={item}
                open={openId === item.id}
                onToggle={() => {
                  haptic('light')
                  setOpenId((id) => (id === item.id ? null : item.id))
                }}
              />
            ))
          )
        }
      </AsyncBlock>
    </Card>
  )
}

/**
 * One item: the title is always visible, the body opens on tap.
 *
 * The title is wrapped, never truncated (`break-words`) — the Telegram webview
 * is narrow, and half a headline is not a reason to tap.
 */
function NewsItem({ item, open, onToggle }) {
  // A banner is optional (§3.3 N7) and the file behind it may be gone; a
  // broken image icon in the middle of an announcement looks like a fault.
  const [imageFailed, setImageFailed] = useState(false)

  return (
    <div className="border-t border-slate-100 first:border-t-0">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={open}
        className="flex w-full items-start gap-3 px-4 py-3 text-left"
      >
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
          <Newspaper className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="break-words text-[15px] font-semibold leading-snug">{item.title}</p>
          <p className="mt-0.5 text-[13px] text-slate-500">
            {[dateTime(item.publishedAt), item.authorName].filter(Boolean).join(' · ')}
          </p>
        </div>
        <ChevronDown
          className={
            'mt-1 h-5 w-5 shrink-0 text-slate-300 transition ' + (open ? 'rotate-180' : '')
          }
        />
      </button>

      {open && (
        <div className="px-4 pb-4">
          {item.imageUrl && !imageFailed && (
            <img
              src={item.imageUrl}
              alt=""
              loading="lazy"
              onError={() => setImageFailed(true)}
              className="mb-3 max-h-60 w-full rounded-2xl object-cover"
            />
          )}
          {/* §3.3 N2 — plain text; only the line breaks are preserved. */}
          <p className="whitespace-pre-line break-words text-[14px] leading-relaxed text-slate-600">
            {item.body}
          </p>
        </div>
      )}
    </div>
  )
}
