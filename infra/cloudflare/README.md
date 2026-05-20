# `infra/cloudflare/` — Terraform layer for Cloudflare

Manages everything Cloudflare-side: DNS records for the Function App's
custom domain, MX/SPF for inbound email, Email Routing settings, the
verified destination address (Gmail forwards), and the routing rule that
binds `alerts@prettynotifier.com` to the `email-ingest` Worker.

The Worker **script** itself is *not* TF-managed — it's deployed by
wrangler from `workers/email-ingest/`, and TF references it by name. The
bootstrap order is therefore:

1. wrangler deploys the Worker (`cd-worker.yml`)
2. then TF applies the Email Routing rule (`cd-cloudflare.yml`)

`cd-cloudflare.yml` waits on `cd-worker` via `needs:` to keep that order
on first deploy. After bootstrap each side drifts independently when its
paths change.

## Running locally

```sh
export TF_VAR_domain=<your-domain>                                    # GH var
export TF_VAR_cloudflare_account_id=<account-id>                      # GH secret
export TF_VAR_cloudflare_api_token=<api-token>                        # GH secret
export TF_VAR_function_app_hostname=$(az functionapp show -g rg-notify-dev -n <fname> --query defaultHostName -o tsv)
export TF_VAR_function_app_custom_domain_verification_id=$(az functionapp show -g rg-notify-dev -n <fname> --query properties.customDomainVerificationId -o tsv)
export TF_VAR_email_destination_address=<your-verified-destination>   # GH var (public-ish, but treat as moderately sensitive)

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
- **GitHub Actions repo variables** (the domain, the destination address)
- **GitHub Actions repo secrets** (Cloudflare API token + Account ID)

If you create a local `terraform.tfvars` for ad-hoc plans, make sure it
stays out of git — it's already in the repo-root `.gitignore`.
