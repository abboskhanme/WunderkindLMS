/**
 * Homework completion statuses (docs/modules/homework-completion.md, client 2026-10-15).
 *
 * The Mini App is its own Vite app, so it keeps this small copy of the web side's
 * `schoollms.client/src/lib/homeworkStatus.ts` (server: `SchoolLms.Domain/HomeworkStatus.cs`).
 * Change all three together. 1 and 2 are the old "qildi" / "qilmadi" — their meaning is frozen.
 */

export const HW_NONE = 0
export const HW_DONE = 1

/** Legend order — the client's: + − * K Y. `tone` is the colour of the symbol when not selected. */
export const HOMEWORK_STATUSES = [
  { value: 1, symbol: '+', label: 'Bajardi', tone: 'text-emerald-600' },
  { value: 3, symbol: '−', label: 'Chala', tone: 'text-amber-600' },
  { value: 2, symbol: '*', label: "Umuman yo'q", tone: 'text-red-600' },
  { value: 4, symbol: 'K', label: 'Kech qoldi', tone: 'text-violet-600' },
  { value: 5, symbol: 'Y', label: 'Kelmadi', tone: 'text-slate-500' },
]
