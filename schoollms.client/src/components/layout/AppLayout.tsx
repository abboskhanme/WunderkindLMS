import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { UnreadProvider } from '@/context/unread-context'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { CommandPalette } from './CommandPalette'

export function AppLayout() {
  // Desktopda ochiq, mobil ekranda yopiq holatda boshlanadi
  const [open, setOpen] = useState(
    () => typeof window !== 'undefined' && window.innerWidth >= 1024,
  )

  const closeOnMobile = () => {
    if (window.innerWidth < 1024) setOpen(false)
  }

  return (
    <UnreadProvider>
      <CommandPalette />
      <div className="flex h-screen overflow-hidden">
        {/* Mobil uchun fon (orqa qoplama) */}
        {open && (
          <div
            onClick={() => setOpen(false)}
            className="fixed inset-0 z-30 bg-slate-900/40 lg:hidden"
          />
        )}

        <Sidebar open={open} onNavigate={closeOnMobile} />

        <div className="flex flex-1 flex-col overflow-hidden">
          <Topbar onMenuClick={() => setOpen((o) => !o)} />
          <main className="flex-1 overflow-y-auto p-6">
            <Outlet />
          </main>
        </div>
      </div>
    </UnreadProvider>
  )
}
