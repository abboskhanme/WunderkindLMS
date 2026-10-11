/**
 * Formatlangan matn — Telegram ilovasidagi belgilar (mijoz, 2026-10-11: "bold, kursiv va boshqa
 * funksiyalar"). Serverdagi `TelegramHtml.FromMarkup` ning nusxasi: Telegramga aynan shu ko'rinish
 * ketadi, shuning uchun ekrandagi namuna bilan bir xil bo'lishi kerak.
 *
 *   **qalin**  __kursiv__  ++tagiga chizilgan++  ~~ustidan chizilgan~~  ||spoiler||
 *   `kod`  ```kod bloki```  [matn](https://havola)  "> " bilan boshlangan qator — iqtibos
 *
 * Avval HAMMA matn ekranlanadi, keyin faqat shu belgilar tegga aylanadi — natijani
 * `dangerouslySetInnerHTML` ga berish xavfsiz (atribut yo'q, faqat http/https havola).
 */

const escape = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

const INLINE: [RegExp, string][] = [
  [/\*\*(?=\S)([\s\S]+?)(?<=\S)\*\*/g, 'b'],
  [/__(?=\S)([\s\S]+?)(?<=\S)__/g, 'i'],
  [/\+\+(?=\S)([\s\S]+?)(?<=\S)\+\+/g, 'u'],
  [/~~(?=\S)([\s\S]+?)(?<=\S)~~/g, 's'],
  [/\|\|(?=\S)([\s\S]+?)(?<=\S)\|\|/g, 'tg-spoiler'],
]

function format(part: string): string {
  let s = escape(part)
  s = s.replace(/\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)/gi, (_m, label: string, url: string) =>
    `<a href="${url.replace(/"/g, '&quot;')}" target="_blank" rel="noopener noreferrer">${label}</a>`)
  for (const [re, tag] of INLINE) s = s.replace(re, (_m, inner: string) => `<${tag}>${inner}</${tag}>`)
  const out: string[] = []
  let quote: string[] = []
  const flush = () => {
    if (quote.length) out.push(`<blockquote>${quote.join('\n')}</blockquote>`)
    quote = []
  }
  for (const line of s.split('\n')) {
    if (line.startsWith('&gt; ')) {
      quote.push(line.slice(5))
      continue
    }
    flush()
    out.push(line)
  }
  flush()
  return out.join('\n')
}

/** Belgilarli matn → xavfsiz HTML (Telegram HTML qismi). */
export function markupToHtml(text: string | null | undefined): string {
  if (!text) return ''
  let html = ''
  let last = 0
  const code = /```([\s\S]+?)```|`([^`\n]+)`/g
  for (let m = code.exec(text); m; m = code.exec(text)) {
    html += format(text.slice(last, m.index))
    html += m[1] !== undefined ? `<pre>${escape(m[1].replace(/^\n+|\n+$/g, ''))}</pre>` : `<code>${escape(m[2])}</code>`
    last = m.index + m[0].length
  }
  return html + format(text.slice(last))
}

/** Belgilarsiz oddiy matn (qisqa ko'rinishlar uchun). */
export function markupToPlain(text: string | null | undefined): string {
  const div = document.createElement('div')
  div.innerHTML = markupToHtml(text)
  return div.textContent ?? ''
}

/** HTML ichiga qo'yiladigan qiymat (o'rinbosar: ism, sinf …). */
export const escapeHtml = escape
