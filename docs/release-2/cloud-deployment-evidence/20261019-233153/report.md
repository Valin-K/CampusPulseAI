# Cloud smoke test - 20261019-233153

 / 8 checks passed.

| Check | URL | Status | Passed |
|---|---|---|---|
| portal root | https://campuspulse-portal.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/ | 200 | True |
| academicrecords-web CRUD read (web -> api -> db) | https://academicrecords-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/users | 401 | False |
| coursemgmt-web CRUD read (web -> api -> db) | https://coursemgmt-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/course-access/users | 401 | False |
| campushousing-web CRUD read (web -> api -> db) | https://campushousing-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/housing/rooms | 401 | False |
| finaid-web CRUD read (web -> api -> db) | https://finaid-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/invoices | 401 | False |
| eventscheduling-web CRUD read (web -> api -> db) | https://eventscheduling-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/rooms | 401 | False |
| AI profile: /api/assistant/mcp/integration returns 403 | https://finaid-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/assistant/mcp/integration | 401 | False |
| AI profile: /api/ai/knowledge returns 403 | https://finaid-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/ai/knowledge | 401 | False |
