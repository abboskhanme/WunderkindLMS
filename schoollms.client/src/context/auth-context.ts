import { createContext, useContext } from 'react'
import type { User } from '@/types'

export interface AuthContextValue {
  /** Joriy foydalanuvchi yoki kirilmagan bo'lsa null */
  user: User | null
  isAuthenticated: boolean
  /** Email/parol orqali kirish — muvaffaqiyatda foydalanuvchini qaytaradi */
  login: (email: string, password: string) => Promise<User>
  logout: () => void
  /** Joriy foydalanuvchi ma'lumotini yangilash (masalan, akkaunt sozlamalaridan keyin) */
  updateUser: (user: User) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth() AuthProvider ichida ishlatilishi kerak')
  return ctx
}
