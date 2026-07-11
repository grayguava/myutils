# Weekly Health Summary — {{DATE}}

**Host:** {{HOSTNAME}}
**Generated:** {{GENERATED_AT}}
**Snapshot folder:** `logs/{{DATE}}/`

---

## Quick Status

| Area | Status | Notes |
|------|--------|-------|
| Temperatures | {{STATUS_TEMPS}} | |
| Storage (SMART) | {{STATUS_STORAGE}} | |
| Event Logs | {{STATUS_EVENTS}} | |
| System Integrity | {{STATUS_INTEGRITY}} | |
| Drivers | {{STATUS_DRIVERS}} | |
| Network | {{STATUS_NETWORK}} | |

---

## Temperatures

**Idle:**
{{TEMPS_IDLE}}

**Light Load (30s):**
{{TEMPS_LOAD}}

**Fan Speeds:**
{{FANS}}

> Summer note: compare week-over-week, not just thresholds.
> CPU idle concern: >70°C | CPU load concern: >90°C | GPU load concern: >95°C

---

## Storage

**Drive Health:**
{{STORAGE_HEALTH}}

**Volume Usage:**
{{STORAGE_VOLUMES}}

**SMART Concerns:**
{{SMART_CONCERNS}}

---

## Event Log Summary

**Counts (last 7 days):**
{{EVENT_COUNTS}}

**Notable Events:**
{{NOTABLE_EVENTS}}

> Focus on recurrence and escalation — not isolated warnings.

---

## System Integrity

**DISM Result:** {{DISM_RESULT}}

**SFC Result:** {{SFC_RESULT}}

---

## Drivers

**Recently Changed (last 30 days):**
{{DRIVERS_RECENT}}

**Problem Devices:**
{{DRIVERS_PROBLEMS}}

**Unsigned Drivers:**
{{DRIVERS_UNSIGNED}}

---

## Software Changes

**Recently Installed (last 30 days):**
{{SOFTWARE_RECENT}}

---

## Network

**Listening Ports (unexpected):**
{{NETWORK_LISTENERS}}

**Anomalies:**
{{NETWORK_ANOMALIES}}

---

## System Info

**OS:** {{OS_NAME}} (Build {{OS_BUILD}})
**Uptime:** {{UPTIME}}
**RAM Used:** {{RAM_USED}}
**Pending Reboot:** {{PENDING_REBOOT}}
**Last Windows Update:** {{LAST_UPDATE}}

---

## Observations

_Manual notes — add anything notable that the script missed._

-

---

## Action Items

_Things to follow up on next week or investigate._

- [ ]

---

## Compared to Last Week

_Fill in after reviewing previous summary._

| What | Last Week | This Week | Trend |
|------|-----------|-----------|-------|
| CPU Idle Temp | | | |
| GPU Idle Temp | | | |
| Drive Temp | | | |
| Free Space (C:) | | | |
| Critical Events | | | |
| Errors | | | |

---

_Next scheduled run: {{NEXT_RUN}}_
