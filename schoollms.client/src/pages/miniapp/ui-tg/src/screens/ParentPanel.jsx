/**
 * OTA-ONA PANELI — qobiq.
 *
 * QOBIQ IKKI NARSANI TUTADI, UCHINCHISINI EMAS: qaysi tab ochiq va QAYSI
 * FARZAND tanlangan. Ma'lumot so'rash — tablarning ishi.
 *
 * NEGA FARZAND SHU YERDA. Ota-onaning bir nechta farzandi bo'lishi mumkin.
 * Agar har tab o'zi "qaysi bola" degan holatni saqlasa, ota-ona "Baholar" da
 * kattasini, "To'lov" da kichigini ko'rib turgan holatga tushib qoladi — va
 * buni sezmaydi ham. Tanlov bitta joyda tursa, u umuman yuzaga kelmaydi.
 * Tab almashganda ham tanlov saqlanadi, farzand almashganda esa tablar
 * `key={child.id}` orqali qaytadan yig'iladi: ochilgan oy, tanlangan hafta —
 * hammasi yangi farzandning holatiga tozalanadi.
 */
import { useEffect, useState } from 'react'
import { CalendarDays, GraduationCap, Home, UserRoundX, UtensilsCrossed, Wallet } from 'lucide-react'
import { EmptyState, ErrorState, Hero, Loader, Screen, TabBar } from '../components/ui'
import { useAsync } from '../lib/useAsync'
import { listChildren } from '../lib/parentApi'
import { showBackButton } from '../lib/telegram'
import { ChildSwitcher } from './parent/ChildSwitcher'
import { HomeTab } from './parent/HomeTab'
import { GradesTab } from './parent/GradesTab'
import { ScheduleTab } from './parent/ScheduleTab'
import { FinanceTab } from './parent/FinanceTab'
import { MenuTab } from './parent/MenuTab'

const TABS = [
  { value: 'home', label: 'Bosh', icon: Home },
  { value: 'grades', label: 'Baholar', icon: GraduationCap },
  { value: 'schedule', label: 'Jadval', icon: CalendarDays },
  { value: 'finance', label: "To'lov", icon: Wallet },
  { value: 'menu', label: 'Ovqat', icon: UtensilsCrossed },
]

export function ParentPanel({ user }) {
  const [tab, setTab] = useState('home')
  const [childId, setChildId] = useState(null)

  const state = useAsync(() => listChildren(), [])
  const children = state.data ?? []
  // Tanlanmagan bo'lsa — birinchi farzand. Tanlangani ro'yxatdan chiqib
  // ketsa (arxivlandi, bog'lanish o'chdi) yana birinchisiga qaytamiz: bo'sh
  // ekran ko'rsatgandan ko'ra ishlaydigan ekran ko'rsatgan ma'qul.
  const child = children.find((c) => c.id === childId) ?? children[0] ?? null

  useEffect(() => {
    if (child && child.id !== childId) setChildId(child.id)
  }, [child, childId])

  // Telegramning o'z "orqaga" tugmasi — ichki tabdan Boshga qaytaradi.
  useEffect(() => {
    if (tab === 'home') return undefined
    return showBackButton(() => setTab('home'))
  }, [tab])

  const roleWord = user.role === 'student' ? "O'quvchi" : 'Ota-ona'
  const subtitle = child
    ? [child.className ? `${child.className} sinf` : null, roleWord].filter(Boolean).join(' · ')
    : roleWord

  return (
    <>
      <Screen>
        <Hero title={child?.fullName || user.fullName} subtitle={subtitle}>
          {/* Bitta farzandda `null` — `Hero` bo'sh joy ham qoldirmaydi. */}
          {children.length > 1 ? (
            <ChildSwitcher items={children} value={child?.id} onChange={setChildId} />
          ) : null}
        </Hero>

        {/* Ro'yxat qayta yuklanayotganda ekranni bo'shatmaymiz — eski farzand
            ko'rinib turadi, aks holda har yangilanishda ekran "sakraydi". */}
        {state.loading && !child && <Loader label="Farzandlar yuklanmoqda…" />}

        {!state.loading && state.error && (
          <ErrorState message={state.error} onRetry={state.reload} />
        )}

        {!state.loading && !state.error && !child && (
          <EmptyState
            icon={<UserRoundX className="h-10 w-10" />}
            title="Farzand biriktirilmagan"
            note="Telegram hisobingiz maktabdagi hisobga bog'langan, lekin unga hech qaysi o'quvchi biriktirilmagan. Maktab kotibiyatiga murojaat qiling."
          />
        )}

        {child && tab === 'home' && (
          <HomeTab key={child.id} child={child} onOpenTab={setTab} />
        )}
        {child && tab === 'grades' && <GradesTab key={child.id} child={child} />}
        {child && tab === 'schedule' && <ScheduleTab key={child.id} child={child} />}
        {child && tab === 'finance' && <FinanceTab key={child.id} child={child} />}
        {child && tab === 'menu' && <MenuTab key={child.id} child={child} />}
      </Screen>

      <TabBar tabs={TABS} value={tab} onChange={setTab} />
    </>
  )
}
