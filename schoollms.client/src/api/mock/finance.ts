import type { FinanceTransaction } from '@/types'

// Mock rejim uchun moliyaviy amallar (backend seed bilan mos).
export const financeMock: FinanceTransaction[] = [
  // Mart
  { id: 'f-03-1', date: '2026-03-05', direction: 'expense', category: 'salary', amount: 25_000_000, note: "O'qituvchilar oyligi" },
  { id: 'f-03-2', date: '2026-03-10', direction: 'expense', category: 'utilities', amount: 4_200_000, note: 'Kommunal to\'lovlar' },
  { id: 'f-03-3', date: '2026-03-12', direction: 'expense', category: 'supplies', amount: 1_800_000, note: 'O\'quv materiallari' },
  { id: 'f-03-4', date: '2026-03-08', direction: 'income', category: 'tuition', amount: 1_100_000, note: "O'quvchi to'lovi — Aziz Karimov", studentId: 's1', studentName: 'Aziz Karimov' },
  { id: 'f-03-5', date: '2026-03-08', direction: 'income', category: 'tuition', amount: 1_100_000, note: "O'quvchi to'lovi — Malika Yusupova", studentId: 's2', studentName: 'Malika Yusupova' },
  // Aprel
  { id: 'f-04-1', date: '2026-04-05', direction: 'expense', category: 'salary', amount: 25_000_000, note: "O'qituvchilar oyligi" },
  { id: 'f-04-2', date: '2026-04-10', direction: 'expense', category: 'utilities', amount: 4_200_000, note: 'Kommunal to\'lovlar' },
  { id: 'f-04-3', date: '2026-04-20', direction: 'income', category: 'rent_in', amount: 3_000_000, note: 'Sport zal ijarasi' },
  { id: 'f-04-4', date: '2026-04-08', direction: 'income', category: 'tuition', amount: 1_000_000, note: "O'quvchi to'lovi — Sherzod Rahimov", studentId: 's3', studentName: 'Sherzod Rahimov' },
  { id: 'f-04-5', date: '2026-04-08', direction: 'income', category: 'tuition', amount: 950_000, note: "O'quvchi to'lovi — Gulnoza Toshmatova", studentId: 's4', studentName: 'Gulnoza Toshmatova' },
  // May
  { id: 'f-05-1', date: '2026-05-05', direction: 'expense', category: 'salary', amount: 25_000_000, note: "O'qituvchilar oyligi" },
  { id: 'f-05-2', date: '2026-05-10', direction: 'expense', category: 'utilities', amount: 4_200_000, note: 'Kommunal to\'lovlar' },
  { id: 'f-05-3', date: '2026-05-03', direction: 'expense', category: 'repair', amount: 5_500_000, note: 'Sinf xonasi ta\'miri' },
  { id: 'f-05-4', date: '2026-05-15', direction: 'income', category: 'donation', amount: 10_000_000, note: 'Homiy yordami' },
  { id: 'f-05-5', date: '2026-05-08', direction: 'income', category: 'tuition', amount: 900_000, note: "O'quvchi to'lovi — Jahongir Aliyev", studentId: 's5', studentName: 'Jahongir Aliyev' },
]
