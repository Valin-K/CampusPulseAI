# Cloud Deployment — Plan and Current Stage

Target platform: **Microsoft Azure** (Container Apps + GitHub Container Registry). Covers marking criteria
**5 (Cloud Deployment Scripts)**, **6 (Cloud Deployment CI/CD Workflow)** and **7 (Integrated Cloud
Application – CRUD)**. AI-Mode, MCP, RAG and the Multi-Agent Server stay **disabled by default** in the
cloud, per the brief's baseline. Full architecture: [`cloud-deployment-plan.md`](cloud-deployment-plan.md).

---

## Current status: a successful cloud deployment is now verified

GitHub Actions Cloud Deployment run [#275](https://github.com/Valin-K/CampusPulseAI/actions/runs/39611514822)
completed successfully on 2026-10-21. It was manually dispatched from branch
`Ryan-cloud-smoke-test` at commit `cd8ebcf935464f63e552f06e030f47eeeb861974` (not from `master`).
The `deploy` job completed Azure login, infrastructure deployment, app deployment, smoke testing,
and evidence upload successfully. The uploaded evidence is also saved under
[`cloud-deployment-evidence/20261021-135602/`](cloud-deployment-evidence/20261021-135602/):
**8/8 checks passed**—portal, sign-in, authenticated CRUD reads for all five services, and the
enabled Financial Aid MCP integration.

The earlier `20261019-233153` report is a historical failed run (1/8), not the latest result.
Run #275 is a real successful deployment, but its branch matters: repeat the deployment from
`master` after the fixes are merged before claiming the master revision is verified.

The latest `master` dispatch before Run #275 was [run #265](https://github.com/Valin-K/CampusPulseAI/actions/runs/39386062433)
on 2026-10-20 and it failed before smoke testing. Its logs show the AI-enabled deploy stopped
because `GEMINI_API_KEY` was unavailable; provider-registration attempts also logged
authorization failures. Do not describe #265 as a failed 1/8 smoke test—the smoke-test step
never ran. Run #275 is newer and succeeded, but on the separate `Ryan-cloud-smoke-test` branch.

The latest Shared MCP Server CI run available during this check (#130) succeeded but detected
no MCP changes and skipped its build and smoke-test steps. Source inspection confirms MCP
authorization is configured. The current working-tree change makes that requirement explicit
on both MCP endpoints and adds anonymous-request checks to MCP CI; those new checks have not
yet run on GitHub.

All of the following are **on `master`**, confirmed via `git log origin/master`:

| PR | What it added |
|---|---|
| #85 | CI workflow trigger fixes (all 9 workflows); Event Scheduling cloud-readiness |
| #86 | Unrelated UI fix (equipment-return popup removal) |
| #87 | Academic Records, Course Management, Campus Housing, Financial Aid cloud-readiness; the shared `nginx` proxy + `BackendUrl` fallback + AI kill-switch pattern applied to all 4 |
| #88 | Shared `shared-ui` portal Dockerfile; Bicep infrastructure (`scripts/deploy/infra/`); PowerShell deploy scripts (`scripts/deploy/*.ps1`); `cloud-deployment.yml` |

**Every one of the 5 services, plus the portal, has the full cloud-readiness pattern applied and verified**
(real `docker compose` stack, real browser checks, real test suites — not assumed). See the commit
history / PR descriptions on GitHub for the detailed per-service verification notes; they're not
repeated here since they're now part of the permanent record, not something still pending.

### What HAS happened: a real deployment, which surfaced (and now fixes) a genuine networking bug

`workflow_dispatch` was triggered for a real end-to-end run. The build/push/deploy stages all succeeded,
but `Test-CloudDeployment.ps1` only passed 1/8 checks — the portal loaded, but every frontend's `/api/`
proxy (and both AI-gate checks) returned `502 Bad Gateway`, even though the backends were confirmed
healthy via Log Analytics.

Root cause, found via real Log Analytics queries against the frontend containers' own nginx logs (not
guesswork): nginx's resolver does **raw DNS lookups without search-domain suffixing** — unlike `nslookup`
or `wget` inside the same container, which DO resolve a bare short name like `academicrecords-api`. Azure
Container Apps' internal-ingress apps are only reachable via their full FQDN,
`https://<app-name>.internal.<environment-default-domain>` — a bare name, and plain HTTP, both fail.
Confirmed via `az containerapp show --query properties.configuration.ingress.fqdn` on two different apps.

**Fix applied**: `main.bicep` now computes every frontend-to-backend and portal-to-service env var through
two new `internalUrl()` / `externalUrl()` functions, producing a single pre-composed `*_BASE_URL` value
(e.g. `BACKEND_BASE_URL`) per connection instead of separate `*_HOST`/`*_PORT` pairs. All 6 nginx
`*.conf.template` files and the relevant Dockerfiles were updated to match (local defaults stay plain
`http://service:8080`, unchanged behavior for local `docker compose`). A CORS-origin entry on
`coursemgmt-api` needed the *external* (public) URL instead — handled via an `'external://'` prefix
convention on that one env entry, since Bicep for-expressions can't be nested inside `concat()` (BCP138).

Separately, while re-verifying locally, the portal's `40-render-portal-links.sh` initially crash-looped
the `shared-ui` container (exit 127, shebang "not found"). Root cause was CRLF line endings that had
crept into this local working-tree checkout only — the committed blob on `master` is already LF, so this
never affected the actual cloud deployment or other checkouts. Re-normalized locally; nothing to commit.

All of the above was validated with `az bicep build` (clean compile) and a real local `docker compose up`
run: all 6 frontends' `/api/` (and `/academic-api/`) proxies now correctly reach their backends (401s —
auth required — not 502s), and the portal serves its root page and proxies `/api/` correctly.

