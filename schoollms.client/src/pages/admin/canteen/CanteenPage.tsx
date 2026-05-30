import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, ChevronLeft, ChevronRight, Utensils } from 'lucide-react'
import type { DayMenu, Dish, MealType } from '@/types'
import {
  getDayMenu,
  getMenuRange,
  createDish,
  updateDish,
  deleteDish,
  type DishPayload,
} from '@/api/services/canteen'
import { mealOrder, mealLabels } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'
import { addDaysISO, mondayOfISO } from '@/lib/weeks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DishFormModal } from './DishFormModal'

type View = 'daily' | 'weekly' | 'monthly'

const weekdayNames = [
  'Yakshanba',
  'Dushanba',
  'Seshanba',
  'Chorshanba',
  'Payshanba',
  'Juma',
  'Shanba',
]
const monthNames = [
  'Yanvar',
  'Fevral',
  'Mart',
  'Aprel',
  'May',
  'Iyun',
  'Iyul',
  'Avgust',
  'Sentabr',
  'Oktabr',
  'Noyabr',
  'Dekabr',
]

const pad = (n: number) => String(n).padStart(2, '0')

function todayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

const weekdayName = (iso: string) => weekdayNames[new Date(iso).getDay()] ?? ''
const dishCount = (m: DayMenu) =>
  m.meals.breakfast.length + m.meals.lunch.length + m.meals.dinner.length

