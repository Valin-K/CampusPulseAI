// Deploys the full integrated application: 5 databases, 5 backends, 5 frontends and the
// shared portal, all inside one Consumption-only Container Apps environment. Re-running this
// (e.g. with a new imageTag) updates the existing apps in place rather than duplicating them.
//
// AI baseline: enableAi defaults to false, which is what keeps AI-Mode/MCP/RAG/Multi-Agent
// disabled by default in the cloud per the brief. EnableAi adds the four internal AI apps.

@description('Azure region. Must be one allowed by the subscription - check before deploying.')
param location string = resourceGroup().location

@description('GHCR namespace images are pulled from, e.g. ghcr.io/<owner>/campuspulse-<service>:<imageTag>.')
param githubOwner string

@description('Git SHA (or other unique tag) identifying which build to deploy. Never "latest" - see cloud-deployment-plan.md.')
param imageTag string

@description('false = required cloud baseline (AI disabled). true = bonus AI profile.')
param enableAi bool = false

@secure()
@description('Gemini credential, stored as a Container Apps secret only on the AI gateway.')
param geminiApiKey string = ''

@secure()
@description('JWT signing key shared by secured services, stored as a Container Apps secret.')
param campusPulseSigningKey string

@description('0 normally (scale-to-zero). 1 during the showcase/marking window.')
param minReplicas int = 0

var namePrefix = 'campuspulse'
var registry = 'ghcr.io/${githubOwner}'
var aiEnvValue = enableAi ? 'true' : 'false'

// Internal-ingress apps (databases, backends, AI services) are NOT reachable by their plain
// name from other apps: confirmed by hand - nginx's own resolver (unlike nslookup/wget, which
// apply a search-domain suffix nginx's raw resolver does not) fails on the bare name with
// "could not be resolved", and Azure's own ingress.fqdn for an internal app is actually
// "<name>.internal.<environment-default-domain>". Internal ingress also requires HTTPS
// (containerApp.bicep sets allowInsecure: false), not plain HTTP. External apps (the 5 web
// frontends, the portal) use the same domain without ".internal." and are also HTTPS-only.
func internalUrl(appName string, domain string) string => 'https://${appName}.internal.${domain}'
func externalUrl(appName string, domain string) string => 'https://${appName}.${domain}'

module environment 'environment.bicep' = {
  name: 'environment'
  params: {
    location: location
    namePrefix: namePrefix
    databaseShareNames: concat([
      'academicrecords-data'
      'coursemgmt-data'
      'campushousing-data'
      'finaid-data'
      'eventscheduling-data'
    ], enableAi ? ['multiagent-data'] : [])
  }
}

// RAG holds its index in memory; Multi-Agent runs an in-process job queue. Keep one
// replica of each alive while AI is enabled, rather than losing state on idle scale-down.
var aiServices = [
  {
    name: 'ai-gateway'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
    ]
    minReplicas: minReplicas
  }
  {
    name: 'mcp-server'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'Services__AcademicRecordsBackend', value: 'http://academicrecords-api' }
      { name: 'Services__CourseManagementBackend', value: 'http://coursemgmt-api' }
      { name: 'Services__CampusHousingDatabase', value: 'http://campushousing-db' }
    ]
    minReplicas: minReplicas
  }
  {
    name: 'rag-server'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'Services__AiGateway', value: 'http://ai-gateway' }
    ]
    minReplicas: 1
  }
  {
    name: 'multi-agent-server'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'Services__AiGateway', value: 'http://ai-gateway' }
      { name: 'Services__McpServer', value: 'http://mcp-server' }
      { name: 'Services__RagServer', value: 'http://rag-server' }
      { name: 'MultiAgent__Enabled', value: 'true' }
      { name: 'MultiAgent__DatabasePath', value: '/app/data/multi-agent.db' }
    ]
    minReplicas: 1
  }
]

