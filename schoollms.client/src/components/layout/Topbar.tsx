import { useCallback, useEffect, useRef, useState } from 'react'
import { ChevronDown, LogOut, PanelLeftClose, PanelLeftOpen, Settings } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { NotificationsBell } from './NotificationsBell'
import { GlobalSearch } from './GlobalSearch'
import { Modal } from '@/components/ui/Modal'
import { UserAvatar } from '@/components/ui/UserAvatar'
import { Toast } from '@/components/ui/Toast'
import { AccountSettings } from '@/pages/admin/account/AccountSettings'

interface TopbarProps {
  /** Yon menyu hozir ochiqmi — tugma belgisi shunga qarab o'zgaradi. */
  sidebarOpen: boolean
  onMenuClick: () => void
}

export function Topbar({ sidebarOpen, onMenuClick }: TopbarProps) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  // Akkaunt sozlamalari — alohida sahifa emas, modal oyna (mijoz, 2026-09-22).
  const [accountOpen, setAccountOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])
  const closeAccount = useCallback(() => setAccountOpen(false), [])

  // Tashqariga bosilganda yoki Escape bosilganda profil menyusini yopamiz
  useEffect(() => {
    if (!menuOpen) return
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMenuOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen])

  if (!user) return null

  const handleLogout = () => {
    setMenuOpen(false)
    logout()
    navigate('/login', { replace: true })
  }

  const openAccount = () => {
    setMenuOpen(false)
    setAccountOpen(true)
  }


  return (
    <header className="flex h-16 items-center justify-between border-b border-slate-200 bg-white px-4 sm:px-6">
      {/* "Xush kelibsiz" yozuvi o'rniga — keng qidiruv maydoni (mijoz, 2026-09-22). */}
      <div className="flex min-w-0 flex-1 items-center gap-3">
        <button
          onClick={onMenuClick}
          className="rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100"
          title={sidebarOpen ? 'Menyuni yig\'ish' : 'Menyuni ochish'}
          aria-label={sidebarOpen ? 'Menyuni yig\'ish' : 'Menyuni ochish'}
          aria-expanded={sidebarOpen}
        >
          {/* Hamburger emas: bu tugma yon panelni yig'adi/ochadi, belgisi ham shuni ko'rsatadi. */}
          {sidebarOpen ? <PanelLeftClose className="h-5 w-5" /> : <PanelLeftOpen className="h-5 w-5" />}
        </button>
        <GlobalSearch />
      </div>

      <div className="ml-3 flex items-center gap-3 sm:gap-4">
        <NotificationsBell />

        {/* Profil — bosilganda akkaunt sozlamalari/chiqish menyusi ochiladi */}
        <div className="relative" ref={menuRef}>
          <button
            onClick={() => setMenuOpen((o) => !o)}
            className="flex items-center gap-2.5 rounded-lg p-1 transition-colors hover:bg-slate-50"
            title="Profil"
            aria-haspopup="menu"
            aria-expanded={menuOpen}
          >
            <UserAvatar fullName={user.fullName} avatarUrl={user.avatarUrl} className="h-9 w-9 text-sm" />
            <div className="hidden text-left leading-tight sm:block">
              <p className="text-sm font-medium text-slate-700">{user.fullName}</p>
            </div>
            <ChevronDown
              className={`hidden h-4 w-4 text-slate-400 transition-transform sm:block ${
                menuOpen ? 'rotate-180' : ''
              }`}
            />
          </button>

          {menuOpen && (
            <div
              role="menu"
              className="absolute right-0 top-full z-40 mt-2 w-56 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg"
            >
              <div className="border-b border-slate-100 px-4 py-3">
                <p className="truncate text-sm font-medium text-slate-700">{user.fullName}</p>
                {user.email && <p className="truncate text-xs text-slate-400">{user.email}</p>}
              </div>
              <button
                role="menuitem"
                onClick={openAccount}
                className="flex w-full items-center gap-2 px-4 py-2.5 text-sm text-slate-700 transition-colors hover:bg-slate-50"
              >
                <Settings className="h-4 w-4 text-slate-400" />
                Akkaunt sozlamalari
              </button>
              <button
                role="menuitem"
                onClick={handleLogout}
                className="flex w-full items-center gap-2 border-t border-slate-100 px-4 py-2.5 text-sm text-red-600 transition-colors hover:bg-red-50"
              >
                <LogOut className="h-4 w-4" />
                Chiqish
              </button>
            </div>
          )}
        </div>
      </div>
      <Modal open={accountOpen} onClose={closeAccount} title="Akkaunt sozlamalari" size="sm">
        <AccountSettings
          bare
          onSaved={() => {
            setAccountOpen(false)
            setToast('Akkaunt ma\'lumotlari saqlandi')
          }}
        />
      </Modal>
      <Toast message={toast} onClose={closeToast} />
    </header>
  )
}
