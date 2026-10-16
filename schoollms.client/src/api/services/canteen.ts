import type { DayMenu, Dish, MealType } from '@/types'
import { delay, uid } from '@/lib/utils'
import { addDaysISO } from '@/lib/weeks'
import { api, USE_MOCK } from '../client'
import { canteenMock } from '../mock/canteen'

export interface DishPayload {
  name: string
  ingredients: string
  imageUrl?: string
}

const emptyMeals = (): Record<MealType, Dish[]> => ({
  breakfast: [],
  lunch: [],
  dinner: [],
})

export async function getDayMenu(date: string): Promise<DayMenu> {
  if (USE_MOCK) {
    await delay()
    return { date, meals: canteenMock[date] ?? emptyMeals() }
  }
  const { data } = await api.get<DayMenu>(`/admin/canteen/${date}`)
  return data
}

export async function getMenuRange(startISO: string, endISO: string): Promise<DayMenu[]> {
  if (USE_MOCK) {
    await delay()
    const res: DayMenu[] = []
    let cur = startISO
    while (cur <= endISO) {
      res.push({ date: cur, meals: canteenMock[cur] ?? emptyMeals() })
      cur = addDaysISO(cur, 1)
    }
    return res
  }
  const { data } = await api.get<DayMenu[]>('/admin/canteen', {
    params: { start: startISO, end: endISO },
  })
  return data
}

export async function createDish(
  date: string,
  meal: MealType,
  payload: DishPayload,
): Promise<Dish> {
  if (USE_MOCK) {
    await delay(150)
    const dish: Dish = { id: uid(), ...payload }
    const day = (canteenMock[date] ??= emptyMeals())
    day[meal] = [...day[meal], dish]
    return dish
  }
  const { data } = await api.post<Dish>(`/admin/canteen/${date}/${meal}`, payload)
  return data
}

export async function updateDish(
  date: string,
  meal: MealType,
  dishId: string,
  payload: DishPayload,
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    const day = canteenMock[date]
    if (day) day[meal] = day[meal].map((d) => (d.id === dishId ? { ...d, ...payload } : d))
    return
  }
  await api.put(`/admin/canteen/${date}/${meal}/${dishId}`, payload)
}

export async function deleteDish(date: string, meal: MealType, dishId: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    const day = canteenMock[date]
    if (day) day[meal] = day[meal].filter((d) => d.id !== dishId)
    return
  }
  await api.delete(`/admin/canteen/${date}/${meal}/${dishId}`)
}

/* ------------------------------------------------------------ copy a week */

/** Upper bound for "copy to several weeks" — same as the server's `CanteenWeekCopy.MaxWeeks`. */
export const MAX_COPY_WEEKS = 8

/** `POST /admin/canteen/menu/copy-week` — both dates are Mondays (docs/modules/canteen.md). */
export interface CopyWeekPayload {
  fromWeekStart: string
  toWeekStart: string
  /** Replace target days that already have a menu; without it the server answers 409. */
  overwrite: boolean
  /** Consecutive weeks, starting at `toWeekStart`, that receive the copy (1..MAX_COPY_WEEKS). */
  weeks: number
}

export interface CopyWeekResult {
  copied: number
  /** Source dishes the menu cannot show (unknown meal or no name) — not copied. */
  skipped: number
  /** Dishes removed from target days by `overwrite`. */
  replaced: number
  targetWeeks: string[]
  days: string[]
}

const dishTotal = (day: Record<MealType, Dish[]> | undefined) =>
  day ? day.breakfast.length + day.lunch.length + day.dinner.length : 0

export async function copyMenuWeek(payload: CopyWeekPayload): Promise<CopyWeekResult> {
  if (USE_MOCK) {
    await delay(200)
    const offsets = [0, 1, 2, 3, 4, 5, 6].filter(
      (i) => dishTotal(canteenMock[addDaysISO(payload.fromWeekStart, i)]) > 0,
    )
    const weeks = Array.from({ length: payload.weeks }, (_, w) => addDaysISO(payload.toWeekStart, w * 7))
    const days = weeks.flatMap((week) => offsets.map((i) => addDaysISO(week, i)))
    const filled = days.filter((date) => dishTotal(canteenMock[date]) > 0)
    if (filled.length > 0 && !payload.overwrite) {
      // Shaped like the axios 409 so the modal handles mock and server alike.
      const message = 'Bu kunlarda menyu allaqachon bor.'
      throw Object.assign(new Error(message), { response: { status: 409, data: { message, days: filled } } })
    }
    const result: CopyWeekResult = { copied: 0, skipped: 0, replaced: 0, targetWeeks: weeks, days }
    for (const week of weeks) {
      for (const i of offsets) {
        const date = addDaysISO(week, i)
        const src = canteenMock[addDaysISO(payload.fromWeekStart, i)]
        result.replaced += dishTotal(canteenMock[date])
        result.copied += dishTotal(src)
        canteenMock[date] = {
          breakfast: src.breakfast.map((d) => ({ ...d, id: uid() })),
          lunch: src.lunch.map((d) => ({ ...d, id: uid() })),
          dinner: src.dinner.map((d) => ({ ...d, id: uid() })),
        }
      }
    }
    return result
  }
  const { data } = await api.post<CopyWeekResult>('/admin/canteen/menu/copy-week', payload)
  return data
}