module aiApps 'containerApp.bicep' = [
  for ai in aiServices: if (enableAi) {
    name: 'app-${ai.name}'
    params: {
      name: ai.name
      location: location
      environmentId: environment.outputs.environmentId
      image: '${registry}/campuspulse-${ai.name}:${imageTag}'
      targetPort: 8080
      external: false
      env: concat(ai.env,
        ai.name == 'ai-gateway' ? [{ name: 'GEMINI_API_KEY', secretRef: 'gemini-api-key' }] : [],
        [{ name: 'Authentication__SigningKey', secretRef: 'campuspulse-signing-key' }])
      geminiApiKey: ai.name == 'ai-gateway' ? geminiApiKey : ''
      campusPulseSigningKey: campusPulseSigningKey
      minReplicas: ai.minReplicas
      maxReplicas: 1
      volumeMount: ai.name == 'multi-agent-server' ? { mountPath: '/app/data' } : {}
      storageName: ai.name == 'multi-agent-server' ? 'multiagent-data' : ''
    }
  }
]

// ---- Databases (SQLite on Azure Files; maxReplicas must stay 1) ----

var databases = [
  {
    name: 'academicrecords-db'
    shareName: 'academicrecords-data'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ConnectionStrings__AcademicRecords', value: 'Data Source=/app/data/academic-records.db' }
    ]
  }
  {
    name: 'coursemgmt-db'
    shareName: 'coursemgmt-data'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ConnectionStrings__CourseManagementDatabase', value: 'Data Source=/app/data/course-management.db;Foreign Keys=True' }
      { name: 'SyllabusStorage__RootPath', value: '/app/data/syllabi' }
    ]
  }
  {
    name: 'campushousing-db'
    shareName: 'campushousing-data'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ConnectionStrings__CampusHousing', value: 'Data Source=/app/data/campus-housing.db' }
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
    ]
  }
  {
    name: 'finaid-db'
    shareName: 'finaid-data'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ConnectionStrings__FinancialAidBilling', value: 'Data Source=/app/data/financial-aid-billing.db' }
    ]
  }
  {
    name: 'eventscheduling-db'
    shareName: 'eventscheduling-data'
    env: [
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ConnectionStrings__EventScheduling', value: 'Data Source=/app/data/event-scheduling.db' }
    ]
  }
]

module databaseApps 'containerApp.bicep' = [
  for db in databases: {
    name: 'app-${db.name}'
    params: {
      name: db.name
      location: location
      environmentId: environment.outputs.environmentId
      image: '${registry}/campuspulse-${db.name}:${imageTag}'
      targetPort: 8080
      external: false
      env: concat(db.env, [{ name: 'Authentication__SigningKey', secretRef: 'campuspulse-signing-key' }])
      campusPulseSigningKey: campusPulseSigningKey
      minReplicas: minReplicas
      maxReplicas: 1
      volumeMount: { mountPath: '/app/data' }
      storageName: db.shareName
    }
  }
]

// ---- Backends (internal-only; AI disabled by default) ----

var sharedAiDisabled = [
  { name: 'Mcp__Enabled', value: aiEnvValue }
  { name: 'Rag__Enabled', value: aiEnvValue }
  { name: 'MultiAgent__Enabled', value: aiEnvValue }
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
  { name: 'Services__AiGateway', value: 'http://ai-gateway' }
  { name: 'Services__McpServer', value: 'http://mcp-server' }
  { name: 'Services__RagServer', value: 'http://rag-server' }
  { name: 'Services__MultiAgentServer', value: 'http://multi-agent-server' }
]

var backends = [
  {
    name: 'academicrecords-api'
    env: concat(sharedAiDisabled, [
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
      { name: 'Services__CourseManagementDatabase', value: 'http://coursemgmt-db' }
    ])
  }
  {
    name: 'coursemgmt-api'
    // CORS is a browser-enforced check on cross-origin requests; with the nginx /api/ proxy
    // every real request is same-origin, so this only matters for direct testing. The
    // allowed origin is set to the public frontend URL at the module call site.
    env: concat(sharedAiDisabled, [
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
      { name: 'Services__CourseManagementDatabase', value: 'http://coursemgmt-db' }
    ])
  }
  {
    name: 'campushousing-api'
    env: concat(sharedAiDisabled, [
      { name: 'Services__CampusHousingDatabase', value: 'http://campushousing-db' }
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
    ])
  }
  {
    name: 'finaid-api'
    env: concat(sharedAiDisabled, [
      { name: 'Services__InvoiceDatabase', value: 'http://finaid-db' }
      { name: 'Services__AcademicRecordsBackend', value: 'http://academicrecords-api' }
    ])
  }
  {
    name: 'eventscheduling-api'
    env: concat(sharedAiDisabled, [
      { name: 'Services__EventDatabase', value: 'http://eventscheduling-db' }
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
    ])
  }
]

