import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import type { User } from '@/types'
import { AuthContext } from './auth-context'
import { login as loginRequest, fetchMe } from '@/api/services/auth'
import { USE_MOCK } from '@/api/client'

const TOKEN_KEY = 'token'
const USER_KEY = 'user'

function readStoredUser(): User | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as User) : null
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
