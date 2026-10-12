/**
 * O'QITUVCHI PANELI — qobiq.
 *
 * Qobiq uchta ish qiladi va boshqa hech narsa: profil bilan meta'ni BIR MARTA
 * oladi (har tab o'zi so'ramasin), faol tabni saqlaydi va tab panelini chizadi.
 * Har tabning mazmuni `teacher/*.jsx` da.
 *
 * BIR XIL MENYU (mijoz, 2026-10-07: "o'qituvchi bo'lsa bo'ldi, hammasida bir xil menu, bir xil oynalar
 * ochilishi kerak"). Pastki panel HAR BIR o'qituvchida bir xil beshta tab — fani, sinf rahbarligi,
 * ruxsati yoki HR qatori bor-yo'qligidan qat'i nazar. Ruxsati olib qo'yilgan bo'lim yashirilmaydi:
 * tab o'z joyida, ichida "bo'lim yopiq" deb yoziladi (server baribir 403 qaytaradi).
 */
import { useState } from 'react'
import { CalendarDays, ClipboardCheck, Home, ListChecks, Lock } from 'lucide-react'
import { Card, EmptyState, ErrorState, Hero, Loader, Screen, TabBar } from '../components/ui'
import { useAsync } from '../lib/useAsync'
import { teacherApi } from '../lib/teacherApi'
import { hrApi } from '../lib/hrApi'
import { TodayTab } from './teacher/TodayTab'
import { AttendanceTab } from './teacher/AttendanceTab'
import { ScheduleTab } from './teacher/ScheduleTab'
import { BoardingScreen } from './teacher/BoardingScreen'
import { HrTab } from './hr/HrTab'

/** `perm` — shu tab ichini ochish uchun kerakli ruxsat (null = hammaga). Tab o'zi doim ko'rinadi. */
const TABS = [
  { value: 'home', label: 'Bugun', icon: Home, perm: null },
  // "Jurnal": davomat va baho bitta ro'yxatda (mijoz, 2026-09-29).
  { value: 'attendance', label: 'Jurnal', icon: ClipboardCheck, perm: 'journal' },
  { value: 'schedule', label: 'Jadval', icon: CalendarDays, perm: 'schedule' },
  // No 'Maosh' tab (client, 2026-09-28): teachers do not see their salary; the server refuses it too.
  // "Xabarlar" (guruh chati) olib tashlandi (mijoz, 2026-10-11): chat faqat admin panelida.
]

/**
 * HR topshiriqlari (hr-tasks.md §8) — doim beshinchi tab. Ichi `GET /hr/me` javobiga qarab:
 * xodim qatori bo'lsa topshiriqlar, bo'lmasa "topshiriq yo'q".
 */
const HR_TAB = { value: 'hr', label: 'Topshiriqlar', icon: ListChecks, perm: null }
const ALL_TABS = [...TABS, HR_TAB]

/** The same tab for everyone; inside — why it is empty for this teacher. */
function Closed({ title, note }) {
  return (
    <Screen>
      <Hero title={title} subtitle="Bo'lim" />
      <Card>
        <EmptyState icon={<Lock className="h-9 w-9" />} title="Bo'lim yopiq" note={note} />
      </Card>
    </Screen>
  )
}

export function TeacherPanel() {
  const ctx = useAsync(() => Promise.all([teacherApi.profile(), teacherApi.meta()]), [])
  // Panelni KUTTIRMAYDI: javob kelguncha beshta tab, xodim bo'lsa oltinchisi qo'shiladi.
  // Har qanday xato — "tab yo'q" (o'qituvchi paneli o'zgarishsiz qoladi).
  const hr = useAsync(() => hrApi.probe().catch(() => null), [])
  const hrMe = hr.data
  const hrLoading = hr.loading && hr.data === null
  const [tab, setTab] = useState('home')
  // "Bugun" dan darsga bosilganda davomat o'sha darsda ochilishi uchun.
  const [focusLesson, setFocusLesson] = useState(null)
  // Komendant (2026-10-12): "Bugun" dagi kartadan ochilgan yo'qlama — 'evening' | 'dorm' | null.
  // Alohida tab emas: pastki menyu hammada bir xil qoladi.
  const [boarding, setBoarding] = useState(null)

  if (ctx.loading && ctx.data === null) {
    return (
      <Screen>
        <Loader label="Panel yuklanmoqda…" />
      </Screen>
    )
  }
  if (ctx.error) {
    return (
      <Screen>
        <ErrorState message={ctx.error} onRetry={ctx.reload} />
      </Screen>
    )
  }

  const [profile, meta] = ctx.data
  const perms = profile?.permissions || []
  const allowed = (value) => {
    const t = ALL_TABS.find((x) => x.value === value)
    return !t?.perm || perms.includes(t.perm)
  }
  const closedNote = "Bu bo'lim siz uchun hali ochilmagan. Maktab administratoriga murojaat qiling."
  const active = tab

  const openAttendance = (lesson) => {
    setFocusLesson(lesson)
    setTab('attendance')
  }

  const change = (next) => {
    if (next !== 'attendance') setFocusLesson(null)
    setBoarding(null)
    setTab(next)
  }

  return (
    <>
      {active === 'home' && !boarding && (
        <TodayTab profile={profile} meta={meta} onOpenAttendance={openAttendance} onOpenBoarding={setBoarding} />
      )}
      {active === 'home' && boarding && (
        <BoardingScreen
          key={boarding}
          profile={profile}
          meta={meta}
          session={boarding}
          onSession={setBoarding}
          onBack={() => setBoarding(null)}
        />
      )}
      {active !== 'home' && active !== 'hr' && !allowed(active) && (
        <Closed title={ALL_TABS.find((t) => t.value === active)?.label ?? ''} note={closedNote} />
      )}
      {active === 'attendance' && allowed('attendance') && (
        <AttendanceTab
          profile={profile}
          meta={meta}
          focusLesson={focusLesson}
          onClearFocus={() => setFocusLesson(null)}
        />
      )}
      {active === 'schedule' && allowed('schedule') && <ScheduleTab meta={meta} />}
      {active === 'hr' &&
        (hrMe ? (
          <HrTab me={hrMe} />
        ) : hrLoading ? (
          <Screen>
            <Loader label="Topshiriqlar yuklanmoqda…" />
          </Screen>
        ) : (
          <Screen>
            <Hero title="Topshiriqlar" subtitle="HR" />
            <Card>
              <EmptyState
                icon={<ListChecks className="h-9 w-9" />}
                title="Topshiriq yo'q"
                note="Sizga hali HR topshiriqlari biriktirilmagan."
              />
            </Card>
          </Screen>
        ))}

      <TabBar tabs={ALL_TABS} value={active} onChange={change} />
    </>
  )
}
