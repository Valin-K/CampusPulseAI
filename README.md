# CampusPulseAI

CampusPulseAI is a .NET 10 campus-services solution containing five student applications, shared frontend assets, backend APIs, and AI services. The repository includes a Visual Studio Docker Compose project for running the complete environment.

## Prerequisites

Install the following on Windows before opening the solution:

1. **Visual Studio Community 2026** (18.9 or later)
   - Install the **ASP.NET and web development** workload.
   - In the Visual Studio Installer, also ensure **Container Development Tools** is installed. This provides Visual Studio Container Tools and the Docker Compose integration.
   - Sign in to Visual Studio if your Community licence requires it.

2. **.NET SDK 10.0.302**
   - The repository pins this SDK in `global.json`.
   - Install the Windows x64 SDK from the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
   - The installed SDK must be `10.0.302`; the repository allows only the latest patch for that SDK feature band.

3. **WSL 2**
   - Open Windows Terminal or PowerShell as Administrator and run:

     ```powershell
     wsl --install
     ```

   - Restart Windows when prompted, then complete the Linux distribution setup.
   - Confirm WSL 2 is active:

     ```powershell
     wsl --status
     wsl --update
     ```

   - If an existing distribution is using WSL 1, convert it with `wsl --set-version <DistributionName> 2`.

