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
