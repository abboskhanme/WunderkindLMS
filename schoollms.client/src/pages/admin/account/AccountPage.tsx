import { AccountSettings } from './AccountSettings'

/** Tepadagi profil menyusidan ochiladigan akkaunt (login/parol) sozlamalari sahifasi. */
export function AccountPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Akkaunt sozlamalari</h1>
        <p className="text-sm text-slate-400">Tizimga kirish login (email) va parolingiz</p>
      </div>
      <div className="max-w-3xl">
        <AccountSettings />
      </div>
    </div>
  )
}
