/**
 * VAZIFALAR — o'qituvchi beradigan topshiriqlar (2026-10-12: "o'quvchilar uchun alohida"; HR'ning
 * "Topshiriqlar" tabi xodimlarniki). O'qituvchi paneli ham, o'quvchi/ota-ona paneli ham shu
 * yorliqlar va muddat qoidasidan foydalanadi.
 */
import { CheckCircle2, FileText, Paperclip, PenLine, Video } from 'lucide-react'
import { todayISO } from './weeks'

/** Formatlar — backend `Assignment.Format` qiymatlari (eski veb-ilova bilan bir xil). */
export const FORMATS = [
  { value: 'test', label: 'Test', note: 'Variantli savollar', icon: CheckCircle2 },
  { value: 'written', label: 'Yozma', note: 'Matn javob', icon: PenLine },
  { value: 'file', label: 'Fayl', note: 'Fayl yuklaydi', icon: Paperclip },
  { value: 'video', label: 'Video', note: 'Video yoki havola', icon: Video },
]

const OTHER = { value: '', label: 'Vazifa', note: '', icon: FileText }

/** `text` — eski yozuvlardagi "written" ning nomi. */
export function formatOf(value) {
  return FORMATS.find((f) => f.value === (value === 'text' ? 'written' : value)) ?? OTHER
}

/** "2026-10-12" yoki "2026-10-12T…" → "2026-10-12". */
export const isoDay = (value) => (value ? String(value).slice(0, 10) : '')

/** Kunlar farqi: muddat − bugun (manfiy — o'tib ketgan). Muddat yo'q bo'lsa null. */
export function daysLeft(dueDate, today = todayISO()) {
  const due = isoDay(dueDate)
  if (!due) return null
  const ms = Date.parse(`${due}T00:00:00`) - Date.parse(`${today}T00:00:00`)
  return Number.isNaN(ms) ? null : Math.round(ms / 86_400_000)
}

/** Muddat yozuvi va rangi: `{ text, tone }` (tone — Badge tonlari). */
export function dueInfo(dueDate) {
  const d = daysLeft(dueDate)
  if (d === null) return { text: "Muddat yo'q", tone: 'neutral' }
  if (d > 1) return { text: `${d} kun qoldi`, tone: 'neutral' }
  if (d === 1) return { text: 'Ertaga tugaydi', tone: 'brand' }
  if (d === 0) return { text: 'Bugun tugaydi', tone: 'brand' }
  return { text: `Muddati o'tgan (${-d} kun)`, tone: 'danger' }
}

/** Fayl nomi URL'dan (`/uploads/abc_name.pdf` → `abc_name.pdf`). */
export function fileNameOf(url) {
  if (!url) return ''
  try {
    return decodeURIComponent(String(url).split('/').pop() || 'fayl')
  } catch {
    return String(url).split('/').pop() || 'fayl'
  }
}

/** Havola (http…) — video formatida o'quvchi fayl o'rniga havola beradi. */
export const isLink = (value) => /^https?:\/\//i.test(String(value || ''))

/**
 * Ochiladigan manzil yoki null. O'quvchi yozgan havola `javascript:` bo'lishi mumkin — faqat
 * http(s) va saytning o'z yo'llari (`/uploads/…`) havola bo'ladi.
 */
export const safeHref = (value) => (/^(https?:\/\/|\/(?!\/))/i.test(String(value || '')) ? value : null)

/* ------------------------------------- bulk import of test questions (2026-10-15, test-import.md) */

/** Questions per test — the server's `AssignmentService.MaxQuestions`. */
export const MAX_TEST_QUESTIONS = 200

/** The server's reasons (`AssignmentQuestionReason`) — the import's error chips. */
const IMPORT_REASONS = {
  emptyText: "Savol matni bo'sh",
  tooFewOptions: 'Variant 2 tadan kam',
  tooManyOptions: "Variant 6 tadan ko'p",
  noCorrect: "To'g'ri javob belgilanmagan",
  badCorrect: "To'g'ri javob variantlarda yo'q",
  manyCorrect: "Bir nechta to'g'ri javob",
  duplicateOption: 'Bir xil variant',
}

export const importReasonLabel = (reason) => IMPORT_REASONS[reason] ?? reason

/** "2, 5, 9 … (+12)" — a long list stays on a line or two. */
export function importRowsText(rows, shown = 20) {
  const sorted = [...rows].sort((a, b) => a - b)
  const head = sorted.slice(0, shown).join(', ')
  return sorted.length > shown ? `${head} … (+${sorted.length - shown})` : head
}

/** "7-A" → "7"; no leading number — empty. */
export const gradeOf = (className) => /^\s*(\d{1,2})/.exec(String(className || ''))?.[1] ?? ''

/**
 * The ready prompt for ChatGPT / Gemini — its answer comes back in the import's text format.
 * An unknown value stays as a [placeholder] for the teacher to fill in the AI window.
 */
export function aiQuestionPrompt({ grade, subject, topic, count }) {
  const g = grade ? `${grade}-sinf` : '[sinf]-sinf'
  const s = subject ? `«${subject}»` : '«[fan]»'
  const t = topic?.trim() ? `«${topic.trim()}» mavzusida ` : '[mavzu] mavzusida '
  const n = Number(count) > 0 ? Math.min(Number(count), MAX_TEST_QUESTIONS) : 10
  return [
    `${g} o'quvchilari uchun ${s} fanidan ${t}${n} ta test savoli tuz.`,
    "Har bir savolda 4 ta javob varianti bo'lsin, ulardan faqat bittasi to'g'ri.",
    '',
    "Javobni FAQAT quyidagi ko'rinishda, oddiy matn bilan yoz:",
    '',
    '1. Savol matni',
    'A) variant',
    "*B) to'g'ri variant",
    'C) variant',
    'D) variant',
    '',
    '2. Keyingi savol',
    '…',
    '',
    'Qoidalar:',
    "- To'g'ri variant harfi oldiga * belgisini qo'y (har savolda faqat bittasiga).",
    "- Savollar orasida bitta bo'sh qator qoldir.",
    "- Markdown, jadval, izoh, tushuntirish, kirish va xulosa so'zlarini yozma.",
  ].join('\n')
}

/** Copies text; when the Telegram WebView has no clipboard API — the old way. `true` = copied. */
export async function copyText(text) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    /* fall through to the old way */
  }
  try {
    const area = document.createElement('textarea')
    area.value = text
    area.setAttribute('readonly', '')
    area.style.position = 'fixed'
    area.style.opacity = '0'
    document.body.appendChild(area)
    area.select()
    const ok = document.execCommand('copy')
    area.remove()
    return ok
  } catch {
    return false
  }
}
