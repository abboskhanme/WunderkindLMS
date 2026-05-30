import type { Stage } from '@/types'

// Standart ustunlar (id'lar leads mock'idagi stage qiymatlariga mos)
export const stagesMock: Stage[] = [
  { id: 'new', title: 'Yangi', color: 'slate' },
  { id: 'contacted', title: "Bog'lanildi", color: 'blue' },
  { id: 'interview', title: 'Suhbat', color: 'amber' },
  { id: 'enrolled', title: 'Qabul qilindi', color: 'emerald' },
]
