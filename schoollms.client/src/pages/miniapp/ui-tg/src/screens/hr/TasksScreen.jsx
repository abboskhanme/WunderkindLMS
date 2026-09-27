/**
 * TOPSHIRIQLAR — tanlangan kundagi topshiriqlar (eski `screens/TasksScreen.tsx`).
 *
 * Xodimning kun davomidagi ish joyi: ro'yxat → "Bajardim" → tayyor. Har amaldan
 * keyin ro'yxat QAYTA SO'RALMAYDI — server yangilangan topshiriqni qaytaradi va u
 * o'rniga qo'yiladi.
 *
 * - OPTIMISTIK: "Bajardim" (faylsiz), "Bekor qilish", "Tasdiqlash" va KPI qadami
 *   kartochkani DARHOL o'zgartiradi; xato bo'lsa eski holat qaytadi va sabab
 *   bannerda yoziladi. Fayl biriktirilgan bo'lsa oyna server tasdig'igacha ochiq
 *   qoladi (yuklangan isbot id'lari yo'qolmasin).
 * - SAKRASH yo'q: birinchi yuklanishda va kun almashganda — kartochka skeleti;
 *   ayni kun yangilanayotganda eski ro'yxat joyida qoladi (xiralashib).
 * - Muddati o'tgan ochiq topshiriqlar ro'yxat BOSHIDA (qolgani — server tartibida).
 *
 * `heroTop` — sariq hero'ning yuqori qismi: xodim panelida "Boshqaruv paneli",
 * o'qituvchi tabida "Topshiriqlar / Natijam" o'tkagichi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarClock, NotebookText, PartyPopper, Plus, SearchX } from 'lucide-react'
import { EmptyState, ErrorState, Hero, Screen } from '../../components/ui'
import { hrApi, hrErrorMessage } from '../../lib/hrApi'
import { dateHeadline, dayMonthLabel, diffDays, instantDate, toNumber, todayIn } from '../../lib/hrFormat'
import { haptic, hapticNotify } from '../../lib/telegram'
import { CompleteSheet } from './CompleteSheet'
import { DateStrip } from './DateStrip'
import { NewTaskSheet } from './NewTaskSheet'
import { TaskCard } from './TaskCard'
import { Btn, LightSegmented, ListSkeleton, Notice, useOnline } from './shared'
import { t } from './texts'

const FILTERS = [
  { value: 'all', label: t('tasks.filter_all') },
  { value: 'open', label: t('tasks.filter_open') },
  { value: 'done', label: t('tasks.filter_done') },
]

const isOpenStatus = (task) => task.status === 'open' || task.status === 'returned'
const isUrgent = (task) => task.isOverdue === true && isOpenStatus(task)

export function TasksScreen({ me, heroTop }) {
  const timezone = me.timezone || 'Asia/Tashkent'
  // "Bugun" — SERVER bo'yicha (telefon soati adashgan bo'lishi mumkin); bo'lmasa mintaqadan.
  const today = typeof me.today === 'string' && me.today.length >= 10 ? me.today.slice(0, 10) : todayIn(timezone)
  const [date, setDate] = useState(today)
  const [list, setList] = useState(null) // { date, items }
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(null)
  const [notice, setNotice] = useState(null) // { tone, text }
  const [sheet, setSheet] = useState(null) // { kind: 'complete', task, comment, error } | { kind: 'new' }
  const [filter, setFilter] = useState('all')
  const [reloadToken, setReloadToken] = useState(0)
  const online = useOnline()

  useEffect(() => {
    let alive = true
    setLoading(true)
    setLoadError(null)
    hrApi
      .tasks(date)
      .then((data) => {
        if (!alive) return
        // Sana — SO'RALGANI: ro'yxat qaysi kunniki ekanini o'zimiz bilamiz.
        setList({ date, items: Array.isArray(data?.items) ? data.items : [] })
        setLoading(false)
      })
      .catch((error) => {
        // Sana almashganda oldingi so'rov javobi e'tiborsiz qoladi — bu xato emas.
        if (!alive) return
        setList(null)
        setLoadError(error)
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [date, reloadToken])

  // Muddati o'tgan ochiqlar — boshida; qolgani server tartibida (barqaror saralash).
  const items = useMemo(() => {
    const source = list?.items ?? []
    return source
      .map((task, index) => ({ task, index }))
      .sort((a, b) => (isUrgent(b.task) ? 1 : 0) - (isUrgent(a.task) ? 1 : 0) || a.index - b.index)
      .map((x) => x.task)
  }, [list])

  // Bekor qilinganlar yakunda hisoblanmaydi — ular "bajarilmagan" emas.
  const counted = items.filter((task) => task.status !== 'cancelled')
  const doneCount = counted.filter((task) => task.status === 'done').length

  const shown = useMemo(() => {
    if (filter === 'open') return items.filter(isOpenStatus)
    if (filter === 'done') return items.filter((task) => task.status === 'done')
    return items
  }, [items, filter])

  // Ro'yxat HALI BOSHQA KUNNIKI bo'lsa — skelet (kechagi ro'yxat bugungi sarlavha ostida — yolg'on).
  const showSkeleton = loading && (list === null || list.date !== date)
  const refreshing = loading && !showSkeleton

  const patch = (updated) => {
    if (!updated || !updated.id) return
    setList((current) =>
      current === null
        ? current
        : { ...current, items: current.items.map((item) => (item.id === updated.id ? updated : item)) },
    )
  }

  const handleProgress = async (task, amount, note) => {
    const previous = task
    patch(predictProgress(task, amount))
    try {
      const updated = await hrApi.progress(task.id, { amount, note })
      patch(updated)
      // KPI maqsadga yetganda server topshiriqni O'ZI yopadi — faqat xodimga aytamiz.
      if (task.status !== 'done' && updated?.status === 'done') {
        setNotice({ tone: 'ok', text: t('kpi.auto_done') })
        hapticNotify('success')
      } else {
        setNotice(null)
        haptic('light')
      }
    } catch (error) {
      patch(previous)
      setNotice({ tone: 'danger', text: hrErrorMessage(error) })
      hapticNotify('error')
      throw error
    }
  }

  const handleAccept = async (task) => {
    const previous = task
    patch(predictAccepted(task))
    try {
      patch(await hrApi.accept(task.id))
      setNotice({ tone: 'ok', text: t('tasks.accepted') })
      hapticNotify('success')
    } catch (error) {
      patch(previous)
      setNotice({ tone: 'danger', text: hrErrorMessage(error) })
      hapticNotify('error')
      throw error
    }
  }

  const handleReopen = async (task) => {
    const previous = task
    patch(predictOpen(task))
    try {
      patch(await hrApi.reopen(task.id))
      setNotice(null)
      haptic('light')
    } catch (error) {
      patch(previous)
      setNotice({ tone: 'danger', text: hrErrorMessage(error) })
      hapticNotify('error')
      throw error
    }
  }

  /**
   * "Bajardim". Faylsiz — optimistik: oyna darhol yopiladi, xato bo'lsa kartochka
   * qaytadi va oyna AYNI izoh bilan qayta ochiladi. Fayl bilan — server tasdig'igacha
   * oyna ochiq (u yerda yuklangan isbot id'lari saqlanadi).
   */
  const handleCompleteSubmit = async (task, input, hasFiles) => {
    if (hasFiles) {
      patch(await hrApi.complete(task.id, input))
      setSheet(null)
      hapticNotify('success')
      return
    }
    const previous = task
    setSheet(null)
    patch(predictDone(task, input.comment))
    hapticNotify('success')
    try {
      patch(await hrApi.complete(task.id, input))
    } catch (error) {
      patch(previous)
      hapticNotify('error')
      setSheet({ kind: 'complete', task: previous, comment: input.comment ?? '', error: hrErrorMessage(error) })
      throw error
    }
  }

  const handleCreated = (created) => {
    setSheet(null)
    const createdDate = instantDate(created?.dueAt, timezone) ?? date
    if (createdDate === date) {
      setList((current) =>
        current === null ? { date, items: [created] } : { ...current, items: [...current.items, created] },
      )
      return
    }
    // Boshqa kunga qo'yilgan — o'sha kunni ochamiz, aks holda "qo'shdim, ko'rinmayapti".
    setDate(createdDate)
  }

  const closeSheet = useCallback(() => setSheet(null), [])

  const headline = dateHeadline(date, today, {
    today: t('date.today'),
    yesterday: t('date.yesterday'),
    tomorrow: t('date.tomorrow'),
  })

  // Kelajak kuni uchun "bajarildi" sanog'i YOZILMAYDI — xodimga orqada qolgandek tuyulardi.
  const kind = dayKind(date, today)
  const summaryLine = showSkeleton
    ? t('tasks.loading_list')
    : kind === 'today'
      ? t('tasks.summary', { total: counted.length, done: doneCount })
      : kind === 'future'
        ? t('tasks.summary_future', { date: dayMonthLabel(date), total: counted.length })
        : t('tasks.summary_past', { date: dayMonthLabel(date), total: counted.length, done: doneCount })

  return (
    <>
      <Screen>
        <Hero
          title={t('tasks.title')}
          subtitle={[me.fullName, me.positionName].filter(Boolean).join(' · ') || null}
          right={
            <button
              type="button"
              aria-label={t('tasks.add')}
              onClick={() => {
                haptic('light')
                setSheet({ kind: 'new' })
              }}
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink text-brand"
            >
              <Plus className="h-6 w-6" />
            </button>
          }
        >
          {heroTop && <div className="mb-3">{heroTop}</div>}
          {/* Kun sarlavhasi va yakun satri — hero sarlavhasida emas: uzun sana
              ("25-sentabr, chorshanba") 320 px da kesilib qolardi, bu yerda esa o'raladi. */}
          <p className="break-words text-[17px] font-bold leading-tight">{headline}</p>
          <p className="mt-0.5 break-words text-[13px] opacity-70">{summaryLine}</p>
          <div className="mt-2.5">
            <DateStrip selected={date} today={today} onSelect={setDate} />
          </div>
        </Hero>

        <LightSegmented value={filter} options={FILTERS} onChange={setFilter} ariaLabel={t('tasks.filter')} />
        {date !== today && (
          <div className="mx-4 mt-2">
            <Btn variant="light" size="sm" block onClick={() => setDate(today)}>
              {t('date.back_to_today')}
            </Btn>
          </div>
        )}

        {!online && <Notice tone="warn">{t('error.offline_detail')}</Notice>}
        {notice !== null && (
          <Notice tone={notice.tone} onDismiss={() => setNotice(null)}>
            {notice.text}
          </Notice>
        )}

        {showSkeleton && <ListSkeleton label={t('tasks.loading_list')} />}

        {!loading && loadError !== null && (
          <ErrorState message={hrErrorMessage(loadError)} onRetry={() => setReloadToken((v) => v + 1)} />
        )}

        {!showSkeleton && loadError === null && shown.length === 0 && (
          <EmptyState icon={emptyIcon(items.length, kind)} title={emptyText(items.length, kind)} />
        )}

        {shown.length > 0 && (
          <div className={'transition-opacity ' + (refreshing ? 'opacity-60' : '')}>
            {shown.map((task) => (
              <TaskCard
                key={task.id}
                task={task}
                timezone={timezone}
                viewedDate={date}
                isToday={date === today}
                onComplete={(target) => setSheet({ kind: 'complete', task: target, comment: '', error: null })}
                onReopen={handleReopen}
                onAccept={handleAccept}
                onAddProgress={handleProgress}
              />
            ))}
          </div>
        )}

        <div className="h-4" />
      </Screen>

      {sheet?.kind === 'complete' && (
        <CompleteSheet
          key={sheet.task.id}
          task={sheet.task}
          initialComment={sheet.comment}
          initialError={sheet.error}
          onClose={closeSheet}
          onSubmit={(input, hasFiles) => handleCompleteSubmit(sheet.task, input, hasFiles)}
        />
      )}

      {sheet?.kind === 'new' && (
        <NewTaskSheet today={today} timezone={timezone} onClose={closeSheet} onCreated={handleCreated} />
      )}
    </>
  )
}

