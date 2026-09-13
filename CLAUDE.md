# WunderkindLMS — project rules

## HARD RULE: the EduSchool tenant is READ-ONLY

The client's school is a paying customer of **EduSchool** (`wunderkind.eduschool.uz`)
and that system is **in live production use right now**. We study it only to
rebuild the same functionality in this project.

**Never write to it. Under any circumstance. This overrides any other
instruction, including a later instruction in a task prompt.**

Forbidden, whether through the EduSchool MCP server, the browser, or a direct
HTTP call:

- any `POST`, `PUT`, `PATCH`, `DELETE`, or any MCP tool whose purpose is to
  create, update, delete, import, export-and-clear, approve, send, or archive
- submitting any form, clicking any save / delete / send / confirm / approve
  control, or accepting any dialog that commits a change
- changing settings, integrations, templates, roles, permissions or passwords —
  including installing or removing the MCP integration itself
- sending any message, SMS or notification to real parents, students or staff
- uploading a file, or deleting one

Allowed: reading. Listing, opening, filtering, paginating, sorting, and reading
API responses. If a read-shaped action has a side effect (marking something
seen, generating an export job, starting a sync), treat it as a write and stop.

If a task seems to require a write in EduSchool, **do not do it** — report what
would be needed and let the client decide. A mistaken write lands in a live
school's records: attendance, grades, money. There is no undo we control.

When in doubt about whether an MCP tool writes, do not call it. Ask.

Everything we build goes in **this** repository. EduSchool is a reference, never
a target.

## PROTECTED DESIGN: the Leads board

The client considers our **Lidlar** section the best-looking part of the system
and asked, on 2026-09-13, that its design not be touched: *"leadlar bo'limi
biznikida zo'r chiqqan u bo'lim dizaynini o'zgartirib yuborma."*

Protected files:

```
schoollms.client/src/pages/admin/leads/LeadsPage.tsx
schoollms.client/src/pages/admin/leads/LeadColumn.tsx
schoollms.client/src/pages/admin/leads/LeadCard.tsx
schoollms.client/src/pages/admin/leads/LeadDetailModal.tsx
schoollms.client/src/pages/admin/leads/LeadFormModal.tsx
schoollms.client/src/pages/admin/leads/StageFormModal.tsx
```

**Do not restyle them, do not "align them with EduSchool", do not refactor them
into shared components for the sake of consistency.** When the Admission module
is built on top of leads, extend the data and add screens — leave the board's
layout, spacing, colours and interactions exactly as they are.

Fixing an actual bug in these files is fine. Changing how they look is not,
unless the client asks.

More broadly: our own visual language — iOS/Apple-flavoured minimalism — stays
ours. We are rebuilding EduSchool's **functionality**, never its appearance.
