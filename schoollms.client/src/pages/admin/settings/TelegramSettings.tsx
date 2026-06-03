import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle } from 'lucide-react'
import {
  getTelegramSettings,
  saveTelegramSettings,
  type TelegramConfig,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

/** Telegram bot tokenini (ota-onalarga e'lon yuborish uchun) kiritadigan sozlama. */
export function TelegramSettings() {
  const [token, setToken] = useState('')
  const [username, setUsername] = useState('')
  const [name, setName] = useState('')
  const [configured, setConfigured] = useState(false)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getTelegramSettings()
      .then((c: TelegramConfig) => {
        setToken(c.botToken)
        setUsername(c.botUsername)
        setName(c.botName)
        setConfigured(c.configured)
      })
      .finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    const saved = await saveTelegramSettings({
      botToken: token.trim(),
      botUsername: username.trim(),
      botName: name.trim(),
    })
    setConfigured(saved.configured)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-1 flex items-center gap-2">
        <span className="font-semibold text-slate-800">Telegram bot</span>
        {configured ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
            <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
            <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
          </span>
        )}
      </div>
      <p className="mb-4 text-sm text-slate-400">
        Bot orqali sinf ota-onalariga e'lon yuboriladi. Tokenni Telegramdagi{' '}
        <span className="font-medium text-slate-500">@BotFather</span> dan oling
        (/newbot → token). Token kiritilgandan so'ng bot avtomatik ishga tushadi.
      </p>

      <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
        <Input
          label="Bot tokeni"
          placeholder="123456789:AAH..."
          value={token}
          onChange={(e) => setToken(e.target.value)}
          autoComplete="off"
        />
        <Input
          label="Bot nomi (ko'rsatish uchun)"
          placeholder="Maktab LMS Bot"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoComplete="off"
        />
        <Input
          label="Bot foydalanuvchi nomi (@username)"
          placeholder="MaktabBot"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoComplete="off"
        />

        <div className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
          Ota-ona botni ochib (masalan {username ? `@${username}` : '@BotUsername'}) "Start" bossin va
          telefon raqamini ulashsin — raqami o'quvchining ota-ona raqami bilan solishtirilib, e'lon
          oluvchilar ro'yxatiga qo'shiladi.
        </div>

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={status === 'saving'}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
