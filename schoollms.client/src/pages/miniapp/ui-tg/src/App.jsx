/**
 * Mini App qobig'i: sessiya → rolga qarab panel.
 *
 * IKKI PANEL, BITTA ILOVA. Telegramda bitta bot va bitta Mini App bo'ladi,
 * kimligini esa server aytadi — shuning uchun o'qituvchi va ota-ona uchun
 * alohida ilova yasamaymiz, faqat panelni almashtiramiz.
 */
import { SessionProvider, useSession } from './lib/session'
import { ErrorState, Loader } from './components/ui'
import { TeacherPanel } from './screens/TeacherPanel'
import { ParentPanel } from './screens/ParentPanel'
import { LinkScreen } from './screens/LinkScreen'
import { BrowserLogin } from './screens/BrowserLogin'
import { StaffPortal } from './screens/StaffPortal'

const TEACHER_ROLES = ['teacher']
const PARENT_ROLES = ['parent', 'student']
// Veb-panelda ishlaydiganlar — Mini App ularni panelga parolsiz o'tkazadi (2026-09-25).
const PANEL_ROLES = ['staff', 'admin', 'superadmin', 'cashier']

function Shell() {
  const { status, user, error, retry, adoptSession } = useSession()

  if (status === 'loading') return <Loader label="Kirilmoqda…" />

  // Telegramdan tashqarida — login va parol bilan. Bu demoga tayyorlanish va
  // brauzerdan tekshirish uchun; hech qanday chetlab o'tish yo'q.
  if (status === 'outside') return <BrowserLogin onSuccess={adoptSession} />

  if (status === 'unlinked') return <LinkScreen />

  if (status === 'error') return <ErrorState message={error} onRetry={retry} />

  if (TEACHER_ROLES.includes(user.role)) return <TeacherPanel user={user} />
  if (PARENT_ROLES.includes(user.role)) return <ParentPanel user={user} />
  if (PANEL_ROLES.includes(user.role)) return <StaffPortal user={user} />

  // Admin, kassir va xodim uchun Mini App yo'q — ular veb-panelda ishlaydi.
  // Jimgina bo'sh ekran ko'rsatishdan ko'ra sababini aytgan ma'qul.
  return (
    <ErrorState
      message={`Bu hisob (${user.role}) uchun Mini App yo'q. Veb-panelga kiring.`}
    />
  )
}

export function App() {
  return (
    <SessionProvider>
      <Shell />
    </SessionProvider>
  )
}
