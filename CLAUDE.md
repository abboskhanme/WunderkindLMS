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
