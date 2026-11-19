using 'main.bicep'

// Non-secret settings for the PennyPath VM. The three per-person values are read from environment variables when you
// deploy (see docs/operations.md section 11):
//   ADMIN_SSH_PUBLIC_KEY  contents of your .pub key file
//   ADMIN_PRINCIPAL_ID    az ad signed-in-user show --query id -o tsv
//   ADMIN_IP              your public IPv4 (lets you set Key Vault secrets; SSH only if allowSshFromAdminIp)
param location = 'centralindia'
param namePrefix = 'pennypath'
// Must be unique in the region; the site becomes https://<dnsLabel>.centralindia.cloudapp.azure.com
param dnsLabel = 'pennypath-app'
param vmSize = 'Standard_B2s'
param githubRepository = 'NishaAalwin/PennyPath'
param githubEnvironment = 'production'
param allowSshFromAdminIp = false

param adminSshPublicKey = readEnvironmentVariable('ADMIN_SSH_PUBLIC_KEY')
param adminPrincipalId = readEnvironmentVariable('ADMIN_PRINCIPAL_ID')
param adminIpAddress = readEnvironmentVariable('ADMIN_IP', '')
