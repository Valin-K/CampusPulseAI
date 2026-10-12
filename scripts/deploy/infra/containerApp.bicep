// Reusable module: one Container App. Called once per service (16 times) from main.bicep.
// Every app in this deployment shares the same shape (Consumption, 0.25 CPU / 0.5 GiB,
// scale-to-zero) - only ingress, port, env vars and replica/volume settings differ per app.

@description('Container App name - must be lowercase, matches the naming table in cloud-deployment-plan.md.')
param name string

@description('Azure region, inherited from the resource group by the deploy script.')
param location string

@description('Container Apps managed environment this app joins.')
param environmentId string

@description('Full image reference, e.g. ghcr.io/valin-k/campuspulse-eventscheduling-web:abc1234.')
param image string

@description('Port the container listens on inside the container.')
param targetPort int

@description('true = reachable from the internet (frontends/portal). false = reachable only from inside the environment (backends/databases).')
param external bool

@description('Environment variables for this container, as {name, value} pairs.')
param env array = []

@description('0 normally (scale-to-zero); 1 during the showcase/marking window (Deploy-Apps.ps1 -WarmUp).')
param minReplicas int = 0

@description('Databases must stay at 1 - SQLite only tolerates a single writer. Api/web apps can go slightly higher.')
param maxReplicas int = 1

@description('Optional: mounts an Azure Files share at this container path (databases only).')
param volumeMount object = {}

@description('Name of the environment storage definition to mount, required when volumeMount is set.')
param storageName string = ''

@description('Path the startup probe checks. Backends/databases expose /health; the nginx frontends and the portal only serve static content, so they use / instead.')
param healthPath string = '/health'

var hasVolume = !empty(volumeMount)

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: external
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: env
          volumeMounts: hasVolume
            ? [
                {
                  volumeName: 'data'
                  mountPath: volumeMount.mountPath
                }
              ]
            : []
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: healthPath
                port: targetPort
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              failureThreshold: 20
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
      volumes: hasVolume
        ? [
            {
              name: 'data'
              storageType: 'AzureFile'
              storageName: storageName
            }
          ]
        : []
    }
  }
}

@description('The app internal/public hostname. Internal apps are reachable by plain name (http://<name>) from inside the environment; this FQDN is mainly useful for the portal\'s external links.')
output fqdn string = containerApp.properties.configuration.ingress.fqdn
output name string = containerApp.name
