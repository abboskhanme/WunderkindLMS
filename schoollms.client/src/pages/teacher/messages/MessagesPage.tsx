import { useEffect, useState } from 'react'
import { Briefcase } from 'lucide-react'
import { getTeacherChatClasses, getTeacherChat, sendTeacherChat } from '@/api/services/teacher'
import { STAFF_CHANNEL, STAFF_CHANNEL_LABEL } from '@/config/constants'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ChatPanel } from '@/components/chat/ChatPanel'

export function TeacherMessagesPage() {
  const { unreadChannels } = useUnread()
  const [classes, setClasses] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    getTeacherChatClasses()
      .then((cs) => {
        // Xodimlar kanali alohida ko'rsatiladi — sinflar ro'yxatidan chiqaramiz.
        const onlyClasses = cs.filter((c) => c !== STAFF_CHANNEL)
        setClasses(onlyClasses)
        setSelected((prev) => prev ?? onlyClasses[0] ?? STAFF_CHANNEL)
      })
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Guruh chati</h1>
        <p className="text-sm text-slate-400">Dars beradigan sinflaringiz va xodimlar chati</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[240px_1fr]">
          <Card className="p-2">
            <div className="max-h-[70vh] space-y-1 overflow-y-auto">
              {/* Xodimlar kanali — har bir o'qituvchiga ochiq */}
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
                  key={c}
                  type="button"
                  onClick={() => setSelected(c)}
                  className={cn(
                    'flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-sm font-medium transition-colors',
                    selected === c ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {c}
                  {unreadChannels.has(c) && (
                    <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                  )}
                </button>
              ))}

              {classes.length === 0 && (
                <p className="px-3 py-4 text-center text-xs text-slate-400">
                  Sizga biriktirilgan sinf yo'q.
                </p>
              )}
            </div>
          </Card>

          <div>
            {selected === STAFF_CHANNEL ? (
              <ChatPanel
                key="staff"
                className={STAFF_CHANNEL}
                fetchMessages={getTeacherChat}
                sendMessage={sendTeacherChat}
                title={`${STAFF_CHANNEL_LABEL} — guruh chati`}
                subtitle="Barcha o'qituvchilar va adminlar"
              />
            ) : selected ? (
              <ChatPanel
                key={selected}
                className={selected}
                fetchMessages={getTeacherChat}
                sendMessage={sendTeacherChat}
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
