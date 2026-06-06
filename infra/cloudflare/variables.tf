# Inputs to the Cloudflare layer.
#
# This file declares variable *shapes*, never values. Every input flows
# in at runtime through `TF_VAR_<name>` env vars set by cd-cloudflare.yml:
#   - Bicep outputs       → function_app_hostname, function_app_custom_domain_verification_id
#   - GitHub repo vars    → domain
#   - GitHub repo secrets → cloudflare_api_token
#
# Email Routing (alerts@<domain> → email-ingest Worker) moved to
# my.pretty.blender; this stack now only manages the func.<domain> DNS
# records. The email-routing variables that used to live here went with it.
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