4. **Docker Desktop for Windows**
   - Install [Docker Desktop](https://www.docker.com/products/docker-desktop/).
   - In Docker Desktop, enable **Use the WSL 2 based engine** under **Settings > General**.
   - Under **Settings > Resources > WSL Integration**, enable integration for the distribution used by the project.
   - Start Docker Desktop and wait until it reports that Docker is running.

5. **Git**
   - Install [Git for Windows](https://git-scm.com/download/win) and make sure `git` is available in a new terminal.

### Verify the installation

Open a new PowerShell window and run:

```powershell
dotnet --version
wsl --status
docker version
docker compose version
git --version
```

`dotnet --version` must print `10.0.302`. If it does not, install that SDK or open the repository from a terminal where the correct SDK is available.

## Clone and open the solution

```powershell
git clone https://github.com/Valin-K/CampusPulseAI.git
cd CampusPulseAI
```

Open `CampusPulseAI.slnx` in Visual Studio 2026. The solution includes the `docker-compose.dcproj` project and the five student applications, their tests, and the AI services.

When the solution is first opened, allow Visual Studio to restore NuGet packages. Package versions are managed centrally in `Directory.Packages.props`.

## Run the complete environment in Visual Studio

1. Start Docker Desktop and confirm it is running.
2. Open `CampusPulseAI.slnx`.
3. In **Solution Explorer**, right-click the solution and choose **Set Startup Projects**.
4. Select **Multiple startup projects** if necessary and set `docker-compose` to **Start**. Alternatively, select the `docker-compose` project directly and choose **Set as Startup Project**.
5. In the Visual Studio debug target selector, choose **Docker Compose**.
6. Press **F5** to build the images, start the services, attach the debugger, and launch the configured browser target. Use **Ctrl+F5** to run without the debugger.

The Compose configuration starts the databases, backend APIs, shared AI gateway, and frontend containers. Visual Studio starts MCP and RAG on the host through `scripts\launch-local-ai-services.ps1`; multi-agent routing is not part of this gateway. The frontend URLs are:

| Application | URL |
| --- | --- |
| Academic Records | `http://localhost:8081` |
| Course Management | `http://localhost:8082` |
| Campus Housing | `http://localhost:8083` |
| Financial Aid and Billing | `http://localhost:8084` |
| Event Scheduling | `http://localhost:8085` |
| Shared UI | `http://localhost:8080` |

Backend containers expose port `8080` inside the Compose network. Visual Studio may assign host ports dynamically for services that do not have an explicit host-port mapping. Use the **Containers** window to see the actual host ports and service status.

## Use the Visual Studio Containers window

Visual Studio provides a graphical view of the Docker resources used by the solution:

1. Start Docker Desktop.
2. Open **View > Other Windows > Containers**.
3. Select the **Containers** tab and expand the Compose application.
4. Select a container to inspect its state, image, ports, environment variables, volumes, and logs.
5. Use the container toolbar to stop, start, restart, or remove containers. Use the **Logs** view when a service does not start.
6. Open a mapped port from the container details to navigate to that service in a browser.

Do not remove running containers while Visual Studio is debugging unless you intend to stop the current Compose session. Stop debugging first, then restart the Compose profile to recreate the environment.

## Run with Docker Compose from a terminal

The same environment can be started without Visual Studio:

```powershell
docker compose up --build
```

Run in the background with:

```powershell
docker compose up --build -d
```

View running services and logs:

```powershell
docker compose ps
docker compose logs -f
```

Stop the environment:

```powershell
docker compose down
```

To rebuild after Dockerfile or project changes:

```powershell
docker compose down
docker compose build --no-cache
docker compose up
```

The Docker build context must be the repository root because the Dockerfiles copy `Directory.Packages.props` and project files from the root-level directory structure.

## Shared Gemini gateway

Set `GEMINI_API_KEY` in the repository-root `.env` (never commit it). Compose injects it
**only into `ai.gateway`**, not into any student backend or the RAG server.

- `shared\backend\CampusPulse.Shared.Ai` is the reusable client and contracts project.
- `shared\backend\CampusPulse.Shared.Ai.Gateway` is the separately hosted gateway.
- All five student backends call `IAiGatewayClient.GenerateAsync(prompt, context, cancellationToken)`.
  They retain their own prompts, data access, validation, and business logic.
- RAG uses the same gateway for generation and embeddings. Retrieval and citations remain in RAG.
- This is a Gemini transport gateway, **not** a multi-agent server, router, or tool executor.

Start the gateway with `docker compose up --build -d ai.gateway`. For terminal-based
development, also run `.\scripts\launch-local-ai-services.ps1` for MCP and RAG.
Host services connect to `http://localhost:5269`; Compose backends use `http://ai.gateway:8080`.
Override `Services__AiGateway` when using another address. Only the gateway configures
`Gemini__GenerationModel`, `Gemini__EmbeddingModel`, and `Gemini__EmbeddingDimensions`.

The HTTP API is `POST /api/ai/generate` with `{ "prompt": "...", "context": "..." }`,
returning `{ "text": "...", "model": "..." }`; `POST /api/ai/embeddings` accepts
`{ "texts": ["..."], "isQuery": false }`. `GET /api/ai/status` reports model
configuration and availability without exposing the key. Generation/embedding errors
are explicit; a missing key returns 503. RAG retains local hashed retrieval when the
gateway explicitly reports no key. Restart RAG and re-ingest after changing embedding
configuration.

The gateway has no public authentication layer: it is for the trusted Compose network
and its host port is bound to loopback only. Do not expose it publicly without
service authentication and rate limiting. The separate Python developer agentic-loop
tool below is not part of the application gateway migration.

## Run and test .NET projects locally

To restore, build, and test the solution without containers:

```powershell
dotnet restore .\CampusPulseAI.slnx
dotnet build .\CampusPulseAI.slnx
dotnet test .\CampusPulseAI.slnx
```

Individual projects can be run with `dotnet run --project`, for example:

```powershell
dotnet run --project .\student-5\backend\EventScheduling.Backend\EventScheduling.Backend.csproj
```

The local Event Scheduling backend uses `http://localhost:5021` when started with its `http` launch profile. Frontend projects are Blazor WebAssembly applications and are normally easiest to run through Visual Studio or their project launch profile.

## Run the shared agentic loop

The agentic loop is a Python implementation of the **Plan -> Act -> Observe -> Adapt -> Verify** workflow. It validates the entire CampusPulseAI solution (all five student services, shared infrastructure, Docker Compose, and live HTTP endpoints) using one or more OpenRouter models.

### Configure the API key

1. Copy the environment template to `.env` in the repository root:

   ```powershell
   Copy-Item .env.template .env
   ```

2. Replace the placeholder API key in `.env` with your actual OpenRouter key:

   ```powershell
   (Get-Content .env) `
       -replace 'OPENROUTER_API_KEY=sk-or-v1-your-key-here', `
                'OPENROUTER_API_KEY=sk-or-v1-your-actual-key' `
       | Set-Content .env
   ```

   The `.env` file is ignored by Git and is never written to evidence files.

3. (Optional) Override per-stage models in `.env`:

   ```text
   AGENTIC_PLAN_MODEL=qwen/qwen3.8-27b
   AGENTIC_ACT_MODEL=qwen/qwen3.8-27b
   AGENTIC_REVIEW_MODEL=gemini-3.5-flash
   AGENTIC_PROVIDER=openrouter
   GEMINI_MODEL=gemini-3.5-flash
   ```

   Setting a stage model to a Gemini model name routes only that stage to Gemini; other stages stay on OpenRouter. If your OpenRouter key hits its limit, the loop automatically falls back to Gemini when `GEMINI_API_KEY` is available.

### Approved context

The loop reads an approved project-context file so the agents stay grounded in
facts about the existing solution:

```text
docs\release-0\agentic-loop-context\campuspulseai-context.txt
```

See [docs\release-0\campuspulseai-agentic-loop-learning-guide.md](docs/release-0/campuspulseai-agentic-loop-learning-guide.md) for a full explanation of the workflow.

### Install Python dependencies

From the repository root:

```powershell
pip install -r scripts\agentic-loop\requirements.txt
```

### Run a review-only loop

```powershell
python scripts\agentic-loop\agentic_loop.py `
    --context docs\release-0\agentic-loop-context\campuspulseai-context.txt
```

This runs the deterministic checks, calls the AI planning/implementation/review agents, and writes a timestamped evidence file to `docs\release-0\agentic-loop-evidence\`. It does not modify source code.

### Run with a specific focus

```powershell
python scripts\agentic-loop\agentic_loop.py `
    --context docs\release-0\agentic-loop-context\campuspulseai-context.txt `
    --focus architecture
```

Valid focuses: `implementation`, `database`, `architecture`, `devops`.

### Apply an approved adaptation

After reviewing the plan and the implementation proposal, apply a patch that has been approved by a human:

```powershell
python scripts\agentic-loop\agentic_loop.py `
    --context docs\release-0\agentic-loop-context\campuspulseai-context.txt `
    --approved-patch C:\path\to\approved-change.patch `
    --approval-text "Approved after reviewing the plan, tests, and patch." `
    --apply
```

The script validates that the patch only targets files inside the repository, applies it with `git apply`, and then reruns the same deterministic checks to verify the change.

### Evidence

Each run writes one JSON file under `docs\release-0\agentic-loop-evidence\`. The file records:

- the exact model identifiers used;
- every prompt sent to the AI agents;
- raw AI outputs;
- the project context supplied to the agents;
- all deterministic check results (build, test, Docker Compose, HTTP smoke tests);
- human approval text and applied patch targets;
- branch and commit identifiers;
- any error that caused the run to fail.

API keys are never recorded in evidence files.

## GitHub Actions files in Visual Studio

The workflow definitions are stored in `.github/workflows/`. This repository currently contains workflows for each student project, integration, and cloud deployment.

To view and work with these files inside Visual Studio:

1. Open **Extensions > Manage Extensions**.
2. Search for **.GitHub + MCP**.
3. Install the extensionb and restart Visual Studio when prompted.
4. In **Solution Explorer**, enable **Show All Files** if `.github` is not visible, then expand `.github > workflows`.

The workflows execute on GitHub, not inside Docker Desktop. Use the **GitHub Actions** view or the repository's **Actions** tab on GitHub to inspect hosted-run logs and results.

## Repository layout

- `student-1` through `student-5`: backend, Blazor WebAssembly frontend, tests, and database scripts for each campus service.
- `ai-services`: MCP, multi-agent, and RAG servers.
- `shared`: shared configuration and frontend assets.
- `docker-compose.yml`: image/build definitions and explicit frontend port mappings.
- `docker-compose.override.yml`: development environment variables, ports, and local certificate/user-secret mounts.
- `global.json`: required .NET SDK version.
- `Directory.Packages.props`: centrally managed NuGet package versions.
- `.github/workflows`: GitHub Actions CI and deployment workflows.

## Troubleshooting

### Docker Compose is unavailable in Visual Studio

Verify that Docker Desktop is running and that **Container Development Tools** is installed in the Visual Studio Installer. Close and reopen Visual Studio after installing the workload or extension.

### WSL or Docker cannot start

Run `wsl --update`, restart Docker Desktop, and confirm **Use the WSL 2 based engine** and the required distribution integration are enabled. Do not run Docker Desktop and another Docker daemon at the same time.

### SDK version errors

Run `dotnet --list-sdks` and confirm `10.0.302` is installed. Because `global.json` pins this version, installing only an older .NET 10 SDK is not sufficient.

### A frontend port is already in use

Stop the process using the port or change the host-side port in `docker-compose.yml`. The container continues to listen on port `80`; only the host-side value needs to change.

### A container exits immediately

Open **View > Other Windows > Containers**, select the stopped container, and inspect its logs. From a terminal, use `docker compose logs <service-name>` and confirm that the image built successfully from the repository root.
