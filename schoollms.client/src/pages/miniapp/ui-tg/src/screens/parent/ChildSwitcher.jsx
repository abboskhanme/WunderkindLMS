/**
 * FARZAND ALMASHTIRGICH — sariq hero ichida.
 *
 * UCHTA HOLAT, UCHTA KO'RINISH:
 *   1 farzand   → hech narsa chiqmaydi (ismi hero sarlavhasida turibdi;
 *                 bosib bo'lmaydigan "o'tkagich" faqat chalg'itadi);
 *   2 farzand   → `Segmented` — ikkalasi ham ko'rinib turadi, bitta bosish;
 *   3 va undan  → gorizontal chip qatori. Qator O'Z ICHIDA suriladi
 *   ko'p          (`overflow-x-auto`), sahifa esa qimirlamaydi — telefonda
 *                 gorizontal scroll eng tez asabiylashtiradigan narsa.
 *
 * Tanlov bu yerda SAQLANMAYDI: u qobiqda (`ParentPanel`) turadi, chunki
 * beshta tab ham o'sha bitta qiymatni o'qiydi.
 */
import { haptic } from '../../lib/telegram'
import { Segmented } from '../../components/ui'

/** Uzun ism chipda sig'masin uchun: "Abdullayev Amir Sardorovich" → "Amir A." */
function shortName(fullName) {
  const parts = String(fullName ?? '').trim().split(/\s+/)
  if (parts.length < 2) return parts[0] ?? ''
  return `${parts[1]} ${parts[0].charAt(0)}.`
}

export function ChildSwitcher({ items, value, onChange }) {
  if (items.length < 2) return null

  const pick = (id) => {
    if (id === value) return
    haptic('light')
    onChange(id)
  }

  if (items.length === 2) {
    return (
      <Segmented
        value={value}
        onChange={pick}
        options={items.map((c) => ({ value: c.id, label: shortName(c.fullName) }))}
      />
    )
  }

  return (
    <div className="-mx-4 overflow-x-auto px-4">
      <div className="flex w-max gap-2">
        {items.map((c) => {
          const active = c.id === value
          return (
            <button
              key={c.id}
              type="button"
              onClick={() => pick(c.id)}
              aria-pressed={active}
              className={
                'shrink-0 rounded-2xl px-4 py-2.5 text-[14px] font-semibold transition ' +
                (active ? 'bg-brand-ink text-brand' : 'bg-brand-dark/60 text-brand-ink/70')
              }
            >
              {shortName(c.fullName)}
              {c.className && (
                <span className="ml-1.5 text-[12px] font-normal opacity-70">{c.className}</span>
              )}
            </button>
          )
        })}
      </div>
    </div>
  )
}
