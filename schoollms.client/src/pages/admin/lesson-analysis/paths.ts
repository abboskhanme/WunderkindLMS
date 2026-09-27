/** Boshqaruv → Dars tahlili marshrutlari (docs/modules/lesson-analysis.md §7). */
export const LA_BASE = '/admin/boshqaruv/dars-tahlili'

export const laPaths = {
  dashboard: LA_BASE,
  list: `${LA_BASE}/tahlillar`,
  create: (teacherId?: string) => `${LA_BASE}/tahlillar/yangi${teacherId ? `?teacherId=${encodeURIComponent(teacherId)}` : ''}`,
  detail: (id: string) => `${LA_BASE}/tahlillar/${id}`,
  edit: (id: string) => `${LA_BASE}/tahlillar/${id}/tahrir`,
  rating: `${LA_BASE}/reyting`,
  teacher: (teacherId: string) => `${LA_BASE}/oqituvchi/${teacherId}`,
  criteria: `${LA_BASE}/mezonlar`,
}

/** O'qituvchi portali. */
export const TEACHER_LA_BASE = '/teacher/dars-tahlili'
