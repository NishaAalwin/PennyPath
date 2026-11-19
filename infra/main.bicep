// PennyPath on one Azure Linux VM running the docker compose stack (decision recorded in
// brain/requirements/Constraints and decisions.md, #3). Deploy into an existing resource group:
//
//   export ADMIN_SSH_PUBLIC_KEY="$(cat ~/.ssh/pennypath_azure.pub)"
//   export ADMIN_PRINCIPAL_ID="$(az ad signed-in-user show --query id -o tsv)"
//   export ADMIN_IP="$(curl -s https://api.ipify.org)"
//   az deployment group create -g rg-pennypath -p infra/main.bicepparam
//
// Full steps: docs/operations.md section 11. No secret is passed to or stored by this template; app secrets are put
// into the Key Vault afterwards and read by the VM with its own managed identity.
targetScope = 'resourceGroup'

@description('Azure region. Central India keeps the data in India.')
param location string = resourceGroup().location

@description('Short name used in resource names.')
@maxLength(12)
param namePrefix string = 'pennypath'

@description('DNS label for the public IP. The site becomes https://<label>.<region>.cloudapp.azure.com. Must be unique in the region.')
@minLength(3)
@maxLength(63)
param dnsLabel string

@description('VM size. B2s (2 vCPU, 4 GB) runs Postgres, the API, the web server, Caddy and Mailpit.')
param vmSize string = 'Standard_B2s'

@description('Linux admin user name.')
param adminUsername string = 'pennyadmin'

@description('SSH public key for the admin user (the .pub file). Password sign-in is disabled.')
param adminSshPublicKey string

@description('Your public IPv4 address. Allowed through the Key Vault firewall (to set secrets) and, if allowSshFromAdminIp is true, to SSH.')
param adminIpAddress string = ''

@description('Open SSH (port 22) from adminIpAddress only. Off by default: deploys use Azure Run Command, not SSH.')
param allowSshFromAdminIp bool = false

@description('Object id of the person who manages the secrets: az ad signed-in-user show --query id -o tsv')
param adminPrincipalId string

@description('GitHub repository allowed to deploy, as owner/name.')
param githubRepository string

@description('GitHub environment the deploy job runs in. Only that environment can sign in to Azure.')
param githubEnvironment string = 'production'

@description('Size of the data disk that holds Docker (database, key ring, certificates).')
@minValue(16)
param dataDiskSizeGb int = 32

var suffix = uniqueString(resourceGroup().id)
var vmName = 'vm-${namePrefix}'
var keyVaultName = 'kv${take(namePrefix, 6)}${suffix}' // 3-24 chars, globally unique

// Built-in role ids.
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleKeyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var roleVirtualMachineContributor = '9980e02c-c2be-4d73-94e8-173b1dc7cf3c'

// ---------- Network: only 80 (certificate challenge + redirect) and 443 from the internet ----------
var sshRule = [
  {
    name: 'allow-ssh-admin'
    properties: {
      priority: 200
      direction: 'Inbound'
      access: 'Allow'
      protocol: 'Tcp'
      sourceAddressPrefix: adminIpAddress
      sourcePortRange: '*'
      destinationAddressPrefix: '*'
      destinationPortRange: '22'
    }
  }
]

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: 'nsg-${namePrefix}'
  location: location
  properties: {
    securityRules: concat([
      {
        name: 'allow-https'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
      {
        name: 'allow-http-for-certificates'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '80'
        }
      }
    ], (allowSshFromAdminIp && !empty(adminIpAddress)) ? sshRule : [])
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${namePrefix}'
  location: location
  properties: {
    addressSpace: { addressPrefixes: ['10.20.0.0/16'] }
    subnets: [
      {
        name: 'app'
        properties: {
          addressPrefix: '10.20.1.0/24'
          networkSecurityGroup: { id: nsg.id }
          // Lets the Key Vault firewall admit this subnet.
          serviceEndpoints: [{ service: 'Microsoft.KeyVault' }]
        }
      }
    ]
  }
}

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: 'pip-${namePrefix}'
  location: location
  sku: { name: 'Standard' }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: { domainNameLabel: dnsLabel }
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: 'nic-${namePrefix}'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: { id: vnet.properties.subnets[0].id }
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: { id: publicIp.id }
        }
      }
    ]
  }
}

// ---------- VM ----------
resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  identity: { type: 'SystemAssigned' } // reads secrets from Key Vault
  properties: {
    hardwareProfile: { vmSize: vmSize }
    securityProfile: {
      securityType: 'TrustedLaunch'
      uefiSettings: { secureBootEnabled: true, vTpmEnabled: true }
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      customData: base64(loadTextContent('cloud-init.yaml'))
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: adminSshPublicKey
            }
          ]
        }
        provisionVMAgent: true
        patchSettings: {
          patchMode: 'AutomaticByPlatform'
          assessmentMode: 'AutomaticByPlatform'
          automaticByPlatformSettings: { rebootSetting: 'IfRequired' }
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        managedDisk: { storageAccountType: 'StandardSSD_LRS' }
        deleteOption: 'Delete'
      }
      dataDisks: [
        {
          // Docker's data (Postgres, key ring, certificates). Detached, not deleted, if the VM is deleted.
          lun: 0
          name: 'disk-${namePrefix}-data'
          createOption: 'Empty'
          diskSizeGB: dataDiskSizeGb
          managedDisk: { storageAccountType: 'StandardSSD_LRS' }
          deleteOption: 'Detach'
        }
      ]
    }
    networkProfile: {
      networkInterfaces: [{ id: nic.id, properties: { deleteOption: 'Delete' } }]
    }
    diagnosticsProfile: { bootDiagnostics: { enabled: true } }
  }
}

// ---------- Key Vault: app secrets, readable only by the VM ----------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: [{ id: vnet.properties.subnets[0].id, ignoreMissingVnetServiceEndpoint: false }]
      ipRules: empty(adminIpAddress) ? [] : [{ value: adminIpAddress }]
    }
  }
}

resource vmReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, vm.id, roleKeyVaultSecretsUser)
  scope: keyVault
  properties: {
    principalId: vm.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
  }
}

resource adminManagesSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, adminPrincipalId, roleKeyVaultSecretsOfficer)
  scope: keyVault
  properties: {
    principalId: adminPrincipalId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsOfficer)
  }
}

// ---------- Identity GitHub Actions signs in as (OIDC, no stored secret) ----------
resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-github-deploy'
  location: location
}

resource githubFederation 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deployIdentity
  name: 'github-${githubEnvironment}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:${githubEnvironment}'
    audiences: ['api://AzureADTokenExchange']
  }
}

// Scoped to this one VM: lets the workflow run the deploy script on it, nothing else in the subscription.
resource deployerOnVm 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vm.id, deployIdentity.id, roleVirtualMachineContributor)
  scope: vm
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleVirtualMachineContributor)
  }
}

// ---------- Values to copy into GitHub repository variables (none of them is a secret) ----------
output SITE_ADDRESS string = publicIp.properties.dnsSettings.fqdn
output AZURE_VM_NAME string = vm.name
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output AZURE_KEY_VAULT_NAME string = keyVault.name
output AZURE_CLIENT_ID string = deployIdentity.properties.clientId
output AZURE_TENANT_ID string = subscription().tenantId
output AZURE_SUBSCRIPTION_ID string = subscription().subscriptionId
