# Canteen menu

The canteen menu is one per school: dishes (`dishes`) are free-text rows on a date and a meal
(`breakfast` / `lunch` / `dinner`). Admins edit it under Sozlamalar → Oshxona menyusi (also
`/admin/canteen`, `CanteenController`, gate `[AdminPerm("app")]`); parents and pupils read it in the
Mini App's Ovqat tab and the portal (`GET /api/student/canteen`), Monday to Sunday.

## Copy a week

Status: built 2026-10-16 (client request: *"Oshxona menyusini haftalik ko'chirish imkonini
qo'shish"*).

### What

On the weekly view the **Haftani ko'chirish** button opens a modal that copies the week on screen onto
another week — by default the next one — or onto up to 8 consecutive weeks. The modal shows how many
dishes each source day has, a target week stepper, a week count and the **Ustiga yozish** switch.
After a copy the page moves to the first target week and a toast reports what happened.

### API

`POST /api/admin/canteen/menu/copy-week`

```json
{ "fromWeekStart": "2026-10-19", "toWeekStart": "2026-10-26", "overwrite": false, "weeks": 1 }
```

`weeks` is optional (default 1, at most 8: `CanteenWeekCopy.MaxWeeks`). 200 returns
`{ copied, skipped, replaced, targetWeeks, days }`.

| Case | Answer |
|---|---|
| a date is not `yyyy-MM-dd`, or not a Monday | 400 |
| `weeks` outside 1..8 | 400 |
| the source week is one of the target weeks | 400 |
| the source week has nothing to copy | 400 |
| a target day that would be written already has dishes, `overwrite` off | 409 `{ message, days }` — nothing written |
| same, `overwrite` on | those days' dishes removed and replaced |
| staff without `app` / teacher / pupil / parent | 403 |
| no token | 401 |

### Rules

- Weekday onto weekday, Monday..Sunday — the Mini App shows Sunday, so a Sunday menu travels too.
- Only the weekdays the source week fills are written. A target day whose source day is empty is
  never touched — with or without overwrite — and is not a conflict.
- All or nothing: removal and insertion go through one `SaveChanges` (one transaction). In a
  multi-week copy one filled day in any week stops the whole copy.
- `skipped` — `Dish` has no active/archived flag, so "skipped" means a source row the menu can never
  show: a meal key other than the three, a blank name or an unreadable date. Counted once, not per
  target week.
- A copy shares the source dish's `imageUrl`; deleting a dish never deletes its upload, so this is
  safe.
- Not audited: no canteen write is (dish add / edit / delete write no audit row either).

Code: `SchoolLms.Application/Services/CanteenWeekCopy.cs`, `CanteenController.CopyWeek`,
`schoollms.client/src/pages/admin/canteen/CopyWeekModal.tsx`. Tests: `CanteenWeekCopyTests`.
