import { useEffect, useState } from 'react'
import { MessageSquare, Megaphone, Users, Briefcase, Bell } from 'lucide-react'
import type { MessageClass } from '@/types'
import { getMessageClasses, getChat, sendChat } from '@/api/services/messages'
import { STAFF_CHANNEL, STAFF_CHANNEL_LABEL } from '@/config/constants'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ChatPanel } from '@/components/chat/ChatPanel'
import { BroadcastPanel } from './BroadcastPanel'
import { PushComposer } from './PushComposer'

type Tab = 'chat' | 'broadcast' | 'push'

export function MessagesPage() {
  const { unreadChannels } = useUnread()
  const [classes, setClasses] = useState<MessageClass[]>([])
  const [loading, setLoading] = useState(true)
  const [tab, setTab] = useState<Tab>('chat')
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    getMessageClasses()
      .then((cs) => {
        setClasses(cs)
        setSelected((prev) => prev ?? cs[0]?.name ?? STAFF_CHANNEL)
      })
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xabarlar</h1>
          <p className="text-sm text-slate-400">
            Sinf va xodimlar guruh chati hamda ota-onalarga e'lon
          </p>
        </div>
        <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
          <TabButton active={tab === 'chat'} onClick={() => setTab('chat')} icon={MessageSquare}>
            Guruh chati
          </TabButton>
          <TabButton
            active={tab === 'broadcast'}
            onClick={() => setTab('broadcast')}
            icon={Megaphone}
          >
            E'lon yuborish
          </TabButton>
          <TabButton active={tab === 'push'} onClick={() => setTab('push')} icon={Bell}>
            Push yuborish
          </TabButton>
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : tab === 'broadcast' ? (
        <BroadcastPanel classes={classes} />
      ) : tab === 'push' ? (
        <PushComposer classes={classes} />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[280px_1fr]">
          <Card className="p-2">
            <div className="max-h-[70vh] space-y-1 overflow-y-auto">
              {/* Xodimlar kanali */}
              <button
                type="button"
                onClick={() => setSelected(STAFF_CHANNEL)}
                className={cn(
                  'flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors',
                  selected === STAFF_CHANNEL
                    ? 'bg-brand-50 text-brand-700'
                    : 'text-slate-600 hover:bg-slate-50',
                )}
              >
                <Briefcase className="h-4 w-4 text-slate-400" />
                <span className="font-medium">{STAFF_CHANNEL_LABEL}</span>
                <span className="ml-auto flex items-center gap-1.5">
                  <span className="text-xs text-slate-400">barcha</span>
                  {unreadChannels.has(STAFF_CHANNEL) && (
                    <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                  )}
                </span>
              </button>
              <div className="my-1 border-t border-slate-100" />

              {classes.map((c) => (
                <button
                  key={c.name}
                  type="button"
                  onClick={() => setSelected(c.name)}
                  className={cn(
                    'flex w-full items-center justify-between gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors',
                    selected === c.name
                      ? 'bg-brand-50 text-brand-700'
                      : 'text-slate-600 hover:bg-slate-50',
                  )}
                >
                  <span className="font-medium">{c.name}</span>
                  <span className="flex items-center gap-2 text-xs text-slate-400">
                    <span className="inline-flex items-center gap-0.5" title="O'quvchilar">
                      <Users className="h-3 w-3" />
                      {c.studentCount}
                    </span>
                    {unreadChannels.has(c.name) && (
                      <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                    )}
                  </span>
                </button>
              ))}

              {classes.length === 0 && (
                <p className="px-3 py-6 text-center text-sm text-slate-400">
                  Sinflar yo'q. Avval sinf qo'shing.
                </p>
              )}
            </div>
          </Card>

          <div>
            {selected === STAFF_CHANNEL ? (
              <ChatPanel
                key="staff"
                className={STAFF_CHANNEL}
                fetchMessages={getChat}
                sendMessage={sendChat}
                title={`${STAFF_CHANNEL_LABEL} — guruh chati`}
                subtitle="Barcha o'qituvchilar va adminlar"
              />
            ) : selected ? (
              <ChatPanel
                key={selected}
                className={selected}
                fetchMessages={getChat}
                sendMessage={sendChat}
              />
            ) : (
              <Card>
                <p className="py-10 text-center text-slate-400">Kanal tanlang</p>
              </Card>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: typeof MessageSquare
  children: React.ReactNode
}

function TabButton({ active, onClick, icon: Icon, children }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
      )}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
