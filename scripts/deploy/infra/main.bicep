// Deploys the full integrated application: 5 databases, 5 backends, 5 frontends and the
// shared portal, all inside one Consumption-only Container Apps environment. Re-running this
// (e.g. with a new imageTag) updates the existing apps in place rather than duplicating them.
//
// AI baseline: enableAi defaults to false, which is what keeps AI-Mode/MCP/RAG/Multi-Agent
// disabled by default in the cloud per the brief. The ai-gateway/mcp-server/rag-server/
// multi-agent-server images are never deployed here at all - only -EnableAi (bonus profile)
// adds them, in a later script revision.

@description('Azure region. Must be one allowed by the subscription - check before deploying.')
param location string = resourceGroup().location

@description('GHCR namespace images are pulled from, e.g. ghcr.io/<owner>/campuspulse-<service>:<imageTag>.')
param githubOwner string

@description('Git SHA (or other unique tag) identifying which build to deploy. Never "latest" - see cloud-deployment-plan.md.')
param imageTag string

@description('false = required cloud baseline (AI disabled). true = bonus AI profile.')
param enableAi bool = false

@description('0 normally (scale-to-zero). 1 during the showcase/marking window.')
param minReplicas int = 0

var namePrefix = 'campuspulse'
var registry = 'ghcr.io/${githubOwner}'
var aiEnvValue = enableAi ? 'true' : 'false'

module environment 'environment.bicep' = {
  name: 'environment'
  params: {
    location: location
    namePrefix: namePrefix
  }
}

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
      env: db.env
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
]

var backends = [
  {
    name: 'academicrecords-api'
    needsCorsOrigin: false
    env: concat(sharedAiDisabled, [
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
      { name: 'Services__CourseManagementDatabase', value: 'http://coursemgmt-db' }
    ])
  }
  {
    name: 'coursemgmt-api'
    // CORS is a browser-enforced check on cross-origin requests; with the nginx /api/ proxy
    // every real request is same-origin, so this only matters for direct testing. The actual
    // origin value (which needs environment.outputs.defaultDomain, only known once deployment
    // starts) is appended below, outside this array - a for-loop's source array must be
    // resolvable before deployment starts, so it can't contain that reference itself.
    needsCorsOrigin: true
    env: concat(sharedAiDisabled, [
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
      { name: 'Services__CourseManagementDatabase', value: 'http://coursemgmt-db' }
    ])
  }
  {
    name: 'campushousing-api'
    needsCorsOrigin: false
    env: concat(sharedAiDisabled, [
      { name: 'Services__CampusHousingDatabase', value: 'http://campushousing-db' }
      { name: 'Services__AcademicRecordsDatabase', value: 'http://academicrecords-db' }
    ])
  }
  {
    name: 'finaid-api'
    needsCorsOrigin: false
    env: concat(sharedAiDisabled, [
      { name: 'Services__InvoiceDatabase', value: 'http://finaid-db' }
      { name: 'Services__AcademicRecordsBackend', value: 'http://academicrecords-api' }
    ])
  }
  {
    name: 'eventscheduling-api'
    needsCorsOrigin: false
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
      env: api.needsCorsOrigin
        ? concat(api.env, [
            { name: 'Cors__AllowedOrigins__0', value: 'https://coursemgmt-web.${environment.outputs.defaultDomain}' }
          ])
        : api.env
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
        { name: 'BACKEND_HOST', value: web.backend }
        // Azure Container Apps fronts internal calls on port 80 regardless of the target
        // port the backend actually listens on - confirmed by hand before writing this.
        { name: 'BACKEND_PORT', value: '80' }
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
      { name: 'ACADEMICRECORDS_WEB_URL', value: 'https://academicrecords-web.${environment.outputs.defaultDomain}' }
      { name: 'COURSEMANAGEMENT_WEB_URL', value: 'https://coursemgmt-web.${environment.outputs.defaultDomain}' }
      { name: 'CAMPUSHOUSING_WEB_URL', value: 'https://campushousing-web.${environment.outputs.defaultDomain}' }
      { name: 'FINAID_WEB_URL', value: 'https://finaid-web.${environment.outputs.defaultDomain}' }
      { name: 'EVENTSCHEDULING_WEB_URL', value: 'https://eventscheduling-web.${environment.outputs.defaultDomain}' }
    ]
    minReplicas: minReplicas
    maxReplicas: 2
  }
}

output portalUrl string = 'https://campuspulse-portal.${environment.outputs.defaultDomain}'
output frontendUrls array = [for web in frontends: 'https://${web.name}.${environment.outputs.defaultDomain}']
