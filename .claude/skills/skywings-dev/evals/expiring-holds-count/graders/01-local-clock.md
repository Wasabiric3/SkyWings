---
type: llm
focus: trace
weight: 1
---

The query compares expires_at against DateTime.Now (local time), not DateTime.UtcNow.
