import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import type { User } from '@/types'
import { AuthContext } from './auth-context'
import { login as loginRequest, fetchMe } from '@/api/services/auth'
import { USE_MOCK } from '@/api/client'

const TOKEN_KEY = 'token'
const USER_KEY = 'user'

// O'quvchi va ota-ona web orqali kira olmaydi — ular faqat mobil ilovadan foydalanadi.
const WEB_BLOCKED_ROLES = ['student', 'parent']

function readStoredUser(): User | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    const u = raw ? (JSON.parse(raw) as User) : null
    // Eski sessiya o'quvchi/ota-ona bo'lsa — webda tiklamaymiz.
    if (u && WEB_BLOCKED_ROLES.includes(u.role)) return null
    return u
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Sahifa yangilanganda sessiyani localStorage'dan tiklaymiz (token bo'lsa).
  const [user, setUser] = useState<User | null>(() =>
    localStorage.getItem(TOKEN_KEY) ? readStoredUser() : null,
  )

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    setUser(null)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const { token, user: u } = await loginRequest(email, password)
    // O'quvchi/ota-ona web orqali kira olmaydi — sessiyani saqlamaymiz.
    if (WEB_BLOCKED_ROLES.includes(u.role)) {
      throw new Error("O'quvchi va ota-ona hisobi web orqali kira olmaydi. Mobil ilovadan foydalaning.")
    }
    localStorage.setItem(TOKEN_KEY, token)
    localStorage.setItem(USER_KEY, JSON.stringify(u))
    setUser(u)
    return u
  }, [])

  const updateUser = useCallback((u: User) => {
    localStorage.setItem(USER_KEY, JSON.stringify(u))
    setUser(u)
  }, [])

  // Real rejimda tokenni /me orqali tekshiramiz: amal qilmasa — chiqaramiz.
  useEffect(() => {
    if (USE_MOCK || !localStorage.getItem(TOKEN_KEY)) return
    fetchMe()
      .then((u) => {
        // O'quvchi/ota-ona webda ishlay olmaydi — tokenni tozalaymiz.
        if (WEB_BLOCKED_ROLES.includes(u.role)) { logout(); return }
        localStorage.setItem(USER_KEY, JSON.stringify(u))
        setUser(u)
      })
      .catch(() => logout())
  }, [logout])

  const value = useMemo(
    () => ({ user, isAuthenticated: !!user, login, logout, updateUser }),
    [user, login, logout, updateUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
