// PWA o'rnatish (install) — beforeinstallprompt hodisasini ushlab turamiz va
// Profil ekranidagi "Ilovani o'rnatish" tugmasi orqali ishga tushiramiz.

let deferred = null
const subs = new Set()

if (typeof window !== 'undefined') {
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault()
    deferred = e
    subs.forEach((f) => f(true))
  })
  window.addEventListener('appinstalled', () => {
    deferred = null
    subs.forEach((f) => f(false))
  })
}

export function canInstall() {
  return !!deferred
}

export async function promptInstall() {
  if (!deferred) return false
  deferred.prompt()
  const { outcome } = await deferred.userChoice
  if (outcome === 'accepted') {
    deferred = null
    subs.forEach((f) => f(false))
  }
  return outcome === 'accepted'
}

// Standalone (o'rnatilgan / app holatida ochilgan) rejimdami?
export function isStandalone() {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    window.navigator.standalone === true
  )
}

export function onInstallChange(fn) {
  subs.add(fn)
  return () => subs.delete(fn)
}
