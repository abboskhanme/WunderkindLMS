/**
 * O'qituvchi tab'lari uchun umumiy mayda qismlar.
 *
 * Bu YANGI dizayn tizimi EMAS — `components/ui.jsx` dan foydalanadi va faqat
 * beshta tabda takrorlanadigan mantiqni (holatlar, dars vaqti, kanal nomi)
 * bir joyga yig'adi.
 */
import { ErrorState, Loader } from '../../components/ui'
import { teacherApi } from '../../lib/teacherApi'
import { hasUnread } from '../../lib/chatSeen'

/**
 * Bitta so'rovning to'rt holati. `useAsync` natijasini bering:
 * yuklanmoqda → Loader, xato → qayta urinish tugmasi bilan ErrorState,
 * bo'sh → chaqiruvchi bergan sabab, aks holda — mazmun.
 */
export function AsyncBlock({ query, loadingLabel, empty, isEmpty, children }) {
  if (query.loading && query.data === null) return <Loader label={loadingLabel} />
  if (query.error) return <ErrorState message={query.error} onRetry={query.reload} />
  if (isEmpty && isEmpty(query.data)) return empty ?? null
  return children(query.data)
}

/** Hozirgi vaqt "HH:mm" ko'rinishida (qurilma soati bo'yicha). */
export function nowHHmm() {
  const d = new Date()
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

/** "HH:mm" → daqiqa. Noto'g'ri qiymat uchun null. */
export function minutesOf(hhmm) {
  if (typeof hhmm !== 'string' || hhmm.length < 4) return null
  const [h, m] = hhmm.split(':').map(Number)
  if (Number.isNaN(h) || Number.isNaN(m)) return null
  return h * 60 + m
}

/**
 * Dars vaqti bo'yicha holat: `past` | `now` | `next` | `later`.
 * `next` ni chaqiruvchi belgilaydi (kunning birinchi kelayotgan darsi).
 */
export function lessonTimeState(lesson, now = nowHHmm()) {
  const s = minutesOf(lesson.startTime)
  const e = minutesOf(lesson.endTime)
  const n = minutesOf(now)
  if (s === null || e === null || n === null) return 'later'
  if (n >= e) return 'past'
  if (n >= s) return 'now'
  return 'later'
}

/** Ketayotgan darsning o'tgan ulushi (0..1) va qolgan daqiqasi. */
export function lessonProgress(lesson, now = nowHHmm()) {
  const s = minutesOf(lesson.startTime)
  const e = minutesOf(lesson.endTime)
  const n = minutesOf(now)
  if (s === null || e === null || n === null) return { fraction: 0, remaining: 0 }
  const total = Math.max(1, e - s)
  return {
    fraction: Math.min(1, Math.max(0, (n - s) / total)),
    remaining: Math.max(0, e - n),
  }
}

/** Chat kanalining ichki kaliti — xodimlar guruhi sinf emas. */
export const STAFF_CHANNEL = '__xodimlar__'

/**
 * O'quv guruhi kanalining prefiksi — to'liq kalit `grp:<guruh id>` (G-17,
 * docs/modules/students-parity.md §2.1.6).
 *
 * Kalit sinf NOMI bo'lgan kanallar bilan to'qnashmasligi uchun shunday: sinf
 * nomi erkin matn, guruh kaliti esa faqat mavjud guruh id'si bilan mos kelardi.
 * Kalitning o'zi ekranda KO'RSATILMAYDI — nomni server `label` sifatida beradi.
 */
export const GROUP_CHANNEL_PREFIX = 'grp:'

export function isGroupChannel(name) {
  return typeof name === 'string' && name.startsWith(GROUP_CHANNEL_PREFIX)
}

/** Kanal kalitini ko'rinadigan nomga aylantiradi (`label` — serverdan). */
export function channelTitle(name, label) {
  if (name === STAFF_CHANNEL) return 'Xodimlar guruhi'
  if (isGroupChannel(name)) return label || "O'quv guruhi"
  return `${label || name} sinf`
}

/** Ro'yxatdagi avatar/kvadrat uchun qisqa belgi. */
export function channelBadge(name, label) {
  if (name === STAFF_CHANNEL) return 'XD'
  if (isGroupChannel(name)) return (label || 'GR').slice(0, 2).toUpperCase()
  return label || name
}

/** Bugungi darslardagi takrorlanmas (sinf, fan) juftliklari. */
export function lessonPairs(lessons) {
  const seen = new Set()
  const pairs = []
  for (const l of lessons) {
    const key = `${l.classId}|${l.subjectId}`
    if (seen.has(key)) continue
    seen.add(key)
    pairs.push({ classId: l.classId, subjectId: l.subjectId })
  }
  return pairs
}

/**
 * Dars satrining sarlavhasi: sinf nomi (bo'linish bo'lsa — guruh raqami bilan)
 * yoki O'QUV GURUHI nomi (§2.1.4 — guruhda sinf ichidagi bo'linish yo'q).
 */
export function lessonOwnerTitle(lesson) {
  if (lesson.ownerKind === 'group') return `Guruh: ${lesson.className}`
  const sub = lesson.subGroup ?? 0
  return `${lesson.className}${sub > 0 ? ` · ${sub}-guruh` : ''}`
}

/** Darsning o'ziga xos kaliti — bir kunda bir sinfda ikki dars bo'lishi mumkin. */
export function lessonKey(lesson) {
  return `${lesson.classId}|${lesson.subjectId}|${lesson.period}|${lesson.subGroup ?? 0}`
}

/**
 * Har kanal uchun: o'qilmaganlar soni va oxirgi xabar.
 *
 * So'rov FAQAT yangi xabari bor kanalga yuboriladi (`?since=` bilan), ya'ni
 * odatdagi kunda bu bir-ikkita yengil so'rov. Bitta so'rov ham sonini, ham
 * matnini beradi — shuning uchun "Bugun" dagi hisob va "Xabarlar" dagi ro'yxat
 * bir xil manbadan chiqadi va hech qachon bir-biriga zid bo'lmaydi.
 *
 * Server `readAt` ni saqlay boshlaganda bu funksiya bitta endpoint chaqiruviga
 * qisqaradi.
 */
export function channelSummaries(channels, lastMessages, seen) {
  // `channels` — `{ key, label, kind }` yoki (eski chaqiruvlar uchun) oddiy kalit satri.
  return Promise.all(
    (channels || []).map(async (channel) => {
      const name = typeof channel === 'string' ? channel : channel.key
      const label = typeof channel === 'string' ? null : channel.label
      const lastAt = lastMessages?.[name] || null
      const mark = seen?.[name] || null
      if (!lastAt) return { name, label, lastAt: null, unread: 0, preview: null, author: null }
      if (!hasUnread(name, lastAt, seen)) {
        return {
          name,
          label,
          lastAt,
          unread: 0,
          preview: mark?.preview ?? null,
          author: mark?.author ?? null,
        }
      }
      const fresh = await teacherApi.chatMessages(name, mark?.at)
      const last = fresh.length ? fresh[fresh.length - 1] : null
      return {
        name,
        label,
        lastAt,
        unread: fresh.length,
        preview: last?.text ?? null,
        author: last?.senderName ?? null,
      }
    }),
  )
}
