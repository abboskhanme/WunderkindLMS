import type { Tenant } from './api'

export const STATUS_LABEL: Record<Tenant['status'], string> = {
  active: 'Faol',
  provisioning: 'Tayyorlanmoqda',
  suspended: "To'xtatilgan",
}

export function StatusBadge({ status }: { status: Tenant['status'] }) {
  return (
    <span className={`cp-badge ${status}`}>
      <span className="cp-dot" />{STATUS_LABEL[status]}
    </span>
  )
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]?.toUpperCase()).join('') || '?'
}

export function formatDate(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('uz', { year: 'numeric', month: 'short', day: 'numeric' })
}