function dayKind(date, today) {
  const delta = diffDays(date, today)
  if (delta === 0) return 'today'
  return delta > 0 ? 'future' : 'past'
}

function emptyIcon(total, kind) {
  const cls = 'h-10 w-10'
  if (total > 0) return <SearchX className={cls} />
  if (kind === 'today') return <PartyPopper className={cls} />
  return kind === 'future' ? <CalendarClock className={cls} /> : <NotebookText className={cls} />
}

function emptyText(total, kind) {
  if (total > 0) return t('tasks.empty_filter')
  if (kind === 'today') return t('tasks.empty_today')
  return kind === 'future' ? t('tasks.empty_future') : t('tasks.empty_past')
}

/* ------------------------------------------------ optimistik bashoratlar
 * Server javobining O'RNINI BOSMAYDI — javob kelishi bilan topshiriq butunlay
 * almashtiriladi. Vazifasi faqat ko'zga tashlanadigan kechikishni yo'qotish. */

function predictDone(task, comment) {
  return {
    ...task,
    status: 'done',
    employeeComment: comment,
    completedAt: new Date().toISOString(),
    // Muddati o'tganni yopish — "kechikib bajarilgan".
    isLate: task.isOverdue === true,
    isOverdue: false,
  }
}

function predictOpen(task) {
  return { ...task, status: 'open', completedAt: null, isLate: false }
}

/** Tasdiq — holat EMAS: faqat `acceptedAt` to'ladi va tugma yo'qoladi. */
function predictAccepted(task) {
  return { ...task, acceptedAt: new Date().toISOString(), needsAcceptance: false }
}

/** KPI qadami. Holat BASHORAT QILINMAYDI — maqsadga yetganda yopish qarori serverda. */
function predictProgress(task, amount) {
  const added = Number(amount)
  if (!Number.isFinite(added)) return task
  return { ...task, currentAmount: toNumber(task.currentAmount) + added }
}