### Round 2: re-deployed with the FQDN/HTTPS fix, still 1/8 — two more real bugs, found live

PR [#93](https://github.com/Valin-K/CampusPulseAI/pull/93) (`Ryan-fix-nginx-resolver`) carried the fix
above. `workflow_dispatch` ran it end-to-end — build, push, and `Deploy infrastructure`/`Deploy apps` all
succeeded — but `Test-CloudDeployment.ps1` still only passed 1/8, identical symptom (`502` on every
frontend `/api/` proxy). This ruled out "stale code": the fix was genuinely live, something else was
still broken. Found via `az containerapp exec` into a live frontend container and `curl -v` straight at
the internal backend FQDN (the Portal's own **Console** tab under a Container App does the same thing):

1. **HTTP/2 cleartext (h2c) negotiation hang.** With `transport: 'auto'` (the Container Apps default),
   Envoy's internal hop to Kestrel silently hung forever on every request — confirmed by forcing
   `curl --http1.1` against the same URL, which returned instantly. Fix: `containerApp.bicep`'s ingress
   now pins `transport: 'http'` (HTTP/1.1 only) — nothing in this stack needs HTTP/2 internally.
2. **Missing SNI on nginx's upstream TLS connection.** Azure's internal ingress routes by SNI (multiple
   internal apps share one load-balancer IP — confirmed two different apps resolving to the identical
   internal IP). `curl` always sends SNI by default and worked; nginx's `proxy_pass` to an HTTPS upstream
   does **not** send SNI unless told to, so Envoy couldn't match the request to a backend and reset the
   TLS handshake — matching the earlier Log Analytics `SSL_do_handshake() failed (104: Connection reset by
   peer)` error exactly. Fix: added `proxy_ssl_server_name on;` to all 6 nginx `*.conf.template` files.

Both fixes were spot-checked live via `az containerapp exec` against the already-deployed apps (direct
`curl` to the internal backend FQDN now returns `200`/`401` as expected, not a hang) before being written
into the repo, so this isn't guesswork — but the nginx-side fix (`proxy_ssl_server_name`) still needs a
real image rebuild + redeploy to verify end-to-end, since it couldn't be hot-patched into the running
nginx containers cleanly via `exec`.

PR #93 was re-pushed with both fixes and `cloud-deployment.yml` re-ran end-to-end for real. 502s are gone
— progress — but the smoke test still only passed 1/8, now against a different symptom: every `/api/`
proxy returns `404`, and the response body is Azure's own **"Container App - Unavailable"** platform error
page, not nginx's 404 and not the backend app's. That's Envoy failing to match the request to a container
app at all.

### Round 3: wrong Host header breaking Azure's own routing

Root cause: Azure Container Apps' internal ingress routes by **Host header**, not only SNI. Our nginx
config explicitly forwards the original client's Host (`proxy_set_header Host $host;` — e.g. the
*frontend's* own hostname, `academicrecords-web...`) to the backend proxy target. Envoy then can't match
that Host to the backend app (`academicrecords-api...`) it's actually being asked to route to, and falls
back to its platform error page. `curl` worked in every manual test because it auto-sets Host to match
the URL it's connecting to; nginx does not do this by default when proxying to a variable-based upstream.

**Fix**: changed every `proxy_set_header Host $host;` forwarding to a backend/upstream app (11 occurrences
across all 6 nginx templates) to `proxy_set_header Host $proxy_host;` — nginx's built-in variable holding
the host:port from the resolved `proxy_pass` target, which correctly matches the backend being called.

**Verified by Run #275**: the fix was deployed, and `Test-CloudDeployment.ps1` reported 8/8
against the Azure deployment. The evidence artifact records successful sign-in, all five
authenticated CRUD reads, and the enabled AI/MCP integration check.

---

## Azure OIDC setup

**Operationally verified by Run #275**:
- `azure/login@v2` succeeded in the production environment, so the workflow had a usable Azure federated-login configuration.
- Infrastructure and app deployment succeeded.
- The smoke artifact includes the enabled AI/MCP check; the deploy script reads both the signing key and Gemini key from Key Vault for that profile. Therefore the workflow identity could read the required Key Vault secrets during that run.

This proves the deployment path worked, but it does **not** reveal the exact Azure role assignments or prove least-privilege scope. The Azure CLI is not installed in this workspace, so live role assignments, storage network rules, encryption configuration, and backup settings were not independently queried. A successful run also does not prove that GitHub production-environment reviewer or branch restrictions are configured. Verify those settings directly in Azure/GitHub and save redacted output.

---

## Repository security-screening status

The pre-commit security hooks now cover repository-wide secret scanning, Python Ruff security
rules, plus .NET security analyzers and NuGet audits across all projects. Python dependency
vulnerability auditing is omitted because Python is not a production application in this project.
Validation of the expanded checks found:

- .NET security analyzers: passed for all 34 projects.
- NuGet vulnerability audit: passed for all 34 projects.
- Python Ruff security rules: passed across repository Python files.
- `detect-secrets`: **not clean**. The first all-files run reported 20 candidate detections in
  source, tests, templates, README, and generated evidence. These have not been individually
  classified or added to the baseline. Review them before treating secret scanning as passed;
  do not baseline a real credential simply to make the hook green.

The expanded Ruff scan also prompted HTTPS-or-loopback-only checks for authentication/service
URLs and explicit safe-argv annotations for internal subprocess calls. The Git executable is
resolved from PATH rather than invoked by a partial executable name.

Expanding .NET analyzer coverage also found a CA2100 warning on a conditional SQL statement in
`WorkflowStore.Save`. The query strings are fixed constants; the assignment was split into
explicit branches, and the security analyzer then passed. The focused multi-agent workflow and
authorization tests passed (21 tests).

---

## `cloud-deployment.yml` is manual-trigger only, deliberately

No `push:` trigger exists — only `workflow_dispatch`. This is intentional until a real end-to-end
deployment has succeeded; an automatic trigger on every merge would otherwise redeploy on every future
PR, including unrelated ones. **Switch it to `push: branches: [master]` only after a successful real run.**

---

## Next steps, in order

1. Merge the verified deployment and current security changes to `master`, then dispatch the workflow from `master` and preserve its new evidence artifact.
2. Verify GitHub production-environment required reviewers/branch restrictions and query live Azure RBAC assignments. Confirm the deploy identity is scoped only to required resources and Key Vault secret reads. Pre-register Azure resource providers administratively so the deploy identity does not need subscription-level provider-registration permission.
3. Query the storage account's encryption, public-network/firewall/private-endpoint, and Azure Files backup settings. Add a restore drill and retain redacted configuration evidence.
4. Keep GHCR public-image exposure documented or migrate to a private registry with scoped pull access; add image scanning/signing and immutable digest deployment.
5. Ensure MCP CI actually runs for changes and fails if anonymous tool listing or tool execution is not rejected with 401. The latest run #130 skipped its smoke tests because it detected no MCP changes.
6. Collect the architecture diagram and any remaining presentation evidence. Keep automatic deployment disabled until the successful `master` run and production protections are confirmed.

### Separately, unresolved
- The Visual Studio `docker-compose.dcproj` build (Windows reserved-port issue with
  `launch-local-ai-services.ps1`) was never confirmed fixed after the `winnat` restart — worth checking
  before assuming local deployment (Criterion 1) is solid.
- Bonus items (B1–B6) — correctly deferred until the above is proven working end-to-end.
