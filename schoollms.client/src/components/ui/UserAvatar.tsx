import { cn } from '@/lib/utils'

/**
 * Foydalanuvchi rasmi — yuklangan bo'lsa surati, bo'lmasa ism bosh harflari.
 * Topbar va akkaunt oynasida bir xil ko'rinsin deb bitta joyda.
 */
export function UserAvatar({
  fullName,
  avatarUrl,
  className,
}: {
  fullName: string
  avatarUrl?: string | null
  className?: string
}) {
  const initials = fullName
    .split(' ')
    .filter(Boolean)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()

  return (
    <div
      className={cn(
        'flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-brand-600 font-semibold text-white',
        className,
      )}
    >
      {avatarUrl ? (
        <img src={avatarUrl} alt={fullName} className="h-full w-full object-cover" />
      ) : (
        initials
      )}
    </div>
  )
}
