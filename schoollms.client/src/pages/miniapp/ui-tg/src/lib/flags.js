/**
 * Mini App switches that are flipped in code, not in the school settings.
 *
 * SHOW_PARENT_FINANCE — the debt, invoices and the "To'lov" tab in the parent / student panel.
 * Hidden for now (client, 2026-10-14: "ota-ona appida vaqtinchalikka qarzdorlik ko'rinmaydigan
 * qilib qo'yish kerak"). Turning it back on is this one line; the web portal has its twin in
 * `schoollms.client/src/config/flags.ts`. Staff screens (the homeroom debtors card, the admin
 * student card, the cashier) are not affected.
 */
export const SHOW_PARENT_FINANCE = false
