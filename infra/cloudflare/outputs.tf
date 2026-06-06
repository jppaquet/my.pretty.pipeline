output "zone_id" {
  description = "Cloudflare zone ID — handy when running ad-hoc `wrangler` commands that need it."
  value       = local.zone_id
}

output "function_app_fqdn" {
  description = "Custom-domain FQDN bound to the Function App."
  value       = local.function_app_fqdn
}
