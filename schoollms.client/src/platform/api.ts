import axios from 'axios'

/**
 * Control Plane (loyiha boshlig'i) uchun alohida API klienti. Maktab klientidan farqli:
 *  - tokeni `platform_token` da saqlanadi (maktab `token` bilan aralashmaydi),
 *  - X-Tenant sarlavhasi yo'q (asosiy domen so'rovi).
 */
export const platformApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
})

export const PLATFORM_TOKEN_KEY = 'platform_token'

platformApi.interceptors.request.use((config) => {
  const token = localStorage.getItem(PLATFORM_TOKEN_KEY)
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

export interface PlatformOwner {
  id: string
  fullName: string
  email: string
}

export interface Tenant {
  id: string
  name: string
  slug: string
  status: 'provisioning' | 'active' | 'suspended'
  superAdminEmail: string
  createdAt: string
  /** Maktab foydalana oladigan bo'limlar (kalitlar). Bo'sh = cheklovsiz (hamma bo'lim). */
  enabledModules: string[]
  /** Obuna boshlanish sanasi (YYYY-MM-DD) yoki null = chegarasiz. */
  subscriptionStartsAt: string | null
  /** Obuna tugash sanasi (YYYY-MM-DD) yoki null = muddatsiz. */
  subscriptionEndsAt: string | null
  /** Davr uchun obuna narxi (so'm). */
  subscriptionPrice: number
}

/** Admin paneli bo'limi (modul) — obuna belgilash uchun katalog elementi. */
export interface PlatformModule {
  key: string
  label: string
}

export interface PlatformDashboard {
  total: number
  active: number
  provisioning: number
  suspended: number
}

export interface CreateTenantInput {
  name: string
  slug: string
  superAdminFullName: string
  superAdminEmail: string
  superAdminPassword: string
  /** Ochiladigan bo'limlar (kalitlar). Bo'sh/berilmagan = cheklovsiz. */
  enabledModules?: string[]
  /** Obuna boshlanish sanasi (YYYY-MM-DD). null/berilmagan = chegarasiz. */
  subscriptionStartsAt?: string | null
  /** Obuna tugash sanasi (YYYY-MM-DD). null/berilmagan = muddatsiz. */
  subscriptionEndsAt?: string | null
  /** Obuna narxi (so'm). */
  subscriptionPrice?: number
}

/** Maktab obunasini tahrirlash kirishi (sanalar har doim to'liq yuboriladi; bo'sh = chegarasiz). */
export interface UpdateSubscriptionInput {
  enabledModules?: string[]
  subscriptionPrice?: number
  subscriptionStartsAt?: string | null
  subscriptionEndsAt?: string | null
}

export interface TenantStats {
  teachers: number
  staff: number
  students: number
  classes: number
  appActivated: number
  appDevices: number
}

export interface UpdateAccountInput {
  fullName?: string
  email?: string
  currentPassword?: string
  newPassword?: string
}

export interface UpdateTenantInput {
  name?: string
  superAdminEmail?: string
  superAdminPassword?: string
}

export async function platformLogin(email: string, password: string) {
  const { data } = await platformApi.post<{ token: string; owner: PlatformOwner }>(
    '/platform/auth/login', { email, password },
  )
  return data
}

export async function platformMe() {
  const { data } = await platformApi.get<PlatformOwner>('/platform/auth/me')
  return data
}

export async function listTenants() {
  const { data } = await platformApi.get<Tenant[]>('/platform/tenants')
  return data
}

export async function getDashboard() {
  const { data } = await platformApi.get<PlatformDashboard>('/platform/tenants/dashboard')
  return data
}

export async function createTenant(input: CreateTenantInput) {
  const { data } = await platformApi.post<Tenant>('/platform/tenants', input)
  return data
}

export async function setTenantStatus(id: string, status: 'active' | 'suspended') {
  const { data } = await platformApi.patch<Tenant>(`/platform/tenants/${id}`, { status })
  return data
}

export async function getTenantStats(id: string) {
  const { data } = await platformApi.get<TenantStats>(`/platform/tenants/${id}/stats`)
  return data
}

export async function updateTenant(id: string, input: UpdateTenantInput) {
  const { data } = await platformApi.put<Tenant>(`/platform/tenants/${id}`, input)
  return data
}

export async function listPlatformModules() {
  const { data } = await platformApi.get<PlatformModule[]>('/platform/tenants/modules')
  return data
}

export async function updateSubscription(id: string, input: UpdateSubscriptionInput) {
  const { data } = await platformApi.put<Tenant>(`/platform/tenants/${id}/subscription`, input)
  return data
}

export async function updateAccount(input: UpdateAccountInput) {
  const { data } = await platformApi.put<PlatformOwner>('/platform/auth/account', input)
  return data
}