module backendApps 'containerApp.bicep' = [
  for api in backends: {
    name: 'app-${api.name}'
    params: {
      name: api.name
      location: location
      environmentId: environment.outputs.environmentId
      image: '${registry}/campuspulse-${api.name}:${imageTag}'
      targetPort: 8080
      external: false
      env: api.name == 'coursemgmt-api'
        ? concat(api.env, [
            { name: 'Cors__AllowedOrigins__0', value: 'https://coursemgmt-web.${environment.outputs.defaultDomain}' }
            { name: 'Authentication__SigningKey', secretRef: 'campuspulse-signing-key' }
          ])
        : concat(api.env, [{ name: 'Authentication__SigningKey', secretRef: 'campuspulse-signing-key' }])
      campusPulseSigningKey: campusPulseSigningKey
      minReplicas: minReplicas
      maxReplicas: 2
    }
  }
]

// ---- Frontends (external; nginx proxy to the matching backend) ----

var frontends = [
  { name: 'academicrecords-web', backend: 'academicrecords-api' }
  { name: 'coursemgmt-web', backend: 'coursemgmt-api' }
  { name: 'campushousing-web', backend: 'campushousing-api' }
  { name: 'finaid-web', backend: 'finaid-api' }
  { name: 'eventscheduling-web', backend: 'eventscheduling-api' }
]

module frontendApps 'containerApp.bicep' = [
  for web in frontends: {
    name: 'app-${web.name}'
    params: {
      name: web.name
      location: location
      environmentId: environment.outputs.environmentId
      image: '${registry}/campuspulse-${web.name}:${imageTag}'
      targetPort: 80
      external: true
      healthPath: '/'
      env: [
        { name: 'BACKEND_BASE_URL', value: internalUrl(web.backend, environment.outputs.defaultDomain) }
        { name: 'ACADEMICRECORDS_API_BASE_URL', value: internalUrl('academicrecords-api', environment.outputs.defaultDomain) }
      ]
      minReplicas: minReplicas
      maxReplicas: 2
    }
  }
]

// ---- Shared portal ----

module portal 'containerApp.bicep' = {
  name: 'app-campuspulse-portal'
  params: {
    name: 'campuspulse-portal'
    location: location
    environmentId: environment.outputs.environmentId
    image: '${registry}/campuspulse-portal:${imageTag}'
    targetPort: 80
    external: true
    healthPath: '/'
    env: [
      { name: 'ACADEMICRECORDS_WEB_URL', value: externalUrl('academicrecords-web', environment.outputs.defaultDomain) }
      { name: 'COURSEMANAGEMENT_WEB_URL', value: externalUrl('coursemgmt-web', environment.outputs.defaultDomain) }
      { name: 'CAMPUSHOUSING_WEB_URL', value: externalUrl('campushousing-web', environment.outputs.defaultDomain) }
      { name: 'FINAID_WEB_URL', value: externalUrl('finaid-web', environment.outputs.defaultDomain) }
      { name: 'EVENTSCHEDULING_WEB_URL', value: externalUrl('eventscheduling-web', environment.outputs.defaultDomain) }
      { name: 'ACADEMICRECORDS_BACKEND_BASE_URL', value: internalUrl('academicrecords-api', environment.outputs.defaultDomain) }
      { name: 'ACADEMICRECORDS_FRONTEND_BASE_URL', value: externalUrl('academicrecords-web', environment.outputs.defaultDomain) }
    ]
    minReplicas: minReplicas
    maxReplicas: 2
  }
}

output portalUrl string = 'https://campuspulse-portal.${environment.outputs.defaultDomain}'
output frontendUrls array = [for web in frontends: 'https://${web.name}.${environment.outputs.defaultDomain}']
