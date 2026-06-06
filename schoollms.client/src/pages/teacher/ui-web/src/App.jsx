import { useState, useEffect } from 'react'
import { SessionProvider, useSession } from './lib/session'
import { userStore } from './lib/api'
import BottomNav from './components/BottomNav'

import HomeScreen from './screens/HomeScreen'
import ScheduleScreen from './screens/ScheduleScreen'
import JournalPickerScreen from './screens/JournalPickerScreen'
import JournalGridScreen from './screens/JournalGridScreen'
import AssignmentsScreen from './screens/AssignmentsScreen'
import AssignmentCreateScreen from './screens/AssignmentCreateScreen'
import AssignmentResultsScreen from './screens/AssignmentResultsScreen'
import ChatChannelsScreen from './screens/ChatChannelsScreen'
import ChatConversationScreen from './screens/ChatConversationScreen'
import SalaryScreen from './screens/SalaryScreen'
import ProfileScreen from './screens/ProfileScreen'
import HomeroomScreen from './screens/HomeroomScreen'
import FeedbackScreen from './screens/FeedbackScreen'
import NotificationsScreen from './screens/NotificationsScreen'
import ProgressScreen from './screens/ProgressScreen'
import LmsSubjectsScreen from './screens/LmsSubjectsScreen'
import LmsSubjectScreen from './screens/LmsSubjectScreen'
import LmsStudentTopicsScreen from './screens/LmsStudentTopicsScreen'
import LmsTopicDetailScreen from './screens/LmsTopicDetailScreen'

// Ekran reestri. `tab` bo'lgan ekran — pastki navigatsiya ildizi; qolganlari — ichki ekranlar.
const SCREENS = {
  home: { Comp: HomeScreen, tab: 0 },
  journal: { Comp: JournalPickerScreen, tab: 1 },
  assignments: { Comp: AssignmentsScreen, tab: 2 },
  chat: { Comp: ChatChannelsScreen, tab: 3 },
  profile: { Comp: ProfileScreen, tab: 4 },

  schedule: { Comp: ScheduleScreen },
  journalGrid: { Comp: JournalGridScreen },
  assignmentCreate: { Comp: AssignmentCreateScreen },
  assignmentResults: { Comp: AssignmentResultsScreen },
  chatConversation: { Comp: ChatConversationScreen },
  homeroom: { Comp: HomeroomScreen },
  notifications: { Comp: NotificationsScreen },
  salary: { Comp: SalaryScreen },
  progress: { Comp: ProgressScreen },
  feedback: { Comp: FeedbackScreen },
  lms: { Comp: LmsSubjectsScreen },
  lmsSubject: { Comp: LmsSubjectScreen },
  lmsStudentTopics: { Comp: LmsStudentTopicsScreen },
  lmsTopicDetail: { Comp: LmsTopicDetailScreen },
}

const TAB_IDS = ['home', 'journal', 'assignments', 'chat', 'profile']

const THEME_KEY = 'teacher-theme'

