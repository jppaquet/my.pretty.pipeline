# DNS records:
#   1. CNAME `func.<domain>` → Azure Function App default hostname.
#      `proxied=false` (DNS-only / gray cloud) is required during App
#      Service Managed Certificate issuance, since Azure validates control
#      of the hostname by reaching the actual origin. Flip to proxied later
#      via the dashboard if WAF/DDoS is desired (no record changes needed).
#   2. TXT `asuid.func.<domain>` carrying `customDomainVerificationId` —
#      Azure refuses to bind the hostname until this record resolves.
#
# MX + SPF for Cloudflare Email Routing are intentionally NOT here.
# When the maintainer clicks "Enable Email Routing" in the dashboard
# (one-shot, see FORK-SETUP §11b), CF auto-creates and auto-manages the
# 3 MX records (route1/2/3.mx.cloudflare.net) + the SPF TXT — and rejects
# any TF-side create of identical records with 400 "this zone is managed
# by Email Routing." Treat them as CF-managed; we don't try to dual-write.

resource "cloudflare_dns_record" "func_cname" {
  zone_id = local.zone_id
  name    = local.function_app_fqdn
  type    = "CNAME"
  content = var.function_app_hostname
  ttl     = 1 # 1 = automatic
  proxied = false
  comment = "Function App custom domain (managed-cert issuance requires DNS-only)"
}

resource "cloudflare_dns_record" "func_asuid_txt" {
  zone_id = local.zone_id
  name    = "asuid.${local.function_app_fqdn}"
  type    = "TXT"
  content = "\"${var.function_app_custom_domain_verification_id}\""
  ttl     = 1
  proxied = false
  comment = "Azure App Service hostname-ownership challenge"
}
