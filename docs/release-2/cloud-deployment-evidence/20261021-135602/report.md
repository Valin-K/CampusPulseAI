# Cloud smoke test - 20261021-135602

8 / 8 checks passed.

| Check | URL | Status | Passed |
|---|---|---|---|
| portal root | https://campuspulse-portal.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/ | 200 | True |
| authentication (STU001 sign-in) | https://academicrecords-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/auth/token | 200 | True |
| academicrecords-web CRUD read (web -> api -> db) | https://academicrecords-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/users | 200 | True |
| coursemgmt-web CRUD read (web -> api -> db) | https://coursemgmt-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/courses | 200 | True |
| campushousing-web CRUD read (web -> api -> db) | https://campushousing-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/housing/rooms | 200 | True |
| finaid-web CRUD read (web -> api -> db) | https://finaid-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/invoices | 200 | True |
| eventscheduling-web CRUD read (web -> api -> db) | https://eventscheduling-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/rooms | 200 | True |
| AI profile: /api/assistant/mcp/integration returns 200 | https://finaid-web.bluesea-ede2f3bb.australiaeast.azurecontainerapps.io/api/assistant/mcp/integration | 200 | True |