function Shell() {
  const { isAuthed } = useSession()
  // Chiziqli navigatsiya tarixi: har bir element { id, params }. Oxirgisi — joriy ekran.
  // Brauzer/qurilma "orqaga" tugmasi shu tarixga bog'langan (History API) — ilovadan
  // chiqib ketmaydi, balki ichida bir qadam ortga qaytadi.
  const [entries, setEntries] = useState([{ id: 'home', params: {} }])
  const [dark, setDark] = useState(() => localStorage.getItem(THEME_KEY) === 'dark')

  useEffect(() => {
    localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light')
    // <html> ga ham qo'shamiz — manifest theme-color / address bar mosligi uchun.
    document.documentElement.classList.toggle('dark', dark)
  }, [dark])

  // Login holatidan chiqilganda boshlang'ich ekranga qaytamiz.
  useEffect(() => {
    if (!isAuthed) setEntries([{ id: 'home', params: {} }])
  }, [isAuthed])

  // Brauzer/qurilma "orqaga" (yoki iOS swipe-back) → ilova ichida bir qadam ortga.
  // Forward navigatsiyada history.pushState qilingani uchun, bu yerda faqat bir element olib
  // tashlaymiz; ildizda (bitta element) bo'lsa — brauzerning o'zi ilovadan chiqishiga ruxsat.
  useEffect(() => {
    const onPop = () => setEntries((es) => (es.length > 1 ? es.slice(0, -1) : es))
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  // BITTA login nuqtasi — asosiy sayt (/login). Bu yerda alohida login ekrani YO'Q:
  //  • token yo'q / tugagan → asosiy /login'ga (u rolni aniqlab, o'qituvchini /teacher'ga qaytaradi);
  //  • token bor, lekin rol o'qituvchi emas (masalan admin /teacher'ga kirib qolsa) → asosiy saytga.
  useEffect(() => {
    if (!isAuthed) {
      window.location.replace('/login')
      return
    }
    const u = userStore.get()
    if (u && u.role && u.role !== 'teacher') window.location.replace('/')
  }, [isAuthed])

  if (!isAuthed) {
    return (
      <Frame dark={dark}>
        <Redirecting />
      </Frame>
    )
  }

  const current = entries[entries.length - 1]
  const entry = SCREENS[current.id] || SCREENS.home
  const Comp = entry.Comp
  const onTabRoot = entry.tab !== undefined
  const canGoBack = entries.length > 1

  // Pastki nav uchun aktiv tab — tarixning oxiridan eng yaqin tab ekrani.
  let activeTab = 0
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = SCREENS[entries[i].id]
    if (e && e.tab !== undefined) {
      activeTab = e.tab
      break
    }
  }

  // Forward navigatsiya — yangi element + history.pushState (popstate'ni tetiklamaydi).
  const navigate = (id, params = {}) => {
    if (!SCREENS[id]) return
    setEntries((es) => [...es, { id, params }])
    window.history.pushState({ tApp: true }, '')
  }
  const switchTab = (id) => {
    if (current.id === id) return // shu tab ildizidamiz — takror qo'shmaymiz
    navigate(id, {})
  }
  // "Orqaga" — brauzer tarixini chaqiramiz; popstate yuqorida elementni olib tashlaydi
  // (UI back tugmasi ham, qurilma back tugmasi ham bir xil yo'l bilan ishlaydi).
  const goBack = () => window.history.back()

  return (
    <Frame dark={dark}>
      <div className="absolute inset-0 flex flex-col">
        <div className="flex-1 overflow-hidden">
          <Comp
            key={`${entries.length}:${current.id}`}
            params={current.params}
            onNavigate={navigate}
            onBack={canGoBack ? goBack : null}
            onSwitchTab={switchTab}
            dark={dark}
            onToggleTheme={() => setDark((v) => !v)}
          />
        </div>
        {onTabRoot && (
          <BottomNav activeIndex={activeTab} onChange={(i) => switchTab(TAB_IDS[i])} />
        )}
      </div>
    </Frame>
  )
}

// Asosiy /login'ga yo'naltirilayotganda ko'rsatiladigan qisqa holat (login ekrani o'rniga).
function Redirecting() {
  return (
    <div className="h-full flex flex-col items-center justify-center gap-3 bg-bg">
      <div className="w-9 h-9 rounded-full border-[3px] border-primary border-t-transparent animate-spin" />
      <p className="text-[13px] text-muted">Tizimga yo'naltirilmoqda…</p>
    </div>
  )
}

// Mobil: butun ekran. Keng ekran (desktop): markazda telefon-uzunlikdagi ustun.
function Frame({ dark, children }) {
  return (
    <div className={dark ? 'dark' : ''}>
      <div className="min-h-[100dvh] bg-bg-alt text-text font-sans flex justify-center">
        <div className="relative w-full max-w-[480px] h-[100dvh] bg-bg overflow-hidden shadow-2xl">
          {children}
        </div>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <SessionProvider>
      <Shell />
    </SessionProvider>
  )
}