export function CanteenPage() {
  const [view, setView] = useState<View>('daily')
  const [anchor, setAnchor] = useState(todayISO())
  const [menus, setMenus] = useState<DayMenu[]>([])
  const [loading, setLoading] = useState(true)
  const [form, setForm] = useState<{ meal: MealType; dish: Dish | null } | null>(null)

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni tozalaymiz (maqsadli)
    setLoading(true)
    const load = () => {
      if (view === 'daily') return getDayMenu(anchor).then((m) => [m])
      if (view === 'weekly') {
        const ws = mondayOfISO(anchor)
        return getMenuRange(ws, addDaysISO(ws, 5))
      }
      const [y, m] = anchor.split('-').map(Number)
      const last = new Date(y, m, 0).getDate()
      return getMenuRange(`${y}-${pad(m)}-01`, `${y}-${pad(m)}-${pad(last)}`)
    }
    load()
      .then((ms) => active && setMenus(ms))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [view, anchor])

  const refreshDaily = () => getDayMenu(anchor).then((m) => setMenus([m]))

  const step = (dir: -1 | 1) => {
    if (view === 'daily') setAnchor(addDaysISO(anchor, dir))
    else if (view === 'weekly') setAnchor(addDaysISO(anchor, dir * 7))
    else {
      const [y, m] = anchor.split('-').map(Number)
      const d = new Date(y, m - 1 + dir, 1)
      setAnchor(`${d.getFullYear()}-${pad(d.getMonth() + 1)}-01`)
    }
  }

  const navLabel = () => {
    if (view === 'daily') return `${weekdayName(anchor)}, ${formatDate(anchor)}`
    if (view === 'weekly') {
      const ws = mondayOfISO(anchor)
      return `${formatDate(ws).slice(0, 5)} – ${formatDate(addDaysISO(ws, 5)).slice(0, 5)}`
    }
    const [y, m] = anchor.split('-').map(Number)
    return `${monthNames[m - 1]} ${y}`
  }

  const openToDay = (date: string) => {
    setAnchor(date)
    setView('daily')
  }

  const handleSubmit = (payload: DishPayload) => {
    if (!form) return
    const action = form.dish
      ? updateDish(anchor, form.meal, form.dish.id, payload)
      : createDish(anchor, form.meal, payload)
    action.then(refreshDaily)
    setForm(null)
  }

  const handleDelete = (meal: MealType, dish: Dish) => {
    if (!confirm(`"${dish.name}" o'chirilsinmi?`)) return
    deleteDish(anchor, meal, dish.id).then(refreshDaily)
  }

  const day = menus[0] ?? {
    date: anchor,
    meals: { breakfast: [], lunch: [], dinner: [] },
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Oshxona</h1>
          <p className="text-sm text-slate-400">Kunlik 3 mahal menyu</p>
        </div>
        <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
          {(['daily', 'weekly', 'monthly'] as View[]).map((v) => (
            <button
              key={v}
              type="button"
              onClick={() => setView(v)}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                v === view ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {v === 'daily' ? 'Kunlik' : v === 'weekly' ? 'Haftalik' : 'Oylik'}
            </button>
          ))}
        </div>
      </div>

      {/* Sana navigatsiyasi */}
      <div className="flex items-center gap-2">
        <button
          onClick={() => step(-1)}
          className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[160px] text-center text-sm font-medium text-slate-700">
          {navLabel()}
        </span>
        <button
          onClick={() => step(1)}
          className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : view === 'daily' ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          {mealOrder.map((meal) => (
            <Card key={meal}>
              <div className="mb-3 flex items-center justify-between">
                <h2 className="font-semibold text-slate-800">{mealLabels[meal]}</h2>
                <Button variant="secondary" onClick={() => setForm({ meal, dish: null })}>
                  <Plus className="h-4 w-4" /> Ovqat
                </Button>
              </div>
              <div className="space-y-2">
                {day.meals[meal].map((dish) => (
                  <div
                    key={dish.id}
                    className="flex gap-3 rounded-xl border border-slate-100 p-2"
                  >
                    {dish.imageUrl ? (
                      <img
                        src={dish.imageUrl}
                        alt=""
                        className="h-14 w-14 shrink-0 rounded-lg object-cover"
                      />
                    ) : (
                      <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-slate-300">
                        <Utensils className="h-5 w-5" />
                      </div>
                    )}
                    <div className="min-w-0 flex-1">
                      <p className="font-medium text-slate-800">{dish.name}</p>
                      <p className="line-clamp-2 text-xs text-slate-400">{dish.ingredients}</p>
                    </div>
                    <div className="flex flex-col gap-0.5">
                      <IconBtn
                        icon={Pencil}
                        title="Tahrirlash"
                        onClick={() => setForm({ meal, dish })}
                      />
                      <IconBtn
                        icon={Trash2}
                        title="O'chirish"
                        danger
                        onClick={() => handleDelete(meal, dish)}
                      />
                    </div>
                  </div>
                ))}
                {day.meals[meal].length === 0 && (
                  <p className="py-4 text-center text-sm text-slate-400">Ovqat yo'q</p>
                )}
              </div>
            </Card>
          ))}
        </div>
      ) : view === 'weekly' ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {menus.map((m) => (
            <button
              key={m.date}
              onClick={() => openToDay(m.date)}
              className="rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition-colors hover:border-brand-300"
            >
              <p className="mb-2 font-semibold text-slate-800">
                {weekdayName(m.date)}{' '}
                <span className="text-xs font-normal text-slate-400">
                  {formatDate(m.date).slice(0, 5)}
                </span>
              </p>
              {mealOrder.map((meal) => (
                <p key={meal} className="text-xs">
                  <span className="font-medium text-slate-500">{mealLabels[meal]}: </span>
                  <span className="text-slate-600">
                    {m.meals[meal].map((d) => d.name).join(', ') || '—'}
                  </span>
                </p>
              ))}
            </button>
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-5 lg:grid-cols-7">
          {menus.map((m) => {
            const count = dishCount(m)
            return (
              <button
                key={m.date}
                onClick={() => openToDay(m.date)}
                className={cn(
                  'rounded-xl border p-3 text-center transition-colors hover:border-brand-300',
                  count > 0 ? 'border-brand-100 bg-brand-50/40' : 'border-slate-200 bg-white',
                )}
              >
                <p className="text-lg font-semibold text-slate-800">{Number(m.date.slice(8))}</p>
                <p className="text-xs text-slate-400">{count > 0 ? `${count} taom` : '—'}</p>
              </button>
            )
          })}
        </div>
      )}

      <DishFormModal
        open={!!form}
        title={form ? `${mealLabels[form.meal]} — ovqat` : ''}
        initial={form?.dish}
        onClose={() => setForm(null)}
        onSubmit={handleSubmit}
      />
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
