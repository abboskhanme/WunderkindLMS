/**
 * Matn maydoni ustidagi formatlash tugmalari (mijoz, 2026-10-11: "Matn yozishda bold, kursiv va
 * boshqa funksiyalarni qo'shish"). Tanlangan matnni Telegram belgilari bilan o'raydi (tanlanmagan
 * bo'lsa — kursor joyiga belgi qo'yib, ichiga kursorni qo'yadi). Belgilar ro'yxati — `telegramMarkup`.
 */
import type { RefObject } from 'react'
import { Bold, Code, EyeOff, Italic, Link2, Quote, Strikethrough, Underline } from 'lucide-react'
import { cn } from '@/lib/utils'

type Action = { key: string; title: string; icon: typeof Bold; before: string; after: string; line?: boolean }

const ACTIONS: Action[] = [
  { key: 'b', title: 'Qalin — **matn**', icon: Bold, before: '**', after: '**' },
  { key: 'i', title: 'Kursiv — __matn__', icon: Italic, before: '__', after: '__' },
  { key: 'u', title: 'Tagiga chizilgan — ++matn++', icon: Underline, before: '++', after: '++' },
  { key: 's', title: 'Ustidan chizilgan — ~~matn~~', icon: Strikethrough, before: '~~', after: '~~' },
  { key: 'spoiler', title: "Yashirin (bosilsa ochiladi) — ||matn||", icon: EyeOff, before: '||', after: '||' },
  { key: 'code', title: 'Kod — `matn`', icon: Code, before: '`', after: '`' },
  { key: 'quote', title: 'Iqtibos — qator boshida "> "', icon: Quote, before: '> ', after: '', line: true },
  { key: 'link', title: 'Havola — [matn](https://…)', icon: Link2, before: '[', after: '](https://)' },
]

export function FormatToolbar({
  textareaRef,
  value,
  onChange,
  className,
}: {
  textareaRef: RefObject<HTMLTextAreaElement | null>
  value: string
  onChange: (next: string) => void
  className?: string
}) {
  const apply = (a: Action) => {
    const el = textareaRef.current
    const start = el?.selectionStart ?? value.length
    const end = el?.selectionEnd ?? value.length
    let next: string
    let selStart: number
    let selEnd: number
    if (a.line) {
      // Tanlangan har bir qator boshiga "> ".
      const lineStart = value.lastIndexOf('\n', start - 1) + 1
      const block = value.slice(lineStart, end)
      const quoted = block.split('\n').map((l) => (l.startsWith('> ') ? l : `> ${l}`)).join('\n')
      next = value.slice(0, lineStart) + quoted + value.slice(end)
      selStart = lineStart
      selEnd = lineStart + quoted.length
    } else {
      const picked = value.slice(start, end)
      next = value.slice(0, start) + a.before + picked + a.after + value.slice(end)
      if (a.key === 'link' && picked) {
        // Havolada kursor "https://" ning oxiriga — manzilni darrov yozish uchun.
        selStart = selEnd = start + a.before.length + picked.length + a.after.length - 1
      } else {
        selStart = start + a.before.length
        selEnd = selStart + picked.length
      }
    }
    onChange(next)
    requestAnimationFrame(() => {
      el?.focus()
      el?.setSelectionRange(selStart, selEnd)
    })
  }

  return (
    <div className={cn('flex flex-wrap items-center gap-0.5 rounded-lg border border-slate-200 bg-slate-50 p-1', className)}>
      {ACTIONS.map((a) => (
        <button
          key={a.key}
          type="button"
          title={a.title}
          aria-label={a.title}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => apply(a)}
          className="rounded-md p-1.5 text-slate-500 transition-colors hover:bg-white hover:text-slate-800"
        >
          <a.icon className="h-4 w-4" />
        </button>
      ))}
    </div>
  )
}
