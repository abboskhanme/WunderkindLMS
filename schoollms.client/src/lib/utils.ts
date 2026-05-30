/** Tailwind class'larni shartli birlashtirish */
export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

/** Mock API kechikishini simulyatsiya qilish */
export function delay(ms = 500): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** ISO sanani DD.MM.YYYY ko'rinishida chiqarish */
export function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}.${mm}.${d.getFullYear()}`
}

/** Ma'lumotlarni CSV faylga yuklab olish (Excel uchun UTF-8 BOM bilan) */
export function exportToCsv(filename: string, headers: string[], rows: string[][]): void {
  const escape = (v: string) => `"${String(v).replace(/"/g, '""')}"`
  const csv = [headers, ...rows].map((r) => r.map(escape).join(',')).join('\r\n')
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

/** Oddiy noyob ID */
export function uid(): string {
  return crypto.randomUUID()
}

/** Pulni "850 000 so'm" ko'rinishida formatlash */
export function formatMoney(n: number): string {
  return `${new Intl.NumberFormat('ru-RU').format(n)} so'm`
}

/** Tasodifiy parol (chalkashtirmaydigan belgilardan — 0/O, 1/l/I yo'q) */
export function randomPassword(length = 6): string {
  const alphabet = 'abcdefghijkmnpqrstuvwxyz23456789'
  const bytes = new Uint8Array(length)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('')
}

/** Matnni clipboardga nusxalash (xatoni yutadi) */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}
