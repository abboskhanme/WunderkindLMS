// Web (PWA) push — FCM web token olib, mavjud /teacher/notifications/register ga yuboradi.
// Server tomoni o'zgarmaydi: FcmService web token'ga ham xuddi shu `notification` xabarini yuboradi,
// brauzer (firebase-messaging-sw.js) uni ko'rsatadi. Config server'dan (admin Sozlamalar) keladi.

import { api } from './api'

const TOKEN_KEY = 'teacher-fcm-token'
const BASE = import.meta.env.BASE_URL // '/teacher/'

let _messaging = null
let _foregroundBound = false

export function pushSupported() {
  return (
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'Notification' in window &&
    'PushManager' in window
  )
}

export function notifPermission() {
  return typeof Notification !== 'undefined' ? Notification.permission : 'unsupported'
}

export function hasPushToken() {
  return !!localStorage.getItem(TOKEN_KEY)
}

async function ensureMessaging(webConfig) {
  const { initializeApp, getApps, getApp } = await import('firebase/app')
  const { getMessaging, isSupported } = await import('firebase/messaging')
  if (!(await isSupported())) throw new Error('not-supported')
  const app = getApps().length ? getApp() : initializeApp(webConfig)
  _messaging = getMessaging(app)
  return _messaging
}

async function registerFcmSw(webConfig) {
  const url = `${BASE}firebase-messaging-sw.js?c=${encodeURIComponent(JSON.stringify(webConfig))}`
  const reg = await navigator.serviceWorker.register(url, { scope: `${BASE}fcm/` })
  // getToken faol (activated) SW talab qiladi — faollashishini kutamiz.
  if (!reg.active) {
    await new Promise((resolve) => {
      const sw = reg.installing || reg.waiting
      if (!sw) return resolve()
      sw.addEventListener('statechange', () => sw.state === 'activated' && resolve())
      setTimeout(resolve, 5000) // fallback
    })
  }
  return reg
}

async function bindForeground() {
  if (_foregroundBound || !_messaging) return
  const { onMessage } = await import('firebase/messaging')
  const reg = await navigator.serviceWorker.getRegistration(`${BASE}fcm/`)
  onMessage(_messaging, (payload) => {
    const n = payload.notification || payload.data || {}
    if ((!n.title && !n.body) || notifPermission() !== 'granted') return
    if (reg) reg.showNotification(n.title || 'Bildirishnoma', { body: n.body || '', icon: `${BASE}icon.svg` })
  })
  _foregroundBound = true
}

/**
 * Push'ni yoqadi: server config'ini oladi, ruxsat so'raydi, FCM token olib serverga yuboradi.
 * `silent:true` — ruxsat allaqachon berilgan bo'lsa jimgina yangilaydi (so'ramaydi).
 * Natija: { ok, reason }. reason: unsupported|disabled|config|denied|permission|token.
 */
export async function enablePush({ silent = false } = {}) {
  if (!pushSupported()) return { ok: false, reason: 'unsupported' }

  let cfg
  try { cfg = await api.pushConfig() } catch { return { ok: false, reason: 'config' } }
  if (!cfg || !cfg.enabled) return { ok: false, reason: 'disabled' }

  let webConfig
  try { webConfig = JSON.parse(cfg.webConfigJson) } catch { return { ok: false, reason: 'config' } }

  let perm = notifPermission()
  if (perm === 'default') {
    if (silent) return { ok: false, reason: 'permission' }
    perm = await Notification.requestPermission()
  }
  if (perm !== 'granted') return { ok: false, reason: perm === 'denied' ? 'denied' : 'permission' }

  try {
    const reg = await registerFcmSw(webConfig)
    await ensureMessaging(webConfig)
    const { getToken } = await import('firebase/messaging')
    const token = await getToken(_messaging, { vapidKey: cfg.vapidKey, serviceWorkerRegistration: reg })
    if (!token) return { ok: false, reason: 'token' }
    await api.registerDevice({
      token,
      platform: 'web',
      deviceName: (navigator.userAgent || 'web').slice(0, 80),
      appId: webConfig.appId || '',
    })
    localStorage.setItem(TOKEN_KEY, token)
    await bindForeground()
    return { ok: true }
  } catch {
    return { ok: false, reason: 'token' }
  }
}

/** Push'ni o'chiradi: token'ni serverdan va FCM'dan olib tashlaydi. */
export async function disablePush() {
  const token = localStorage.getItem(TOKEN_KEY)
  try {
    if (_messaging) {
      const { deleteToken } = await import('firebase/messaging')
      await deleteToken(_messaging).catch(() => {})
    }
  } catch {
    /* ignore */
  }
  if (token) {
    try { await api.unregisterDevice(token) } catch { /* ignore */ }
  }
  localStorage.removeItem(TOKEN_KEY)
}

/** Login'dan keyin: ruxsat allaqachon berilgan bo'lsa, tokenni jimgina yangilaydi. */
export async function autoInitPush() {
  if (!pushSupported() || notifPermission() !== 'granted') return
  await enablePush({ silent: true })
}
