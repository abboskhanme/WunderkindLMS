/**
 * Formatted news text (2026-10-11): the server sends `bodyHtml` already sanitized to Telegram's
 * HTML subset (TelegramHtml — every other character escaped), so injecting it is safe. Old items
 * without it fall back to the plain body.
 */
export function RichBody({ html, text, className = '' }) {
  if (!html) return <p className={`whitespace-pre-line break-words ${className}`}>{text}</p>
  return (
    <div
      className={`rich-body whitespace-pre-wrap break-words ${className}`}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  )
}
