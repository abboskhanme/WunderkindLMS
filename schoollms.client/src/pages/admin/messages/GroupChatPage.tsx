/**
 * Guruh chati — sinflar, o'quv guruhlari va xodimlar kanali (ichki, Telegramga ketmaydi).
 * Mijoz, 2026-10-11: "guruh chatini xabarlar bo'limidan future ko'chirish" — "Xabarlar" endi
 * faqat ota-onalarga e'lon; chat menyuda Future → Aloqa ostida.
 */
import { useEffect, useState } from 'react'
import { Briefcase, Users, Users2 } from 'lucide-react'
import type { MessageClass } from '@/types'
import {
  getMessageClasses,
  getMessageChannels,
  getChat,
  sendChat,
  type ChatChannel,
} from '@/api/services/messages'
import { STAFF_CHANNEL, STAFF_CHANNEL_LABEL } from '@/config/constants'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ChatPanel } from '@/components/chat/ChatPanel'

export function GroupChatPage() {
  const { unreadChannels } = useUnread()
  const [classes, setClasses] = useState<MessageClass[]>([])
  /**
   * O'quv guruhi kanallari (G-17). Kalit — `grp:<id>`, ya'ni ekranda
   * ko'rsatilmaydi: sarlavha `label` dan olinadi. Cut-over o'chirgichi o'chiq
   * bo'lsa server bu ro'yxatni bo'sh qaytaradi va bo'lim umuman ko'rinmaydi.
   */
  const [groupChannels, setGroupChannels] = useState<ChatChannel[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getMessageClasses(), getMessageChannels().catch((): ChatChannel[] => [])])
      .then(([cs, chans]) => {
        setClasses(cs)
        setGroupChannels(chans.filter((c) => c.kind === 'group'))
        setSelected((prev) => prev ?? cs[0]?.name ?? STAFF_CHANNEL)
      })
      .finally(() => setLoading(false))
  }, [])

  const selectedGroup = groupChannels.find((g) => g.key === selected) ?? null

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Guruh chati</h1>
          <p className="text-sm text-slate-400">Sinflar, o'quv guruhlari va xodimlarning ichki chati</p>
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
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

              {groupChannels.length > 0 && (
                <>
                  <div className="my-1 border-t border-slate-100" />
                  <p className="px-3 pb-1 pt-2 text-xs font-semibold text-slate-400">
                    O'quv guruhlari
                  </p>
                  {groupChannels.map((g) => (
                    <button
                      key={g.key}
                      type="button"
                      onClick={() => setSelected(g.key)}
                      className={cn(
                        'flex w-full items-center justify-between gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors',
                        selected === g.key
                          ? 'bg-brand-50 text-brand-700'
                          : 'text-slate-600 hover:bg-slate-50',
                      )}
                    >
                      <span className="flex min-w-0 items-center gap-2">
                        <Users2 className="h-4 w-4 shrink-0 text-violet-400" />
                        <span className="truncate font-medium">{g.label}</span>
                      </span>
                      {unreadChannels.has(g.key) && (
                        <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                      )}
                    </button>
                  ))}
                </>
              )}

              {classes.length === 0 && groupChannels.length === 0 && (
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
            ) : selectedGroup ? (
              <ChatPanel
                key={selectedGroup.key}
                className={selectedGroup.key}
                fetchMessages={getChat}
                sendMessage={sendChat}
                title={`${selectedGroup.label} — guruh chati`}
                subtitle="O'quv guruhi a'zolari, uning o'qituvchilari va admin"
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
