import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { Camera, Check, Loader2 } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { removeAvatar, updateAccount, uploadAvatar } from '@/api/services/auth'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { UserAvatar } from '@/components/ui/UserAvatar'

function errorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return (err.response?.data as { message?: string })?.message ?? 'Saqlashda xatolik yuz berdi'
  }
  if (err instanceof Error) return err.message
  return 'Saqlashda xatolik yuz berdi'
}

/** Administrator o'z login (email) va parolini o'zgartiradigan sozlama bo'limi. */
interface AccountSettingsProps {
  /**
   * Modal ichida (Topbar → "Akkaunt sozlamalari", mijoz 2026-09-22): kartasiz va
   * sarlavhasiz — sarlavhani modal beradi. Sahifada (`/admin/account`) — odatdagidek.
   */
  bare?: boolean
  /** Muvaffaqiyatli saqlangandan keyin (modal yopiladi va xabar chiqadi). */
  onSaved?: () => void
}

export function AccountSettings({ bare = false, onSaved }: AccountSettingsProps = {}) {
  const { user, updateUser } = useAuth()
  const [login, setLogin] = useState(user?.email ?? '')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState('')
  const [photoBusy, setPhotoBusy] = useState(false)
  const [photoError, setPhotoError] = useState('')
  const fileRef = useRef<HTMLInputElement | null>(null)

  // Profil rasmi — har qanday rol uchun, parolsiz, darhol saqlanadi (mijoz, 2026-09-22).
  const onPhoto = async (file: File | undefined) => {
    if (!file) return
    setPhotoError('')
    setPhotoBusy(true)
    try {
      updateUser(await uploadAvatar(file))
    } catch (err) {
      setPhotoError(errorMessage(err))
    } finally {
      setPhotoBusy(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  const onRemovePhoto = async () => {
    setPhotoError('')
    setPhotoBusy(true)
    try {
      updateUser(await removeAvatar())
    } catch (err) {
      setPhotoError(errorMessage(err))
    } finally {
      setPhotoBusy(false)
    }
  }

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')

    if (!login.trim()) {
      setError("Login bo'sh bo'lmasligi kerak")
      return
    }
    if (!currentPassword) {
      setError('Joriy parolni kiriting')
      return
    }
    if (newPassword) {
      if (newPassword.length < 8) {
        setError("Yangi parol kamida 8 belgidan iborat bo'lsin")
        return
      }
      if (newPassword !== confirmPassword) {
        setError('Yangi parollar bir-biriga mos kelmadi')
        return
      }
    }

    setStatus('saving')
    try {
      const updated = await updateAccount({
        email: login.trim(),
        currentPassword,
        newPassword: newPassword || undefined,
      })
      updateUser(updated)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
      onSaved?.()
    } catch (err) {
      setStatus('idle')
      setError(errorMessage(err))
    }
  }

  const photo = user && (
    <div className="mb-5 flex items-center gap-4">
      <UserAvatar fullName={user.fullName} avatarUrl={user.avatarUrl} className="h-16 w-16 text-lg" />
      <div className="min-w-0">
        <p className="truncate font-medium text-slate-800">{user.fullName}</p>
        <div className="mt-1.5 flex flex-wrap items-center gap-2">
          <input
            ref={fileRef}
            type="file"
            accept="image/jpeg,image/png,image/webp,image/heic"
            className="hidden"
            onChange={(e) => void onPhoto(e.target.files?.[0])}
          />
          <button
            type="button"
            disabled={photoBusy}
            onClick={() => fileRef.current?.click()}
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            {photoBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Camera className="h-3.5 w-3.5" />}
            {user.avatarUrl ? 'Rasmni almashtirish' : 'Rasm yuklash'}
          </button>
          {user.avatarUrl && (
            <button
              type="button"
              disabled={photoBusy}
              onClick={() => void onRemovePhoto()}
              className="rounded-lg px-2 py-1 text-xs text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500 disabled:opacity-50"
            >
              O'chirish
            </button>
          )}
        </div>
        {photoError && <p className="mt-1 text-xs text-red-600">{photoError}</p>}
      </div>
    </div>
  )

  const intro = (
    <p className="mb-4 text-sm text-slate-400">
      Tizimga kirish login va parolingizni shu yerdan o'zgartirasiz. Login o'zgarsa,
      keyingi safar yangi login bilan kirasiz (qaytadan kirish shart emas).
    </p>
  )

  const form = (
      <form onSubmit={onSubmit} className={bare ? 'space-y-4' : 'max-w-md space-y-4'}>
        <Input
          label="Login"
          type="text"
          autoComplete="username"
          placeholder="Login"
          value={login}
          onChange={(e) => setLogin(e.target.value)}
          required
        />
        <Input
          label="Joriy parol"
          type="password"
          autoComplete="current-password"
          placeholder="parol"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          required
        />

        <div className="border-t border-slate-100 pt-4">
          <p className="mb-3 text-sm font-medium text-slate-600">Parolni o'zgartirish (ixtiyoriy)</p>
          <div className="space-y-4">
            <Input
              label="Yangi parol"
              type="password"
              autoComplete="new-password"
              placeholder="parol"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
            <Input
              label="Yangi parolni tasdiqlang"
              type="password"
              autoComplete="new-password"
              placeholder="parol"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </div>
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

        {/* Modalda — tugma o'ngda, oynalardagi odatiy joyida (mijoz, 2026-09-22). */}
        <div className={bare ? 'flex items-center justify-end gap-3 pt-1' : 'flex items-center gap-3'}>
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
  )

  if (bare) {
    return (
      <>
        {photo}
        {intro}
        {form}
      </>
    )
  }

  return (
    <Card>
      <div className="mb-4 font-semibold text-slate-800">Mening akkauntim</div>
      {photo}
      {intro}
      {form}
    </Card>
  )
}
