/**
 * Joriy host'dan tenant (maktab)ni aniqlaydi.
 *  - asosiy domen (lvh.me, app.lvh.me, localhost, ...) → Control Plane (loyiha boshlig'i).
 *  - <slug>.lvh.me / <slug>.localhost / <slug>.<root> → o'sha maktab LMS'i.
 *
 * Dev'da `?tenant=school1` query bilan ham majburlash mumkin.
 * Prod root domenni `VITE_ROOT_DOMAIN` orqali qo'shing (masalan "maktab.uz").
 */
export interface TenantInfo {
  isPlatform: boolean
  slug: string | null
}

const ROOT_DOMAINS = ['lvh.me', 'nip.io', import.meta.env.VITE_ROOT_DOMAIN]
  .filter((d): d is string => !!d)

// Tenant emas, asosiy domenga tegishli subdomenlar.
const PLATFORM_SUBS = ['www', 'app', 'admin']

function asSub(prefix: string): TenantInfo {
  if (!prefix || prefix.includes('.') || PLATFORM_SUBS.includes(prefix)) {
    return { isPlatform: true, slug: null }
  }
  return { isPlatform: false, slug: prefix.toLowerCase() }
}

export function detectTenant(): TenantInfo {
  const q = new URLSearchParams(window.location.search).get('tenant')
  if (q) return { isPlatform: false, slug: q.toLowerCase() }

  const host = window.location.hostname
  if (host === 'localhost' || /^[\d.]+$/.test(host)) return { isPlatform: true, slug: null }

  if (host.endsWith('.localhost')) return asSub(host.slice(0, -'.localhost'.length))

  for (const root of ROOT_DOMAINS) {
    if (host === root) return { isPlatform: true, slug: null }
    if (host.endsWith('.' + root)) return asSub(host.slice(0, -(root.length + 1)))
  }

  // Noma'lum domen: 3+ bo'lakli bo'lsa birinchi bo'lak subdomen deb qaraladi.
  const labels = host.split('.')
  if (labels.length >= 3) return asSub(labels[0])
  return { isPlatform: true, slug: null }
}

/** Berilgan slug uchun maktab manzili (joriy port/protokol saqlanadi). */
export function tenantUrl(slug: string): string {
  const { protocol, host, hostname, port } = window.location
  // Joriy host'dan tenant prefiksini olib tashlab, root domenni topamiz.
  let root = hostname
  if (hostname.endsWith('.localhost')) root = 'localhost'
  for (const r of [...ROOT_DOMAINS, 'localhost']) {
    if (hostname === r || hostname.endsWith('.' + r)) { root = r; break }
  }
  const portPart = port ? `:${port}` : ''
  void host
  return `${protocol}//${slug}.${root}${portPart}`
}
