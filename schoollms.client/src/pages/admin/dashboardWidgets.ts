/**
 * Bosh sahifa vidjetlari — ro'yxat va har foydalanuvchining tanlovi.
 *
 * Mijoz, 2026-09-22: EduSchool'dagi "Vidjetlar" paneli — o'sha ro'yxat, o'sha
 * guruhlar, o'sha tartib (ularning 25 tasi), oxirida esa bizning bosh sahifada
 * ilgaridan bor bloklar ("Davomat va o'quv").
 *
 * IKKI XIL CHEKLOV:
 * - `requires` — foydalanuvchida ruxsat yo'q bo'lsa vidjet panelda UMUMAN
 *   ko'rinmaydi (moliya hisobotlari server tomonda faqat admin/direktor uchun;
 *   lidlar — `leads` ruxsati).
 * - `unavailable` — bizda bunday ma'lumot yo'q. Panelda ko'rinadi, lekin
 *   yoqib bo'lmaydi va sababi yoziladi: bo'sh karta ko'rsatgandan ko'ra
 *   rostini aytgan yaxshi.
 *
 * TANLOV QAYERDA SAQLANADI: brauzerda (`localStorage`), foydalanuvchi id'si
 * bilan; faqat standartdan farq qilgan qiymatlar (`{ id: true|false }`) —
 * keyin qo'shilgan vidjet eski tanlovi bor foydalanuvchida ham o'z standart
 * holatida chiqadi. docs/ASSUMPTIONS.md, 2026-09-22.
 */
import { useCallback, useMemo, useState } from 'react'

export type WidgetGroup =
  | 'main'
  | 'monitoring'
  | 'finance'
  | 'leads'
  | 'management'
  | 'contingent'
  | 'ours'

export type WidgetAccess = 'finance' | 'leads'

export interface WidgetDef {
  id: string
  label: string
  group: WidgetGroup
  /** Standart holatda ko'rsatiladimi. */
  defaultOn: boolean
  requires?: WidgetAccess
  /** Bizda bu ma'lumot yo'q — sababi. */
  unavailable?: string
}

export const WIDGET_GROUPS: { key: WidgetGroup; label: string }[] = [
  { key: 'main', label: "Asosiy ko'rsatkichlar" },
  { key: 'monitoring', label: 'Kunlik monitoring' },
  { key: 'finance', label: 'Moliya' },
  { key: 'leads', label: 'Lidlar' },
  { key: 'management', label: 'Boshqaruv' },
  { key: 'contingent', label: 'Kontingent' },
  { key: 'ours', label: "Davomat va o'quv" },
]

