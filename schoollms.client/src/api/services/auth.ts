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

/** O'z profil rasmini yuklash (har qanday rol). Yangilangan foydalanuvchini qaytaradi. */
export async function uploadAvatar(file: File): Promise<User> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<User>('/auth/avatar', form)
  return data
}

/** O'z profil rasmini olib tashlash. */
export async function removeAvatar(): Promise<User> {
  const { data } = await api.delete<User>('/auth/avatar')
  return data
}

/**
 * The Telegram account a session was opened from (the `tg_uid` claim) — set only when the web panel
 * was opened from the Mini App ("Panelni ochish" for staff). Read from the token, never trusted for
 * access: it only decides whether "Chiqish" should unlink that Telegram too.
 */
export function telegramOfToken(token: string | null): string | null {
  if (!token) return null
  try {
    const part = token.split('.')[1] ?? ''
    const json = JSON.parse(atob(part.replace(/-/g, '+').replace(/_/g, '/')))
    return typeof json.tg_uid === 'string' ? json.tg_uid : null
  } catch {
    return null
  }
}

/**
 * "Chiqish" from a panel opened through Telegram (2026-10-07): unlink that Telegram as well, so the
 * next open asks for a login instead of signing the old account in again. Never blocks the logout.
 */
export async function unlinkTelegramSession(): Promise<void> {
  if (!telegramOfToken(localStorage.getItem('token'))) return
  try {
    await api.delete('/tg/link')
  } catch {
    /* the logout goes on regardless */
  }
}

// ---------------------------------------------------------------------------
//  Several accounts on one Telegram (client, 2026-10-08; staff panel 2026-10-28) — the same
//  /api/tg/accounts the Mini App uses. Works only in a session opened from Telegram (tg_uid).
// ---------------------------------------------------------------------------

export interface TelegramSavedAccount {
  userId: string
  fullName: string
  role: string
  subtitle: string
  active: boolean
}

/** The accounts this Telegram can switch to — the active one first. */
export async function listTelegramAccounts(): Promise<TelegramSavedAccount[]> {
  const { data } = await api.get<{ accounts: TelegramSavedAccount[] }>('/tg/accounts')
  return data.accounts
}

/** Switches to a saved account — no password; answers with that account's session. */
export async function switchTelegramAccount(userId: string): Promise<LoginResult> {
  const { data } = await api.post<LoginResult>(`/tg/accounts/${encodeURIComponent(userId)}/switch`)
  return data
}

/** Adds an account with its own login and password and switches to it. */
export async function addTelegramAccount(login: string, password: string): Promise<LoginResult> {
  const { data } = await api.post<LoginResult>('/tg/accounts', { login, password })
  return data
}

/** Drops a saved account from this Telegram's list (never the active one). */
export async function forgetTelegramAccount(userId: string): Promise<void> {
  await api.delete(`/tg/accounts/${encodeURIComponent(userId)}`)
}
