# Cloud Deployment — Plan and Current Stage

Target platform: **Microsoft Azure** (Container Apps + GitHub Container Registry). Covers marking criteria
**5 (Cloud Deployment Scripts)**, **6 (Cloud Deployment CI/CD Workflow)** and **7 (Integrated Cloud
Application – CRUD)**. AI-Mode, MCP, RAG and the Multi-Agent Server stay **disabled by default** in the
cloud, per the brief's baseline. Full architecture: [`cloud-deployment-plan.md`](cloud-deployment-plan.md).

---

## Current stage: everything is merged into `master`. Nothing has actually been deployed yet.

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

**Still needed**: push PR #93 with these two additional fixes, re-trigger `cloud-deployment.yml`, and
confirm `Test-CloudDeployment.ps1` reports 8/8 against the real Azure deployment.

---

## Azure OIDC setup

Done, on the Azure side:
- User-assigned managed identity `id-campuspulse-deploy` in `rg-campuspulse`
- Granted **Contributor, scoped only to `rg-campuspulse`** (confirmed via `az role assignment list`)
- Federated credential trusting `repo:Valin-K/CampusPulseAI:environment:production`

Still needed, on the GitHub side — **requires repo admin access** (this account gets a 404; confirmed
blocked). Valin-K needs to run:
```bash
gh api -X PUT /repos/Valin-K/CampusPulseAI/environments/production
gh secret set AZURE_CLIENT_ID --env production --repo Valin-K/CampusPulseAI --body "d97c9b6d-e15e-4921-9f02-d766cb0ff3f5"
gh secret set AZURE_TENANT_ID --env production --repo Valin-K/CampusPulseAI --body "e8911c26-cf9f-4a9c-878e-527807be8791"
gh secret set AZURE_SUBSCRIPTION_ID --env production --repo Valin-K/CampusPulseAI --body "cf883aba-8165-413a-b14b-b9c89f55654f"
```
**Status as of this writing: not yet confirmed done.** Check with:
```bash
gh api /repos/Valin-K/CampusPulseAI/environments --jq ".environments[].name"
gh api /repos/Valin-K/CampusPulseAI/environments/production/secrets --jq ".secrets[].name"
```
(Both 404 for this account too — needs Valin, or someone with read access, to check.)

---

## `cloud-deployment.yml` is manual-trigger only, deliberately

No `push:` trigger exists — only `workflow_dispatch`. This is intentional until a real end-to-end
deployment has succeeded; an automatic trigger on every merge would otherwise redeploy on every future
PR, including unrelated ones. **Switch it to `push: branches: [master]` only after a successful real run.**

---

## Next steps, in order

1. **Confirm Valin's `gh` commands have actually run** (see checks above).
2. **Manually trigger `cloud-deployment.yml`** from the Actions tab on GitHub — this is genuinely the
   first real end-to-end attempt; nothing like it has run before.
3. **Expect the first run to fail at the image-pull step.** The 16 GHCR packages are created private by
   default (confirmed repo is private). After the first push, go through each of the 16 new packages and
   flip visibility to Public — same one-time process done manually for the single test image earlier in
   this project's history.
4. **Re-trigger and confirm `Test-CloudDeployment.ps1` actually passes** — that's the real proof, not just
   "the workflow finished."
5. **Fix whatever else breaks.** Every other stage this session surfaced at least one real bug only
   visible once actually run (wrong port, an nginx `proxy_pass` gotcha, a hidden second `HttpClient` with
   a latent bug, a `for`-loop Bicep error) — there is no reason to expect the first real cloud run to be
   different. Budget for this.
6. **Collect evidence for the technical report**: CRUD screenshots per student in the cloud, the
   architecture diagram (Azure icons, not the ASCII sketch in this repo), the cloud deployment report, and
   the agentic-loop Cloud Deployment Review run against it.
7. **Only then**, switch the trigger to `push: branches: [master]`.

### Separately, unresolved
- The Visual Studio `docker-compose.dcproj` build (Windows reserved-port issue with
  `launch-local-ai-services.ps1`) was never confirmed fixed after the `winnat` restart — worth checking
  before assuming local deployment (Criterion 1) is solid.
- Bonus items (B1–B6) — correctly deferred until the above is proven working end-to-end.
