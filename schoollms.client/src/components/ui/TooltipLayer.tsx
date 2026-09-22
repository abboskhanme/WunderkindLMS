/**
 * Butun ilova uchun bitta tooltip (mijoz, 2026-09-23: "buttonlar ustiga olib borsa
 * nimaligi ko'rinsin").
 *
 * NEGA HAR TUGMAGA KOMPONENT EMAS: ilovada yuzlab tugmada `title` allaqachon yozilgan.
 * Brauzerning o'z izohi esa ~1 soniya kechikadi, kichik va xira. Bu qatlam `title` li
 * tugma/havola ustiga borilganda o'sha matnni darhol, bir xil ko'rinishda ko'rsatadi —
 * yangi tugmaga ham faqat `title` yozish kifoya.
 *
 * Brauzer izohi ikki marta chiqmasin deb `title` vaqtincha `data-tip` ga ko'chiriladi va
 * sichqoncha ketganda qaytariladi. Ekran o'quvchilar uchun `aria-label` bo'lmasa — qo'yiladi.
 */
import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

const SELECTOR = 'button, a, [role="button"], [role="switch"], [role="menuitem"], label'
const SHOW_DELAY_MS = 150

interface Tip {
  text: string
  x: number
  y: number
  above: boolean
}

export function TooltipLayer() {
  const [tip, setTip] = useState<Tip | null>(null)
  const current = useRef<HTMLElement | null>(null)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => {
    const restore = () => {
      const el = current.current
      if (el?.dataset.tip != null) {
        // React shu orada yangi `title` qo'ygan bo'lsa (holat almashdi) — o'shani qoldiramiz,
        // eskisini ustiga yozmaymiz.
        if (!el.hasAttribute('title')) el.setAttribute('title', el.dataset.tip)
        delete el.dataset.tip
      }
      current.current = null
      window.clearTimeout(timer.current)
      setTip(null)
    }

    const onOver = (e: MouseEvent) => {
      const el = (e.target as Element | null)?.closest<HTMLElement>(SELECTOR)
      if (!el || el === current.current) return
      restore()
      const text = el.getAttribute('title')?.trim()
      if (!text) return
      // Brauzer izohini o'chiramiz, matnni saqlab qolamiz.
      el.dataset.tip = text
      el.removeAttribute('title')
      if (!el.hasAttribute('aria-label') && !el.textContent?.trim()) el.setAttribute('aria-label', text)
      current.current = el
      timer.current = window.setTimeout(() => {
        const r = el.getBoundingClientRect()
        const above = r.bottom + 40 > window.innerHeight
        setTip({ text, x: r.left + r.width / 2, y: above ? r.top - 6 : r.bottom + 6, above })
      }, SHOW_DELAY_MS)
    }

    const onOut = (e: MouseEvent) => {
      const el = current.current
      if (el && !el.contains(e.relatedTarget as Node | null)) restore()
    }

    document.addEventListener('mouseover', onOver)
    document.addEventListener('mouseout', onOut)
    document.addEventListener('mousedown', restore)
    window.addEventListener('scroll', restore, true)
    return () => {
      restore()
      document.removeEventListener('mouseover', onOver)
      document.removeEventListener('mouseout', onOut)
      document.removeEventListener('mousedown', restore)
      window.removeEventListener('scroll', restore, true)
    }
  }, [])

  if (!tip) return null

  // Ekran chetidan chiqib ketmasin: markaz 8px chegaradan ichkarida.
  const x = Math.min(Math.max(tip.x, 90), window.innerWidth - 90)

  return createPortal(
    <div
      role="tooltip"
      className="pointer-events-none fixed z-[100] max-w-[16rem] -translate-x-1/2 rounded-lg bg-slate-900/95 px-2.5 py-1.5 text-center text-xs font-medium leading-snug text-white shadow-lg"
      style={{ left: x, top: tip.y, transform: `translate(-50%, ${tip.above ? '-100%' : '0'})` }}
    >
      {tip.text}
    </div>,
    document.body,
  )
}
