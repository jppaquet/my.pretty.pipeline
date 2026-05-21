# DNS records:
#   1. CNAME `func.<domain>` → Azure Function App default hostname.
#      `proxied=false` (DNS-only / gray cloud) is required during App
#      Service Managed Certificate issuance, since Azure validates control
#      of the hostname by reaching the actual origin. Flip to proxied later
#      via the dashboard if WAF/DDoS is desired (no record changes needed).
#   2. TXT `asuid.func.<domain>` carrying `customDomainVerificationId` —
#      Azure refuses to bind the hostname until this record resolves.
#   3. MX records for Cloudflare Email Routing — the three CF inbound
#      MX hosts plus the SPF TXT, exactly what the CF dashboard adds when
#      you click "Enable Email Routing." We add them as code so they stay
#      diff-able.

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

# Cloudflare Email Routing inbound MX + SPF. Priorities + hostnames are the
# CF-managed defaults; same shape the dashboard produces.
resource "cloudflare_dns_record" "email_mx_route1" {
  zone_id  = local.zone_id
  name     = var.domain
  type     = "MX"
  content  = "route1.mx.cloudflare.net"
  priority = 10
  ttl      = 1
  proxied  = false
  comment  = "CF Email Routing"
}

resource "cloudflare_dns_record" "email_mx_route2" {
  zone_id  = local.zone_id
  name     = var.domain
  type     = "MX"
  content  = "route2.mx.cloudflare.net"
  priority = 20
  ttl      = 1
  proxied  = false
  comment  = "CF Email Routing"
}

resource "cloudflare_dns_record" "email_mx_route3" {
  zone_id  = local.zone_id
  name     = var.domain
  type     = "MX"
  content  = "route3.mx.cloudflare.net"
  priority = 30
  ttl      = 1
  proxied  = false
  comment  = "CF Email Routing"
}

resource "cloudflare_dns_record" "email_spf" {
  zone_id = local.zone_id
  name    = var.domain
  type    = "TXT"
  content = "\"v=spf1 include:_spf.mx.cloudflare.net ~all\""
  ttl     = 1
  proxied = false
  comment = "CF Email Routing SPF"
}
