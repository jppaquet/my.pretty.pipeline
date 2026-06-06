# `infra/cloudflare/` — Terraform layer for Cloudflare

Manages the DNS records for the Function App's custom domain: the
`func.<domain>` CNAME and the `asuid.func.<domain>` TXT ownership
challenge Azure requires before binding the hostname.

## ⚠️ Shared zone — boundary with my.pretty.blender

The `prettynotifier.com` Cloudflare zone is managed by **two independent
Terraform stacks in two repos**, each owning a **disjoint** set of records.
This is safe — Terraform only manages the resources it declares, and the
`data.cloudflare_zone` lookup is read-only — **as long as the two stacks
never declare the same record**.

| Owner | Manages |
|---|---|
| **this repo (pipeline)** | `func`, `asuid.func` (this Function App's custom domain) |
| **my.pretty.blender** | `admin`, `api`, `asuid.api` + Email Routing (`alerts@` → worker) — see that repo's `infra/cloudflare/` |

**Do not** add an `admin/api/email-routing` record here, and do not add a
`func.*` record in blender. Each repo carries its own duplicated
`CLOUDFLARE_API_TOKEN` / `CLOUDFLARE_ACCOUNT_ID` secrets so it deploys
standalone. The MX/SPF records stay CF-managed (auto-created when Email
Routing is enabled in the dashboard).

## Running locally

```sh
export TF_VAR_domain=<your-domain>                                    # GH var
export TF_VAR_cloudflare_api_token=<api-token>                        # GH secret
export TF_VAR_function_app_hostname=$(az functionapp show -g rg-notify-dev -n <fname> --query defaultHostName -o tsv)
export TF_VAR_function_app_custom_domain_verification_id=$(az functionapp show -g rg-notify-dev -n <fname> --query properties.customDomainVerificationId -o tsv)

STG=$(az storage account list -g rg-notify-dev --query "[0].name" -o tsv)
terraform init \
  -backend-config="resource_group_name=rg-notify-dev" \
  -backend-config="storage_account_name=${STG}" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=cloudflare.tfstate"

terraform plan
```

OIDC backend auth (`use_oidc = true`) is wired for CI; for local runs
either set `ARM_USE_AZURE_CLI=true` and rely on `az login`, or override
the backend to local state.

## Security note (public-repo discipline)

Nothing in this directory should be edited to contain a real value. Every
input comes from `TF_VAR_*` env vars at runtime — the CI workflow sources
them from a mix of:

- **Bicep outputs** (Function App hostname + customDomainVerificationId)
- **GitHub Actions repo variables** (the domain)
- **GitHub Actions repo secrets** (Cloudflare API token)

If you create a local `terraform.tfvars` for ad-hoc plans, make sure it
stays out of git — it's already in the repo-root `.gitignore`.
