import type { Dish, MealType } from '@/types'

// date -> 3 mahal ovqatlar. Namuna uchun bugun (2026-05-20).
export const canteenMock: Record<string, Record<MealType, Dish[]>> = {
  '2026-05-20': {
    breakfast: [
      { id: 'd1', name: "Sutli bo'tqa", ingredients: "Guruch, sut, shakar, sariyog'" },
      { id: 'd2', name: 'Tuxum', ingredients: 'Tovuq tuxumi' },
    ],
    lunch: [
      { id: 'd3', name: 'Mastava', ingredients: "Go'sht, guruch, sabzi, kartoshka, piyoz" },
      { id: 'd4', name: 'Non', ingredients: "Bug'doy uni" },
    ],
    dinner: [{ id: 'd5', name: 'Makaron', ingredients: "Makaron, go'sht, pomidor" }],
  },
}
