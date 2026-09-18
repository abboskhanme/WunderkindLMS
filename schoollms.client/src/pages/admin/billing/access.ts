/**
 * Moliya ma'lumotnomasining rol chegaralari — SPEC §4.3 jadvali, frontend
 * nusxasi (P1-17).
 *
 * BU YERDAGI TEKSHIRUV HIMOYA EMAS. Haqiqiy darvoza serverda:
 * `[Authorize(Roles = Roles.FinanceStaff)]` + `[FinanceRole(...)]` +
 * `ck_discounts_approver_differs` / `ck_expenses_approver_differs`.
 * Interfeys shunchaki ishlatib bo'lmaydigan tugmani KO'RSATMASLIGI kerak:
 * "bosdim — 403 oldim" foydalanuvchini o'z ruxsatlari haqida chalg'itadi,
 * ikki qavatli nazoratda esa bu ayniqsa yomon (odam o'z chegirmasini
 * tasdiqlashga urinib ko'rishi kerak emas — u variant umuman bo'lmasligi
 * kerak).
 *
 * Matritsa (SchoolLms.Server/Controllers/FinanceRoleAttribute.cs):
 *
 *   Amal                        | cashier | admin | superadmin (direktor)
 *   ----------------------------|---------|-------|----------------------
 *   Bo'limni ochish             |   yo'q  |  ha   |  ha
 *   Obuna/narx boshqarish       |   yo'q  |  ha   |  ha
 *   Chegirma so'rash            |   yo'q  |  ha   |  ha
 *   Chegirmani tasdiqlash       |   yo'q  |  yo'q |  ha   (+ o'zinikini emas)
 *   Chiqim yozish               |   yo'q  |  ha   |  ha
 *   Chiqimni tasdiqlash         |   yo'q  |  yo'q |  ha   (+ o'zinikini emas)
 *   Chiqimni storno qilish      |   yo'q  |  ha   |  ha
 */
import type { Role, User } from '@/types'
import { useAuth } from '@/context/auth-context'

/** Bo'limga umuman kira oladigan rollar (`Roles.FinanceStaff` bilan bir xil). */
export const BILLING_ROLES: readonly Role[] = ['admin', 'superadmin']

/** Faqat direktor bajaradigan amallar (tasdiqlash). */
const DIRECTOR: Role = 'superadmin'

function hasRole(user: User | null, roles: readonly Role[]): boolean {
  return !!user && roles.includes(user.role)
}

/** Ism solishtiruvi uchun: ortiqcha bo'sh joy va katta/kichik harf farqi olib tashlanadi. */
function normalizeName(name: string | undefined | null): string {
  return (name ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase('uz-Latn-UZ')
}

/** Yaratuvchisi ko'rsatilgan har qanday moliyaviy yozuv. */
export interface AuthoredRecord {
  createdByName: string
  /** Backend qo'shsa — id bo'yicha aniq solishtiruv (hozircha yo'q). */
  createdById?: string
}

/**
 * Shu yozuvni JORIY foydalanuvchi yaratganmi? (SPEC §4.5 — ikki qavatli nazorat)
 *
 * `createdById` kelsa — id bo'yicha, aniq. Kelmasa — ism bo'yicha, taxminiy.
 * Taxmin ATAYLAB "yopiq" tomonga ishlaydi: bir xil ismli ikki xodim bo'lsa,
 * tasdiqlash tugmasi ikkalasidan ham yashiriladi. Kerak bo'lmagan tugmani
 * yashirish — kerakli tugmani ochib qo'yishdan xavfsizroq, va serverning
 * `self_approval` tekshiruvi baribir joyida turadi.
 */
export function isOwnRecord(user: User | null, record: AuthoredRecord): boolean {
  if (!user) return false
  if (record.createdById) return record.createdById === user.id
  return normalizeName(record.createdByName) === normalizeName(user.fullName)
}

export interface BillingAccess {
  user: User | null
  /** Bo'limni umuman ochish (admin / direktor). */
  canOpen: boolean
  /** Kassir — sahifaga aynan shu rol yaqinlashtirilmaydi (alohida xabar uchun). */
  isCashier: boolean
  /** Toifa, obuna va narxlarni boshqarish. */
  canManageSubscriptions: boolean
  /**
   * Moliya sozlamalarini (to'lov muddati, chiqim tasdiq chegarasi) ochish —
   * admin va direktor (F14.01). Chegarani TAHRIRLASH esa faqat direktorga
   * ochiq — buni {@link isDirector} bilan alohida tekshiring, server ham
   * shu qoidani `threshold_requires_director` bilan mustahkamlaydi.
   */
  canManageBillingSettings: boolean
  /**
   * Rejalashtirilgan chiqim shablonlarini boshqarish (F6.01) — admin va
   * direktor, `canManageBillingSettings` bilan bir xil daraja
   * (server: `FinanceAction.ManageExpenseTemplates`, `FinanceRoleAttribute.cs`).
   */
  canManageExpenseTemplates: boolean
  /**
   * Joriy foydalanuvchi direktormi. Ichkarida chegirma/chiqim tasdig'i uchun
   * ishlatilgan hisob shu yerda ham ochiladi — F14.01 moliya sozlamalari
   * sahifasi chiqim chegarasini faqat direktorga tahrirlanadigan qiladi.
   */
  isDirector: boolean
  /** Chegirma so'rash (natija har doim `pending`). */
  canGrantDiscount: boolean
  /** Chegirmani tasdiqlash/rad etish — faqat direktor. */
  canApproveDiscount: boolean
  /** Chiqim yozish. */
  canRecordExpense: boolean
  /** Chiqimni tasdiqlash — faqat direktor. */
  canApproveExpense: boolean
  /** Chiqimni storno qilish (sabab bilan). O'chirish YO'Q. */
  canReverseExpense: boolean
  /**
   * Shu yozuvni tasdiqlash tugmasi KO'RSATILADIMI: rol yetarli VA yozuvni
   * boshqa odam yaratgan.
   */
  canApproveRecord: (allowedByRole: boolean, record: AuthoredRecord) => boolean
  /** Yozuvni joriy foydalanuvchi yaratganmi. */
  isOwn: (record: AuthoredRecord) => boolean
}

/** Joriy foydalanuvchining moliya ma'lumotnomasidagi ruxsatlari. */
export function useBillingAccess(): BillingAccess {
  const { user } = useAuth()

  const canOpen = hasRole(user, BILLING_ROLES)
  const isDirector = hasRole(user, [DIRECTOR])
  const isOwn = (record: AuthoredRecord) => isOwnRecord(user, record)

  return {
    user,
    canOpen,
    isCashier: user?.role === 'cashier',
    canManageSubscriptions: canOpen,
    canManageBillingSettings: canOpen,
    canManageExpenseTemplates: canOpen,
    isDirector,
    canGrantDiscount: canOpen,
    canApproveDiscount: isDirector,
    canRecordExpense: canOpen,
    canApproveExpense: isDirector,
    canReverseExpense: canOpen,
    canApproveRecord: (allowedByRole, record) => allowedByRole && !isOwn(record),
    isOwn,
  }
}
