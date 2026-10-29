/**
 * SHOW_PARENT_FINANCE — the parent / student web portal's finance screen (`/parent`, `/student`).
 * Hidden 2026-10-14 ("ota-ona appida vaqtinchalikka qarzdorlik ko'rinmaydigan qilib qo'yish kerak"),
 * shown again 2026-10-28 at the client's request; when off, the portal home opens the news page instead. The Telegram Mini App
 * has its twin in `pages/miniapp/ui-tg/src/lib/flags.js`. The admin student card keeps its
 * finance tab — it renders `FinanceView` with `embedded` and does not read this flag.
 */
export const SHOW_PARENT_FINANCE = true
