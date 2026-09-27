/** HR → TOPSHIRIQLAR routes (docs/modules/hr-tasks.md §8). */
export const HRT_BASE = '/admin/hr/topshiriqlar'

export const hrtPaths = {
  dashboard: HRT_BASE,
  tasks: `${HRT_BASE}/royxat`,
  create: (employeeId?: string) =>
    `${HRT_BASE}/royxat/yangi${employeeId ? `?employeeId=${encodeURIComponent(employeeId)}` : ''}`,
  /** Recurring tasks for many employees (batch) — opens with the "Takroriy" switch on. */
  createRecurring: `${HRT_BASE}/royxat/yangi?takroriy=1`,
  templates: `${HRT_BASE}/takroriy`,
  employees: `${HRT_BASE}/xodimlar`,
  reports: `${HRT_BASE}/hisobotlar`,
  settings: `${HRT_BASE}/sozlamalar`,
  /** For every HR employee — their own tasks (`/api/hr/me/*`). */
  myTasks: '/admin/hr/mening-topshiriqlarim',
}

/** Link to the task list (from the dashboard and reports). */
export function hrtTasksLink(params: Record<string, string | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) if (value) search.set(key, value)
  const qs = search.toString()
  return qs ? `${hrtPaths.tasks}?${qs}` : hrtPaths.tasks
}
