# Provider config + zone lookup. The zone itself isn't TF-managed because
# Cloudflare Registrar auto-creates it on domain purchase; we only manage
# records inside it.

provider "cloudflare" {
  api_token = var.cloudflare_api_token
}

# Look up the zone created by CF Registrar when var.domain was purchased.
data "cloudflare_zone" "primary" {
  filter = {
    name = var.domain
  }
}

locals {
  zone_id           = data.cloudflare_zone.primary.zone_id
  function_app_fqdn = "${var.function_app_custom_hostname}.${var.domain}"
  email_ingest_addr = "${var.email_ingest_local_part}@${var.domain}"
}
