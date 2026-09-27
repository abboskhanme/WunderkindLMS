/**
 * XODIM PANELI — HR xodimi bo'lgan staff / admin / superadmin / kassir uchun
 * (hr-tasks.md §8).
 *
 * Eski xodim ilovasidagidek AYNAN ikkita bo'lim: "Topshiriqlar" va "Natijam".
 * Veb-panelda ishlaydiganlar uchun sariq hero'da "Boshqaruv paneli" tugmasi —
 * `StaffPortal` qanday ochsa, xuddi shunday (token veb-panelga uzatiladi).
 *
 * `StaffHome` — kirish nuqtasi: `GET /hr/me` BIR MARTA so'raladi; xodim bo'lsa —
 * shu panel, bo'lmasa (403 `hr_not_employee` / 404) — bugungi `StaffPortal`
 * o'zgarishsiz (darhol veb-panelga o'tadi).
 */
import { useState } from 'react'
import { ChartColumn, LayoutDashboard, ListChecks } from 'lucide-react'
import { ErrorState, Loader, Screen, TabBar } from '../components/ui'
import { LogoutButton } from '../components/LogoutButton'
import { useAsync } from '../lib/useAsync'
import { hrApi, hrErrorMessage } from '../lib/hrApi'
import { haptic } from '../lib/telegram'
import { StaffPortal, openPanel } from './StaffPortal'
import { TasksScreen } from './hr/TasksScreen'
import { SummaryScreen } from './hr/SummaryScreen'
import { t } from './hr/texts'

const TABS = [
  { value: 'tasks', label: t('nav.tasks'), icon: ListChecks },
  { value: 'summary', label: t('nav.summary'), icon: ChartColumn },
]

/** "Boshqaruv paneli" — sariq hero ichida, yonida "Chiqish". */
function PanelBar({ user }) {
  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={() => {
          haptic('light')
          openPanel(user)
        }}
        className="flex min-w-0 flex-1 items-center justify-center gap-2 rounded-2xl bg-brand-ink px-4 py-2.5 text-[15px] font-bold text-brand"
      >
        <LayoutDashboard className="h-[18px] w-[18px] shrink-0" />
        <span className="truncate">Boshqaruv paneli</span>
      </button>
      <LogoutButton />
    </div>
  )
}

export function EmployeePanel({ user, me }) {
  const [tab, setTab] = useState('tasks')
  const top = <PanelBar user={user} />

  return (
    <>
      {tab === 'tasks' && <TasksScreen me={me} heroTop={top} />}
      {tab === 'summary' && <SummaryScreen heroTop={top} />}
      <TabBar
        tabs={TABS}
        value={tab}
        onChange={(next) => {
          if (next === tab) return
          haptic('light')
          setTab(next)
        }}
      />
    </>
  )
}

/**
 * staff / admin / superadmin / kassir — HR xodimimi yoki yo'qmi, shunga qarab.
 *
 * Tarmoq yoki server xatosida jimgina `StaffPortal` ga o'tkazib YUBORILMAYDI
 * (HR xodimi o'z topshiriqlarini yo'qotib qo'ymasin): "Qayta urinish" va
 * "Boshqaruv paneli" — ikkala yo'l ham ochiq.
 */
export function StaffHome({ user }) {
  const probe = useAsync(async () => {
    try {
      return { me: await hrApi.probe() }
    } catch (e) {
      return { error: hrErrorMessage(e) }
    }
  }, [])

  if (probe.loading || probe.data === null) {
    return (
      <Screen>
        <Loader label="Kirilmoqda…" />
      </Screen>
    )
  }

  if (probe.data.error) {
    return (
      <Screen>
        <ErrorState message={probe.data.error} onRetry={probe.reload} />
        <div className="mx-8">
          <button
            type="button"
            onClick={() => openPanel(user)}
            className="w-full rounded-2xl bg-white py-3 text-[15px] font-semibold text-brand-ink"
          >
            Boshqaruv paneli
          </button>
        </div>
      </Screen>
    )
  }

  if (!probe.data.me) return <StaffPortal user={user} />
  return <EmployeePanel user={user} me={probe.data.me} />
}
