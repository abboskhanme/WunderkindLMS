import { useMemo, useState } from 'react'
import { Search, Users, MessageSquare } from 'lucide-react'
import { BigTitle } from '../components/ui'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const STAFF_NAMES = ['Xodimlar', 'Xodimlar guruhi']

// Oxirgi faollikni qisqa ko'rinishga aylantirish (bugun → HH:mm, aks holda sana).
function formatLast(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return ''
  const now = new Date()
  const sameDay = d.toDateString() === now.toDateString()
  if (sameDay) return d.toLocaleTimeString('uz', { hour: '2-digit', minute: '2-digit' })
  return d.toLocaleDateString('uz', { day: '2-digit', month: '2-digit' })
}

// Chat channels — search bar + class/staff channel rows with last activity.
export default function ChatChannelsScreen({ onNavigate }) {
  const channelsQ = useFetch(() => api.chatClasses(), [])
  const lastQ = useFetch(() => api.chatLastMessages(), [])
  const [search, setSearch] = useState('')

  const channels = Array.isArray(channelsQ.data) ? channelsQ.data : []
  const lastMap = lastQ.data || {}

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    const list = q ? channels.filter((c) => c.toLowerCase().includes(q)) : channels
    // Eng so'nggi faollik bo'yicha tartiblash (faollik yo'qlar oxirida).
    return [...list].sort((a, b) => {
      const ta = lastMap[a] ? Date.parse(lastMap[a]) : 0
      const tb = lastMap[b] ? Date.parse(lastMap[b]) : 0
      return tb - ta
    })
  }, [channels, lastMap, search])

  return (
    <div className="h-full flex flex-col bg-bg">
      <BigTitle
        title="Xabarlar"
        trailing={
          <div className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text">
            <Search size={20} />
          </div>
        }
      />
      <div className="px-4 -mt-1">
        <div className="flex items-center gap-1 text-[12px] text-muted">
          <span className="w-[7px] h-[7px] rounded-full bg-success" /> Online · {channels.length} guruh
        </div>
      </div>

      <div className="px-4 pt-3 pb-1">
        <div className="flex items-center gap-2 px-4 h-11 rounded-xl bg-surface2 border border-border">
          <Search size={18} className="text-faint" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Qidirish..."
            className="flex-1 bg-transparent outline-none text-[14px] text-text placeholder:text-faint"
          />
        </div>
      </div>

      <AsyncView
        query={channelsQ}
        empty={
          <EmptyState
            icon={<EmptyIllustration><MessageSquare size={32} /></EmptyIllustration>}
            title="Guruhlar yo'q"
            subtitle="Hozircha yozish mumkin bo'lgan chat guruhlari mavjud emas."
          />
        }
      >
        <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6 space-y-1.5">
          {filtered.map((name) => {
            const isStaff = STAFF_NAMES.includes(name)
            const last = formatLast(lastMap[name])
            return (
              <button
                key={name}
                onClick={() => onNavigate?.('chatConversation', { className: name })}
                className="w-full p-3 rounded-3xl bg-surface border border-border flex items-center gap-3 text-left"
              >
                <div
                  className="w-12 h-12 rounded-2xl flex items-center justify-center text-white text-[14px] font-extrabold"
                  style={{ background: isStaff ? 'linear-gradient(135deg,#7C3AED,#C026D3)' : 'linear-gradient(135deg,#14B8A6,#0F766E)' }}
                >
                  {isStaff ? <Users size={22} /> : name}
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-[14px] font-bold text-text truncate">{name}</p>
                  <p className="text-[12px] text-muted truncate">
                    {lastMap[name] ? 'Oxirgi faollik' : 'Hali xabar yo‘q'}
                  </p>
                </div>
                <div className="flex flex-col items-end gap-1">
                  {last && <span className="text-[11px] font-semibold text-faint">{last}</span>}
                </div>
              </button>
            )
          })}
        </div>
      </AsyncView>
    </div>
  )
}
