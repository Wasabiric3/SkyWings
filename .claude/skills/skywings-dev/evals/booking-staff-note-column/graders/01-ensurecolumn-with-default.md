---
type: llm
focus: trace
weight: 1
---

The schema change adds the note column with EnsureColumn (ALTER TABLE bookings ADD COLUMN ...) and the column is NOT NULL with a DEFAULT, or nullable, so existing databases open without error.
