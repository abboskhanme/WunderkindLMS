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

const TEACHER_ROLES = ['teacher']
const PARENT_ROLES = ['parent', 'student']

function Shell() {
  const { status, user, error, retry } = useSession()

  if (status === 'loading') return <Loader label="Kirilmoqda…" />

  if (status === 'outside') {
    return (
      <ErrorState
        message="Bu sahifa Telegram ilovasi ichida ochilishi kerak. Botga kiring va menyudan oching."
      />
    )
  }

  if (status === 'unlinked') return <LinkScreen />

  if (status === 'error') return <ErrorState message={error} onRetry={retry} />

  if (TEACHER_ROLES.includes(user.role)) return <TeacherPanel user={user} />
  if (PARENT_ROLES.includes(user.role)) return <ParentPanel user={user} />

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
