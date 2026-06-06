/* FCM (web push) background service worker — o'qituvchi PWA.
   Bu SW faqat push qabul qilish/ko'rsatish uchun (caching alohida sw.js da).
   Firebase web config registratsiya URL'idagi `?c=<json>` orqali keladi (server SchoolMeta'dan beradi),
   shuning uchun build'da hardcode shart emas. */
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-app-compat.js')
importScripts('https://www.gstatic.com/firebasejs/10.14.1/firebase-messaging-compat.js')

try {
  const cfg = JSON.parse(new URL(self.location).searchParams.get('c') || '{}')
  if (cfg && cfg.apiKey) {
    firebase.initializeApp(cfg)
    const messaging = firebase.messaging()
    // `notification` payloadli xabarlarni brauzer avtomatik ko'rsatadi; faqat data-only bo'lsa
    // qo'lda ko'rsatamiz (ikki marta chiqmasligi uchun).
    messaging.onBackgroundMessage((payload) => {
      if (payload.notification) return
      const d = payload.data || {}
      if (!d.title && !d.body) return
      self.registration.showNotification(d.title || 'Bildirishnoma', {
        body: d.body || '',
        icon: '/teacher/icon.svg',
        badge: '/teacher/icon.svg',
        data: d,
      })
    })
  }
} catch (e) {
  // config yo'q/buzilgan — SW shunchaki bo'sh ishlaydi
}

// Bildirishnoma bosilganda — ilovani fokuslaymiz yoki ochamiz.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((list) => {
      for (const c of list) {
        if (c.url.includes('/teacher') && 'focus' in c) return c.focus()
      }
      return clients.openWindow('/teacher/')
    }),
  )
})
