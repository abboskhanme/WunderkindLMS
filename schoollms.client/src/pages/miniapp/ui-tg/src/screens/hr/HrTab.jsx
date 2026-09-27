/**
 * O'QITUVCHI PANELIDAGI "Topshiriqlar" tabi — HR xodimi bo'lgan o'qituvchi uchun
 * (hr-tasks.md §8).
 *
 * Pastki panelda allaqachon beshta tab turibdi, shuning uchun "Natijam" alohida
 * tab EMAS: shu tabning sariq hero'sidagi o'tkagich ("Topshiriqlar / Natijam").
 * Ekranlar xodim panelidagining o'zi.
 */
import { useState } from 'react'
import { Segmented } from '../../components/ui'
import { hapticSelect } from '../../lib/telegram'
import { SummaryScreen } from './SummaryScreen'
import { TasksScreen } from './TasksScreen'
import { t } from './texts'

const VIEWS = [
  { value: 'tasks', label: t('nav.tasks') },
  { value: 'summary', label: t('nav.summary') },
]

export function HrTab({ me }) {
  const [view, setView] = useState('tasks')
  const top = (
    <Segmented
      value={view}
      options={VIEWS}
      onChange={(next) => {
        if (next === view) return
        hapticSelect()
        setView(next)
      }}
    />
  )
  return view === 'tasks' ? <TasksScreen me={me} heroTop={top} /> : <SummaryScreen heroTop={top} />
}
