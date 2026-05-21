# Terraform + provider versions, and the AzureRM state backend.
#
# State lives in the same storage account the Function App uses
# (provisioned by infra/modules/functions.bicep, container `tfstate`).
# The MI used by cd-deploy.yml already has Storage Blob Data Owner at the
# storage-account scope, so cd-cloudflare.yml authenticates via OIDC to the
# same identity and writes here without an extra role grant.
#
# `storage_account_name` is salted (hash of the RG ID) so it isn't hard-coded
# below — cd-cloudflare.yml resolves it at run time and passes it via
# `terraform init -backend-config="storage_account_name=…"`.

terraform {
  required_version = ">= 1.6"

  required_providers {
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.0"
    }
  }

  backend "azurerm" {
    # The remaining fields (resource_group_name, storage_account_name,
    # container_name, key) come from `terraform init -backend-config=…` in
    # the workflow so we don't bake the salted storage name into the file.
    use_oidc         = true
    # The storage account has `allowSharedKeyAccess: false` (see
    # infra/modules/functions.bicep) so the AzureRM backend can't fall
    # back to listing keys for blob access. `use_azuread_auth = true`
    # tells it to use the OIDC-minted AAD token end-to-end — the MI
    # already has Storage Blob Data Owner at the storage-account scope.
    use_azuread_auth = true
  }
}
