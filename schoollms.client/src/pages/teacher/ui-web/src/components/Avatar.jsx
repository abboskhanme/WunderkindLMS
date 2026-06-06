import { avatarColor, initials } from '../lib/colors'

// AppAvatar — colored circle with initials (deterministic color per name).
export default function Avatar({ name, size = 40, ring = false, imageUrl }) {
  const color = avatarColor(name)
  const style = {
    width: size,
    height: size,
    background: color,
    fontSize: size * 0.38,
    boxShadow: ring ? `0 0 0 2px var(--bg), 0 0 0 5px ${color}80` : undefined,
  }
  return (
    <div
      className="shrink-0 rounded-full flex items-center justify-center text-white font-bold overflow-hidden"
      style={style}
    >
      {imageUrl ? (
        <img src={imageUrl} alt={name} className="w-full h-full object-cover" />
      ) : (
        <span style={{ letterSpacing: '-0.02em' }}>{initials(name)}</span>
      )}
    </div>
  )
}
