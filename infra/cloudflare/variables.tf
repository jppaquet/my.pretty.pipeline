# Inputs to the Cloudflare layer.
#
# This file declares variable *shapes*, never values. Every input flows
# in at runtime through `TF_VAR_<name>` env vars set by cd-cloudflare.yml:
#   - Bicep outputs       → function_app_hostname, function_app_custom_domain_verification_id
#   - GitHub repo vars    → domain, email_destination_address
#   - GitHub repo secrets → cloudflare_account_id, cloudflare_api_token
#
# Repo is public — do NOT add `default = "<real-value>"` to anything here,
# and do NOT commit a `terraform.tfvars` with real values (it's gitignored,
# but stay vigilant). The point of this layout is that a fork clones the
# repo and supplies their own values via the same env-var path with zero
# edits to source.

variable "domain" {
  description = "Apex domain hosted on Cloudflare (e.g. prettynotifier.com)."
  type        = string
}

variable "cloudflare_account_id" {
  description = "Cloudflare Account ID — used for account-scoped resources (Workers, Email Routing destination addresses)."
  type        = string
  sensitive   = true
}

variable "cloudflare_api_token" {
  description = "Cloudflare API token with the permissions listed in docs/FORK-SETUP.md §9."
  type        = string
  sensitive   = true
}

variable "function_app_hostname" {
  description = "Azure Function App default hostname (e.g. func-notify-dev-nrajdy.azurewebsites.net). The `func` CNAME points here."
  type        = string
}

variable "function_app_custom_domain_verification_id" {
  description = "Function App `customDomainVerificationId` — value of the asuid TXT record Azure requires before binding the hostname."
  type        = string
  sensitive   = true
}

variable "function_app_custom_hostname" {
  description = "Subdomain on the apex that proxies to the Function App (e.g. func)."
  type        = string
  default     = "func"
}

variable "email_ingest_local_part" {
  description = "Local-part of the email address Google Alerts delivers to (e.g. `alerts` → alerts@<domain>)."
  type        = string
  default     = "alerts"
}

variable "email_destination_address" {
  description = "Verified Cloudflare destination address — Google Alerts verification emails get forwarded here so the maintainer can click the confirm link."
  type        = string
}

variable "email_ingest_worker_name" {
  description = "Name of the Cloudflare Worker (wrangler-deployed) that handles inbound email. Must match `name` in workers/email-ingest/wrangler.toml."
  type        = string
  default     = "email-ingest"
}
