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
