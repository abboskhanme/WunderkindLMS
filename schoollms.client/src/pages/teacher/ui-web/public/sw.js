// O'qituvchi PWA service worker — sodda runtime kesh.
//  • /api/* — hech qachon keshlanmaydi (har doim tarmoq).
//  • /teacher/assets/* (kontent-hashli) — cache-first.
//  • navigatsiya (HTML) — network-first, oflayn bo'lsa keshdagi index.html.
const VERSION = 'teacher-v1'
const SHELL = `${VERSION}-shell`
const SCOPE = self.registration.scope // .../teacher/
const INDEX = new URL('index.html', SCOPE).pathname

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(SHELL).then((c) => c.add(INDEX)).then(() => self.skipWaiting()))
})

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => !k.startsWith(VERSION)).map((k) => caches.delete(k))),
    ).then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (e) => {
  const { request } = e
  if (request.method !== 'GET') return
  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api') || url.pathname.startsWith('/uploads') || url.pathname.startsWith('/hubs')) return

  // Sahifa navigatsiyasi — network-first, oflaynda keshlangan index.
  if (request.mode === 'navigate') {
    e.respondWith(
      fetch(request)
        .then((res) => {
          caches.open(SHELL).then((c) => c.put(INDEX, res.clone()))
          return res
        })
        .catch(() => caches.match(INDEX)),
    )
    return
  }

  // Statik assetlar — cache-first.
  if (url.pathname.includes('/assets/') || /\.(?:js|css|svg|png|webp|woff2?)$/.test(url.pathname)) {
    e.respondWith(
      caches.match(request).then(
        (hit) =>
          hit ||
          fetch(request).then((res) => {
            const copy = res.clone()
            caches.open(SHELL).then((c) => c.put(request, copy))
            return res
          }),
      ),
    )
  }
})
