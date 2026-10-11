/**
 * Formatlangan matnni chizadi. `markup` — foydalanuvchi yozgan belgilarli matn (klientda
 * aylantiriladi); `html` — server tozalab bergan HTML (`TelegramHtml`). Ikkalasi ham faqat ruxsat
 * etilgan teglardan iborat, shuning uchun innerHTML xavfsiz.
 */
import { markupToHtml } from '@/lib/telegramMarkup'
import { cn } from '@/lib/utils'

export function RichText({ markup, html, className }: { markup?: string | null; html?: string | null; className?: string }) {
  return (
    <div
      className={cn(
        'whitespace-pre-wrap break-words',
        '[&_a]:text-brand-600 [&_a]:underline [&_b]:font-semibold [&_strong]:font-semibold',
        '[&_code]:rounded [&_code]:bg-slate-100 [&_code]:px-1 [&_code]:font-mono [&_code]:text-[0.9em]',
        '[&_pre]:my-1 [&_pre]:overflow-x-auto [&_pre]:rounded-lg [&_pre]:bg-slate-100 [&_pre]:p-2 [&_pre]:font-mono [&_pre]:text-[0.9em]',
        '[&_blockquote]:my-1 [&_blockquote]:border-l-4 [&_blockquote]:border-slate-200 [&_blockquote]:pl-3 [&_blockquote]:text-slate-500',
        '[&_tg-spoiler]:rounded [&_tg-spoiler]:bg-slate-300 [&_tg-spoiler]:text-transparent [&_tg-spoiler:hover]:bg-transparent [&_tg-spoiler:hover]:text-inherit',
        className,
      )}
      // Faqat ruxsat etilgan teglar: markupToHtml avval hammasini ekranlaydi, server HTML — TelegramHtml.
      dangerouslySetInnerHTML={{ __html: html ?? markupToHtml(markup) }}
    />
  )
}
