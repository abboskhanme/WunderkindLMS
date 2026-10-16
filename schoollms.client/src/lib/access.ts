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

/**
 * Pages that also edit data behind ANOTHER server section. Granting the page emits every
 * listed section key too (employees-unified.md: Boshqaruv → Xodimlar lists and edits
 * teachers, whose API sits behind `teachers`).
 */
const PAGE_EXTRA_SECTIONS: Record<string, readonly string[]> = {
  '/admin/boshqaruv/staff': ['teachers'],
  // Qizil ro'yxat (2026-10-15): the page reads attendance; "To'liq" also works the queue.
  '/admin/attendance/red-list': ['attendanceCases'],
}

/** Every server section a page grant carries: its own plus {@link PAGE_EXTRA_SECTIONS}. */
export function sectionsOfPage(page: AccessPage): readonly string[] {
  return [page.section, ...(PAGE_EXTRA_SECTIONS[page.key] ?? [])]
}

/**
 * Menu pages that were removed; a grant saved for one keeps opening its successor at
 * runtime (the role editor does not carry it over — the superadmin decides on next save).
 * `/admin/teachers` (HR → O'qituvchilar) merged into Boshqaruv → Xodimlar, 2026-09-26.
 */
const RETIRED_PAGES: Record<string, string> = {
  '/admin/teachers': '/admin/boshqaruv/staff',
}

const GRANT_SEP = '|'

/**
 * AI ulanish ruxsati (docs/modules/mcp-readonly.md). Menyu sahifasi EMAS — rol oynasidagi
 * alohida belgi. Rolning `permissions` ro'yxatiga shu kalit qo'shiladi; server
 * (`McpAccess.PermissionKey`) faqat shu kalitli xodimga AI (MCP) tokenini beradi.
 * Sahifa yozuvi ham, bo'lim kaliti ham emas, shuning uchun `grantsOf`/`encodeGrants` unga tegmaydi.
 */
export const AI_ACCESS_KEY = 'aiAccess'

/**
 * Dars tahlili: mezon qo'shish/tahrirlash (mijoz, 2026-09-29) — AI belgisi kabi rol oynasidagi
 * alohida belgi. Server: `LessonAnalysisAccess.CriteriaPermissionKey`. «Dars tahlili» sahifasi
 * «To'liq» bo'lgandagina ishlaydi (yozish darvozasi). Admin/superadmin'ga kerak emas.
 */
export const LA_CRITERIA_KEY = 'lessonAnalysisCriteria'

/**
 * Yotoqxona komendanti jinsi bo'yicha (mijoz, 2026-10-15: "ayol va erkak komendant davomat qila olishi
 * uchun"). Rol oynasidagi alohida tanlov — «Yotoqxona» sahifasi berilganda chiqadi. Kalit faqat TORAYTIRADI:
 * egasi yotoqxonaning faqat qizlar yoki faqat o'g'il bolalar qismini ko'radi va belgilaydi. Server:
 * `DormGender` (Domain). Ikkalasi ham yoki hech biri — butun yotoqxona.
 */
export const DORM_PAGE = '/admin/attendance/boarding?session=dorm'
export const DORM_GIRLS_KEY = 'dormGirlsOnly'
export const DORM_BOYS_KEY = 'dormBoysOnly'
export type DormGender = 'all' | 'female' | 'male'

export function dormGenderOf(permissions: readonly string[]): DormGender {
  const girls = permissions.includes(DORM_GIRLS_KEY)
  const boys = permissions.includes(DORM_BOYS_KEY)
  return girls === boys ? 'all' : girls ? 'female' : 'male'
}

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
    for (const section of sectionsOfPage(page)) {
      const prev = sections.get(section)
      if (prev !== 'edit') sections.set(section, level)
    }
  }
  for (const [section, level] of sections) out.push(level === 'edit' ? section : `${section}:view`)
  return out
}

/** Xodim cheklanadimi: faqat `staff`. Admin/direktor/boshqalar — bu tizimdan tashqari. */
const isLimited = (user: User | null) => user?.role === 'staff'

const LEVEL_RANK: Record<AccessLevel, number> = { none: 0, view: 1, edit: 2 }
const higher = (a: AccessLevel, b: AccessLevel): AccessLevel => (LEVEL_RANK[b] > LEVEL_RANK[a] ? b : a)

/** Level a raw section key list gives for one section (`finance` → edit, `finance:view` → view). */
function sectionLevel(permissions: readonly string[], section: string): AccessLevel {
  if (permissions.includes(section)) return 'edit'
  if (permissions.includes(`${section}:view`)) return 'view'
  return 'none'
}

/**
 * Does the user hold a server section key (full or view-only)? Mirrors `RequirePerm`:
 * roles without a permission list (admin, director) hold every section.
 */
export function hasSection(user: User | null, section: string): boolean {
  if (!user) return false
  if (!user.permissions) return true
  return sectionLevel(user.permissions, section) !== 'none'
}

/** Menyudagi bitta sahifaning darajasi. */
export function pageLevel(user: User | null, pageKey: string): AccessLevel {
  if (!user) return 'none'
  if (!isLimited(user)) return 'edit'
  const permissions = user.permissions ?? []
  const grants = grantsOf(permissions)
  let level = grants.get(pageKey) ?? 'none'
  // A grant for a retired page still opens the page that replaced it.
  for (const [retired, successor] of Object.entries(RETIRED_PAGES)) {
    if (successor === pageKey) level = higher(level, grants.get(retired) ?? 'none')
  }
  // Legacy form (section keys only): every page of a section is open, so a page that also
  // carries an extra section opens for that section too — e.g. a `teachers`-only legacy
  // role keeps its teacher list, which now lives on Boshqaruv → Xodimlar.
  if (!permissions.some(isPageGrant)) {
    for (const section of PAGE_EXTRA_SECTIONS[pageKey] ?? []) {
      level = higher(level, sectionLevel(permissions, section))
    }
  }
  return level
}

/** Boshqaruv → Rollar. */
export const ROLES_PAGE = '/admin/boshqaruv/roles'

/**
 * May the user assign access roles to staff and teachers (client, 2026-10-12: "rolelarga dostupi bor
 * odam ham role biriktira olsin") — mirror of the server's `RoleAssignment.CanAssign`: superadmin and
 * admin always; a staff member with the Rollar page at the full level. Editing roles stays superadmin-only.
 */
export function canAssignRoles(user: User | null): boolean {
  if (!user) return false
  if (user.role === 'superadmin' || user.role === 'admin') return true
  return (
    user.role === 'staff' &&
    sectionLevel(user.permissions ?? [], 'staff') === 'edit' &&
    pageLevel(user, ROLES_PAGE) === 'edit'
  )
}

/**
 * Every section key of `permissions` is one the user holds at that level (server: `RoleAssignment.Within`)
 * — a role assigner never hands out more than they have. Page grants carry no server access.
 */
export function withinUserRights(user: User | null, permissions: readonly string[]): boolean {
  if (!user) return false
  if (user.role === 'superadmin' || user.role === 'admin') return true
  const own = user.permissions ?? []
  return permissions
    // Gender flags only narrow the dorm key — anyone who may hand out the dorm may hand them out.
    .filter((k) => !isPageGrant(k) && k !== DORM_GIRLS_KEY && k !== DORM_BOYS_KEY)
    .every((k) =>
      k.endsWith(':view') ? sectionLevel(own, k.slice(0, -':view'.length)) !== 'none' : sectionLevel(own, k) === 'edit',
    )
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
