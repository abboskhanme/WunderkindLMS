/**
 * MAKTAB YANGILIKLARI — the teacher panel's news section (SM-11, §3.4).
 *
 * The same card as the parent panel, one audience down: `for_employee`
 * (§5.5 `GET /api/tg/teacher/news`). Employees read school news here and in
 * the Telegram message the publish step sends — the teacher PWA does not get
 * a feed in v1 (§3.4).
 *
 * WHY IT FETCHES ITS OWN DATA. The Bugun tab builds today's lessons out of
 * several dependent requests and a failure there is the tab's error state.
 * News must not be able to break that screen, and a broken schedule must not
 * hide the news, so this card owns its request and its three states.
 *
 * THE BODY IS PLAIN TEXT (§3.3 N2): `whitespace-pre-line`, no markdown, no
 * HTML, no `dangerouslySetInnerHTML`.
 *
 * NOT SHARED WITH THE PARENT CARD ON PURPOSE. The two panels keep separate
 * screen folders and separate API layers (`parentApi` / `teacherApi`); a
 * shared card would have to reach across both, and §3.4 lists one file per
 * panel.
 */
import { useState } from 'react'
import { ChevronDown, Newspaper } from 'lucide-react'
import { Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dateTime } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { teacherApi } from '../../lib/teacherApi'
import { AsyncBlock } from './shared'

/** Items shown before `Barchasi` is tapped (§3.4). */
const PREVIEW = 3

export function NewsCard() {
  const query = useAsync(() => teacherApi.news(), [])
  const [expanded, setExpanded] = useState(false)
  // One open item at a time — two expanded bodies in a narrow webview are a
  // wall of text between the teacher and the next lesson.
  const [openId, setOpenId] = useState(null)

  const items = query.data ?? []
  const shown = expanded ? items : items.slice(0, PREVIEW)

  return (
    <Card
      title="Maktab yangiliklari"
      action={
        items.length > PREVIEW ? (
          <button
            type="button"
            onClick={() => {
              haptic('light')
              setExpanded((v) => !v)
            }}
            className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
          >
            {expanded ? 'Kamroq' : 'Barchasi'}
          </button>
        ) : null
      }
    >
      <AsyncBlock
        query={query}
        loadingLabel="Yangiliklar yuklanmoqda…"
        isEmpty={(rows) => rows.length === 0}
        empty={
          <EmptyState
            icon={<Newspaper className="h-8 w-8" />}
            title="Hozircha yangilik yo'q"
            note="Ma'muriyat yangilik e'lon qilsa, shu yerda ko'rinadi."
          />
        }
      >
        {() =>
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
        }
      </AsyncBlock>
    </Card>
  )
}

/**
 * One item: the title is always visible, the body opens on tap.
 *
 * The title wraps instead of truncating — the webview is narrow and half a
 * headline is not a reason to tap.
 */
function NewsItem({ item, open, onToggle }) {
  // The banner is optional (§3.3 N7) and the file behind it may be gone; a
  // broken image icon inside an announcement reads as a fault.
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
