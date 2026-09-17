import { api } from '../client'

/**
 * Jadval ko'rinishi sozlamalari — `docs/modules/students-parity.md` §2.11 (X-1).
 *
 * Har bir katta ro'yxat (Guruhlar, keyinroq boshqalari) ustunlarni
 * yashirish/tartiblash/qadash imkonini beradi, tanlov esa foydalanuvchi ×
 * sahifa kesimida serverda saqlanadi (`UserTableSettingsController`,
 * `GET|PUT /api/user-table-settings/{page}`). Bu yo'l `/api/admin/...` DAN
 * TASHQARIDA — endpoint bo'lim ruxsatiga emas, faqat admin panel rollariga
 * bog'liq (controller izohi).
 *
 * `settings` shakli SERVER UCHUN shaffof (`JsonElement`) — shakl faqat shu
 * yerda va uni ishlatuvchi `DataTable` komponentida belgilanadi.
 */
export interface TableViewSettings {
  /** Ustunlar id tartibi (chapdan o'ngga). Qoldirilgan id'lar oxiriga qo'shiladi. */
  order?: string[]
  /** Yashirilgan ustun id'lari. */
  hidden?: string[]
  /** Qadalgan (pinned) ustun id'lari — ko'rinadiganlar orasida birinchi bo'lib chiqadi. */
  pinned?: string[]
}

export interface UserTableSettingsResponse {
  page: string
  settings: TableViewSettings
  updatedAt: string | null
}

export async function getTableSettings(page: string): Promise<TableViewSettings> {
  const { data } = await api.get<UserTableSettingsResponse>(
    `/user-table-settings/${encodeURIComponent(page)}`,
  )
  return data.settings
}

export async function saveTableSettings(
  page: string,
  settings: TableViewSettings,
): Promise<TableViewSettings> {
  const { data } = await api.put<UserTableSettingsResponse>(
    `/user-table-settings/${encodeURIComponent(page)}`,
    { settings },
  )
  return data.settings
}
