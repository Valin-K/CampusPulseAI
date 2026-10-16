// Shared infrastructure: one Consumption-only Container Apps environment, Log Analytics
// (capped, so an idle deployment costs ~$0), and one storage account with a file share per
// database service (SQLite persistence survives scale-to-zero).

param location string
param namePrefix string = 'campuspulse'

@description('Database services that each get their own Azure Files share.')
param databaseShareNames array = [
  'academicrecords-data'
  'coursemgmt-data'
  'campushousing-data'
  'finaid-data'
  'eventscheduling-data'
]

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.1')
    }
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: replace('${namePrefix}sqlite', '-', '')
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource fileShares 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = [
  for shareName in databaseShareNames: {
    parent: fileServices
    name: shareName
    properties: {
      shareQuota: 1
    }
  }
]

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    // Consumption-only: no workloadProfiles entry means every app here runs on the shared
    // Consumption plan, which is what makes scale-to-zero cost ~$0 when idle.
    zoneRedundant: false
  }
}

// One managedEnvironments/storages definition per file share, so containerApp.bicep's
// volumeMount can reference each by name.
resource environmentStorages 'Microsoft.App/managedEnvironments/storages@2024-03-01' = [
  for (shareName, i) in databaseShareNames: {
    parent: containerAppsEnvironment
    name: shareName
    properties: {
      azureFile: {
        accountName: storageAccount.name
        accountKey: storageAccount.listKeys().keys[0].value
        shareName: fileShares[i].name
        accessMode: 'ReadWrite'
      }
    }
  }
]

output environmentId string = containerAppsEnvironment.id
output defaultDomain string = containerAppsEnvironment.properties.defaultDomain
output storageAccountName string = storageAccount.name