export const WIDGETS: WidgetDef[] = [
  // EduSchool'da standart holatda yoqilgan sakkiztasi — shu yerda ham yoqilgan.
  { id: 'students', label: "Jami o'quvchilar", group: 'main', defaultOn: true },
  { id: 'unassigned', label: "Sinfga qo'shilmagan o'quvchilar", group: 'main', defaultOn: true },
  { id: 'leftFromClass', label: "Sinfdan chiqarilgan o'quvchilar", group: 'main', defaultOn: true },
  { id: 'classes', label: 'Jami sinflar', group: 'main', defaultOn: true },
  { id: 'active', label: "Aktiv o'quvchilar", group: 'main', defaultOn: false },
  { id: 'waiting', label: "Kutayotgan o'quvchilar", group: 'main', defaultOn: false },
  { id: 'archived', label: "Arxiv o'quvchilar", group: 'main', defaultOn: true },
  { id: 'credit', label: 'Haqdorlar', group: 'main', defaultOn: true },
  { id: 'debtors', label: 'Qarzdorlar', group: 'main', defaultOn: true },
  { id: 'firstPayment', label: "Birinchi to'lov qilgan o'quvchilar", group: 'main', defaultOn: true },

  {
    id: 'debtBlocked',
    label: 'Qarz tufayli bloklangan',
    group: 'monitoring',
    defaultOn: false,
    unavailable: "Bizda qarz uchun bloklash yo'q",
  },
  { id: 'debtDynamics', label: 'Qarzdorlik dinamikasi', group: 'monitoring', defaultOn: false, requires: 'finance' },

  { id: 'todayIncome', label: 'Bugungi kirim', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'todayExpense', label: 'Bugungi chiqim', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'cashBalance', label: 'Kassa balansi', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'monthlyFlow', label: 'Oylik kirim-chiqim', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'financialState', label: 'Moliyaviy holat', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'debtState', label: 'Qarzdorlik holati', group: 'finance', defaultOn: false, requires: 'finance' },
  { id: 'coverage', label: 'Abonement qamrovi', group: 'finance', defaultOn: false, requires: 'finance' },

  { id: 'leadFunnel', label: 'Lidlar voronkasi', group: 'leads', defaultOn: false, requires: 'leads' },

  { id: 'bonusPenalty', label: 'Bonus / Jarima ulushi', group: 'management', defaultOn: false, requires: 'finance' },
  {
    id: 'openTasks',
    label: 'Ochiq topshiriqlar',
    group: 'management',
    defaultOn: false,
    unavailable: "Xodimlar topshiriqlari moduli hali yo'q",
  },

  { id: 'contingent', label: 'Kontingent tarkibi', group: 'contingent', defaultOn: false },
  { id: 'gender', label: "Jins bo'yicha", group: 'contingent', defaultOn: false },
  { id: 'classBreakdown', label: 'Sinflar kesimi', group: 'contingent', defaultOn: false },

  // Bizning bosh sahifada ilgaridan bor bloklar — standart holatda o'z joyida.
  { id: 'attendanceByPeriod', label: 'Davomat analitikasi', group: 'ours', defaultOn: true },
  { id: 'absentStudents', label: "Dars qoldirayotgan o'quvchilar", group: 'ours', defaultOn: true },
  { id: 'teachers', label: "O'qituvchilar", group: 'ours', defaultOn: true },
  { id: 'averageGrade', label: "O'rtacha baho", group: 'ours', defaultOn: true },
  { id: 'attendanceRate', label: 'Umumiy davomat', group: 'ours', defaultOn: true },
  { id: 'todaySchedule', label: 'Bugungi dars jadvali', group: 'ours', defaultOn: true },
  { id: 'classChart', label: "Sinflar bo'yicha statistika", group: 'ours', defaultOn: true },
  { id: 'topClasses', label: 'Eng yuqori bahoga ega sinflar', group: 'ours', defaultOn: true },
]

type Overrides = Record<string, boolean>

const storageKey = (userId: string) => `dashboard:widgets:${userId}`

function load(userId: string): Overrides {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey(userId)) ?? '{}')
    return parsed && typeof parsed === 'object' ? (parsed as Overrides) : {}
  } catch {
    return {}
  }
}

function save(userId: string, overrides: Overrides) {
  try {
    if (Object.keys(overrides).length === 0) localStorage.removeItem(storageKey(userId))
    else localStorage.setItem(storageKey(userId), JSON.stringify(overrides))
  } catch {
    // Brauzer xotirasi yopiq (maxfiy oyna) — tanlov shu sessiya davomida ishlaydi.
  }
}

export interface DashboardWidgets {
  /** Shu foydalanuvchiga ko'rinadigan vidjetlar (ruxsati yo'qlari chiqarilgan). */
  visible: WidgetDef[]
  isOn: (id: string) => boolean
  set: (ids: string[], on: boolean) => void
  reset: () => void
  shownCount: number
}

export function useDashboardWidgets(
  userId: string,
  access: Record<WidgetAccess, boolean>,
): DashboardWidgets {
  const [overrides, setOverrides] = useState<Overrides>(() => load(userId))

  const visible = useMemo(
    () => WIDGETS.filter((w) => !w.requires || access[w.requires]),
    [access],
  )

  const isOn = useCallback(
    (id: string) => {
      const def = visible.find((w) => w.id === id)
      if (!def || def.unavailable) return false
      return overrides[id] ?? def.defaultOn
    },
    [overrides, visible],
  )

  const set = useCallback(
    (ids: string[], on: boolean) => {
      setOverrides((prev) => {
        const next = { ...prev }
        for (const id of ids) {
          const def = WIDGETS.find((w) => w.id === id)
          if (!def || def.unavailable) continue
          // Standartga teng qiymat saqlanmaydi — yuqoridagi izohga qarang.
          if (on === def.defaultOn) delete next[id]
          else next[id] = on
        }
        save(userId, next)
        return next
      })
    },
    [userId],
  )

  const reset = useCallback(() => {
    save(userId, {})
    setOverrides({})
  }, [userId])

  const shownCount = visible.filter((w) => isOn(w.id)).length

  return { visible, isOn, set, reset, shownCount }
}
