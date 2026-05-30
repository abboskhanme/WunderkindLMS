import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import axios from 'axios'
import { GraduationCap } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { useAuth } from '@/context/auth-context'
import { homeByRole } from '@/config/navigation'

interface LocationState {
  from?: string
}

function errorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return (err.response?.data as { message?: string })?.message ?? "Kirishda xatolik yuz berdi"
  }
  if (err instanceof Error) return err.message
  return "Kirishda xatolik yuz berdi"
}

export function LoginPage() {
  const { isAuthenticated, user, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  // Allaqachon kirgan bo'lsa — o'z bo'limiga
  if (isAuthenticated && user) {
    return <Navigate to={homeByRole[user.role]} replace />
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const u = await login(email, password)
      const from = (location.state as LocationState | null)?.from
      navigate(from ?? homeByRole[u.role], { replace: true })
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-100 px-4">
      <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
        <div className="mb-6 flex flex-col items-center gap-3 text-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-brand-600 text-white">
            <GraduationCap className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-lg font-semibold text-slate-800">Maktab LMS</h1>
            <p className="text-sm text-slate-400">Tizimga kirish</p>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Login"
            type="text"
            autoComplete="username"
            placeholder="Login"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          <Input
            label="Parol"
            type="password"
            autoComplete="current-password"
            placeholder="parol"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />

          {error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
          )}

          <Button type="submit" disabled={loading} className="w-full">
            {loading ? 'Kirilmoqda…' : 'Kirish'}
          </Button>
        </form>
      </div>
    </div>
  )
}
