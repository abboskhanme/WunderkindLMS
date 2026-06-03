import { useEffect, useState } from 'react'
import './platform.css'
import { LoginView } from './LoginView'
import { Shell } from './Shell'
import { PLATFORM_TOKEN_KEY, platformMe, type PlatformOwner } from './api'

/**
 * Control Plane ildizi (asosiy domen). Maktab LMS'idan butunlay ALOHIDA:
 * o'z tokeni, o'z auth oqimi. React Router ishlatmaydi — ichki holat bilan oddiy navigatsiya.
 */
export function PlatformApp() {
  const [owner, setOwner] = useState<PlatformOwner | null>(null)
  const [ready, setReady] = useState(false)

  // Sahifa ochilganda tokenni /me orqali tekshiramiz.
  useEffect(() => {
    if (!localStorage.getItem(PLATFORM_TOKEN_KEY)) { setReady(true); return }
    platformMe()
      .then(setOwner)
      .catch(() => localStorage.removeItem(PLATFORM_TOKEN_KEY))
      .finally(() => setReady(true))
  }, [])

  function handleLogin(token: string, o: PlatformOwner) {
    localStorage.setItem(PLATFORM_TOKEN_KEY, token)
    setOwner(o)
  }
  function handleLogout() {
    localStorage.removeItem(PLATFORM_TOKEN_KEY)
    setOwner(null)
  }

  if (!ready) return <div className="cp" />

  return (
    <div className="cp">
      {owner
        ? <Shell owner={owner} onLogout={handleLogout} onOwnerUpdated={setOwner} />
        : <LoginView onLogin={handleLogin} />}
    </div>
  )
}
