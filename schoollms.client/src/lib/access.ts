import type { User } from '@/types'
import { navByRole, type NavItem } from '@/config/navigation'

/**
 * Sahifa darajasidagi ruxsatlar (Boshqaruv → Rollar, mijoz 2026-09-26).
 *
 * Ruxsat birligi — menyudagi SAHIFA (submenyu). Har biriga daraja: yo'q / faqat
 * ko'rish / to'liq. Katalog menyuning o'zidan hosil qilinadi (`navigation.ts`),
 * shuning uchun menyuga yangi sahifa qo'shilsa, rol oynasida ham paydo bo'ladi.
 *
 * SAQLASH SHAKLI (rol va xodimning `permissions` ro'yxatida, bazaga migratsiyasiz):
 *   "/admin/finance/debtors|view"   — sahifa, faqat ko'rish
 *   "/admin/finance/debtors|edit"   — sahifa, to'liq
 *   "finance" / "finance:view"      — server bo'lim kaliti, sahifalardan HOSIL qilinadi
 *                                     (AdminPerm, moliya darvozalari shunga qaraydi)
 *
 * ESKI SHAKL (faqat bo'lim kalitlari, "/" bilan boshlanadigan yozuv yo'q) —
 * bo'limning hamma sahifasiga "to'liq" deb o'qiladi: hech kim bugungi imkoniyatini
 * yo'qotmaydi (mijoz tanlovi).
 */

export type AccessLevel = 'none' | 'view' | 'edit'

export interface AccessPage {
  /** Sahifa kaliti — menyudagi manzil (`to`). */
  key: string
  label: string
  /** Server bo'lim kaliti (AdminPerm), masalan `finance`, `students`. */
  section: string
}

export interface AccessMenu {
  label: string
  pages: AccessPage[]
}

/** Rol oynasida ko'rsatilmaydigan menyu: mijoz, 2026-09-26 — "future ... hozircha shart emas". */
const HIDDEN_MENUS = new Set(['Future'])

function buildCatalog(items: NavItem[]): AccessMenu[] {
  const menus: AccessMenu[] = []
  for (const item of items) {
    if (HIDDEN_MENUS.has(item.label)) continue
    if (!item.children) {
      if (item.perm) menus.push({ label: item.label, pages: [{ key: item.to, label: item.label, section: item.perm }] })
      continue
    }
    const pages = item.children
      // Faqat superadmin'ga mo'ljallangan sahifa (Filiallar) rol bilan berilmaydi.
      .filter((c) => !c.roles || c.roles.includes('staff'))
      .map((c) => ({ key: c.to, label: c.label, section: c.perm ?? item.perm ?? '' }))
      .filter((p) => p.section !== '')
    if (pages.length > 0) menus.push({ label: item.label, pages })
  }
  return menus
}

export const ACCESS_CATALOG: AccessMenu[] = buildCatalog(navByRole.admin)
export const ACCESS_PAGES: AccessPage[] = ACCESS_CATALOG.flatMap((m) => m.pages)

const GRANT_SEP = '|'

export const isPageGrant = (entry: string) => entry.startsWith('/')

/** Rol/xodim ruxsatlari → sahifa → daraja. Eski shakl bo'lim kalitlaridan yoyiladi. */
export function grantsOf(permissions: readonly string[]): Map<string, AccessLevel> {
  const map = new Map<string, AccessLevel>()
  if (permissions.some(isPageGrant)) {
    for (const entry of permissions) {
      if (!isPageGrant(entry)) continue
      const i = entry.lastIndexOf(GRANT_SEP)
      if (i < 0) continue
      const level = entry.slice(i + 1)
      if (level === 'view' || level === 'edit') map.set(entry.slice(0, i), level)
    }
    return map
  }
  // Eski shakl: bo'lim kaliti → uning hamma sahifasi.
  for (const page of ACCESS_PAGES) {
    if (permissions.includes(page.section)) map.set(page.key, 'edit')
    else if (permissions.includes(`${page.section}:view`)) map.set(page.key, 'view')
  }
  return map
}

/** Sahifa darajalari → saqlanadigan ro'yxat (sahifa yozuvlari + hosila bo'lim kalitlari). */
export function encodeGrants(grants: Map<string, AccessLevel>): string[] {
  const out: string[] = []
  const sections = new Map<string, AccessLevel>()
  for (const page of ACCESS_PAGES) {
    const level = grants.get(page.key) ?? 'none'
    if (level === 'none') continue
    out.push(`${page.key}${GRANT_SEP}${level}`)
    const prev = sections.get(page.section)
    if (prev !== 'edit') sections.set(page.section, level)
  }
  for (const [section, level] of sections) out.push(level === 'edit' ? section : `${section}:view`)
  return out
}

/** Xodim cheklanadimi: faqat `staff`. Admin/direktor/boshqalar — bu tizimdan tashqari. */
const isLimited = (user: User | null) => user?.role === 'staff'

/** Menyudagi bitta sahifaning darajasi. */
export function pageLevel(user: User | null, pageKey: string): AccessLevel {
  if (!user) return 'none'
  if (!isLimited(user)) return 'edit'
  return grantsOf(user.permissions ?? []).get(pageKey) ?? 'none'
}

/** Manzil qaysi katalog sahifasiga tegishli (aniq moslik, keyin eng uzun prefiks). */
export function pageForPath(pathname: string, search = ''): AccessPage | null {
  const full = pathname + search
  const exact = ACCESS_PAGES.find((p) => p.key === full) ?? ACCESS_PAGES.find((p) => p.key === pathname)
  if (exact) return exact
  let best: AccessPage | null = null
  for (const p of ACCESS_PAGES) {
    const base = p.key.split('?')[0]
    // "/admin" (Bosh sahifa) prefiks sifatida hamma narsaga mos kelardi — faqat aniq.
    if (base === '/admin') continue
    if (pathname.startsWith(base + '/') && (!best || base.length > best.key.split('?')[0].length)) best = p
  }
  return best
}

/**
 * Joriy manzilning darajasi. Katalogda yo'q manzil (eski yo'llar, ichki sahifalar) —
 * 'edit': ularni route'dagi `RequirePerm` (bo'lim kaliti) himoya qiladi.
 */
export function levelForPath(user: User | null, pathname: string, search = ''): AccessLevel {
  if (!isLimited(user)) return 'edit'
  const page = pageForPath(pathname, search)
  return page ? pageLevel(user, page.key) : 'edit'
}

/**
 * Menyu bandi ko'rinadimi — Sidebar, Bosh sahifa yo'naltirishi va global qidiruv
 * uchun YAGONA qoida. Xodim uchun: katalogdagi sahifa — darajasiga qarab; bolalari
 * bor bo'lim — bolalaridan biri ko'rinsa (chaqiruvchi bo'sh bo'limni olib tashlaydi).
 */
export function canSeeNav(
  user: User | null,
  x: { to: string; perm?: string; roles?: User['role'][]; children?: unknown[] },
): boolean {
  if (!user) return false
  if (x.roles && !x.roles.includes(user.role)) return false
  const perms = user.permissions
  if (isLimited(user)) {
    if (x.children) return true
    if (ACCESS_PAGES.some((p) => p.key === x.to)) return pageLevel(user, x.to) !== 'none'
    return !x.perm || !!perms?.includes(x.perm) || !!perms?.includes(`${x.perm}:view`)
  }
  return !x.perm || !perms || perms.includes(x.perm)
}
