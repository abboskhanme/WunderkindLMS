import type { User } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

export interface LoginResult {
  token: string
  user: User
}

/** USE_MOCK rejimida ishlatiladigan demo foydalanuvchilar (backend seed bilan bir xil). */
const mockAccounts: Record<string, { password: string; user: User }> = {
  'admin@maktab.uz': {
    password: 'admin123',
    user: { id: 'a1', fullName: 'Aziz Karimov', role: 'admin', email: 'admin@maktab.uz' },
  },
  'teacher@maktab.uz': {
    password: 'teacher123',
    user: { id: 't1u', fullName: 'Dilnoza Saidova', role: 'teacher', email: 'teacher@maktab.uz' },
  },
  'student@maktab.uz': {
    password: 'student123',
    user: { id: 's1u', fullName: 'Jasur Tohirov', role: 'student', email: 'student@maktab.uz' },
  },
  'parent@maktab.uz': {
    password: 'parent123',
    user: { id: 'p1u', fullName: 'Nodira Yusupova', role: 'parent', email: 'parent@maktab.uz' },
  },
}

export async function login(email: string, password: string): Promise<LoginResult> {
  if (USE_MOCK) {
    await delay(400)
    const entry = mockAccounts[email.trim().toLowerCase()]
    if (!entry || entry.password !== password) {
      throw new Error("Email yoki parol noto'g'ri")
    }
    return { token: `mock-token-${entry.user.role}`, user: entry.user }
  }
  const { data } = await api.post<LoginResult>('/auth/login', { email, password })
  return data
}

/** Joriy tokenga mos foydalanuvchini olish (sessiyani tekshirish uchun). */
export async function fetchMe(): Promise<User> {
  const { data } = await api.get<User>('/auth/me')
  return data
}

export interface UpdateAccountPayload {
  /** Yangi login (email); o'zgarmasa joriysi yuboriladi */
  email?: string
  /** Tasdiqlash uchun joriy parol */
  currentPassword: string
  /** Yangi parol; bo'sh bo'lsa parol o'zgarmaydi */
  newPassword?: string
}

/** Joriy foydalanuvchi o'z login/parolini o'zgartiradi — yangilangan foydalanuvchini qaytaradi. */
export async function updateAccount(payload: UpdateAccountPayload): Promise<User> {
  if (USE_MOCK) {
    await delay(300)
    return {
      id: 'a1',
      fullName: 'Administrator',
      role: 'admin',
      email: payload.email ?? 'admin@maktab.uz',
    }
  }
  const { data } = await api.put<User>('/auth/account', payload)
  return data
}
