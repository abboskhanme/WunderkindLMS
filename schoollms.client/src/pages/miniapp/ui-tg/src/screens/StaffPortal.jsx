/**
 * Xodim, admin va kassir — Telegram orqali kirish (mijoz, 2026-09-25: "xodim uchun telegram orqali ham kirish
 * mumkin bo'lsin").
 *
 * Ular uchun alohida Mini App paneli yo'q — ish joyi veb-panel. Mini App Telegram imzosidan olgan tokenni
 * veb-panel o'qiydigan joyga (`localStorage.token`, bir xil origin) qo'yadi va panelni shu oynaning o'zida
 * ochadi: parol so'ralmaydi, ruxsatlar (rollar) veb-paneldagidek ishlaydi.
 */
import { useEffect } from 'react'
import { LayoutDashboard } from 'lucide-react'
import { tokenStore } from '../lib/api'
import { Hero, Screen } from '../components/ui'

const HOME = { cashier: '/cashier' }

function openPanel(role) {
  const token = tokenStore.get()
  if (token) {
    try {
      localStorage.setItem('token', token)
    } catch {
      /* private rejim — panel login so'raydi */
    }
  }
  window.location.replace(HOME[role] ?? '/admin')
}

export function StaffPortal({ user }) {
  useEffect(() => {
    openPanel(user.role)
  }, [user.role])

  return (
    <Screen>
      <Hero title="Wunderkind" subtitle={user.fullName} />
      <div className="mx-4 mt-4 rounded-card bg-white p-5 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-brand/25">
          <LayoutDashboard className="h-6 w-6 text-brand-ink" />
        </div>
        <p className="mt-3 text-[16px] font-bold">Boshqaruv paneli ochilmoqda…</p>
        <button
          type="button"
          onClick={() => openPanel(user.role)}
          className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink"
        >
          Panelni ochish
        </button>
      </div>
    </Screen>
  )
}
