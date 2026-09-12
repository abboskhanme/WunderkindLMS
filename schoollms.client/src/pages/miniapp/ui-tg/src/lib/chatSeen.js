/**
 * "Qayerni o'qib bo'ldim" belgisi — qurilmada.
 *
 * Serverda hozircha o'qilganlik holati yo'q (`chat/last-messages` faqat oxirgi
 * xabar vaqtini beradi). Shuning uchun har kanal uchun oxirgi ko'rilgan xabar
 * vaqtini VA matnini o'zimiz saqlaymiz: vaqt — o'qilmaganni sanash uchun, matn
 * — ro'yxatdagi ko'rinish uchun (hammasi o'qilgach ham oxirgi xabar ko'rinib
 * tursin). Bu bitta qurilma uchun to'g'ri javob beradi; serverda `readAt`
 * paydo bo'lganda shu fayl o'rniga o'sha ishlatiladi.
 *
 * Vaqt satrlari serverdan kelgan ko'rinishda ("o" formati, mintaqasiz)
 * saqlanadi — bir xil formatdagi satrlarni oddiy solishtirish to'g'ri ishlaydi
 * va `Date` orqali mintaqa siljishi yuz bermaydi.
 */

const KEY = 'tg_chat_seen'

/** Yozuvni bir ko'rinishga keltiradi (eski versiya oddiy satr saqlagan bo'lishi mumkin). */
function normalize(record) {
  if (!record) return null
  if (typeof record === 'string') return { at: record, preview: null, author: null }
  return { at: record.at ?? null, preview: record.preview ?? null, author: record.author ?? null }
}

/** { [kanal]: { at, preview, author } } */
export function readSeen() {
  try {
    const raw = localStorage.getItem(KEY)
    const parsed = raw ? JSON.parse(raw) : null
    if (!parsed || typeof parsed !== 'object') return {}
    const out = {}
    for (const [k, v] of Object.entries(parsed)) {
      const rec = normalize(v)
      if (rec) out[k] = rec
    }
    return out
  } catch {
    return {}
  }
}

/** Kanalni shu xabargacha o'qilgan deb belgilaydi (orqaga surilmaydi). */
export function markSeen(channel, at, preview, author) {
  if (!channel || !at) return
  try {
    const map = readSeen()
    const prev = map[channel]
    if (prev?.at && prev.at >= at) return
    map[channel] = { at, preview: preview ?? null, author: author ?? null }
    localStorage.setItem(KEY, JSON.stringify(map))
  } catch {
    /* private rejim — belgilash saqlanmaydi, ilova ishlayveradi */
  }
}

/** Kanalda ko'rilmagan xabar bormi. */
export function hasUnread(channel, lastMessageAt, seen) {
  if (!lastMessageAt) return false
  const at = seen?.[channel]?.at
  return !at || at < lastMessageAt
}

/** Yangi xabari bor kanallar soni. */
export function unreadChannelCount(lastMessages, seen) {
  return Object.entries(lastMessages || {}).filter(([name, iso]) => hasUnread(name, iso, seen)).length
}
