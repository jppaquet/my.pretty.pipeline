# Email Routing layer.
#
# Two "destination" concepts to disambiguate:
#   - The Worker (`email-ingest`) handles inbound email programmatically.
#     It's named here as a string — the script itself is deployed by
#     wrangler from workers/email-ingest/. The bootstrap order matters:
#     deploy the Worker once before applying this stack, otherwise the
#     rule create fails with a 404 on the worker reference.
#   - The verified destination address (a regular mailbox, e.g. Gmail)
#     receives Cloudflare-side forwards. We need it because Google Alerts
#     sends a one-click verification email to alerts@<domain>; the Worker
#     detects that email and forwards it here so the maintainer can click
#     the confirm link from a real inbox. Cloudflare requires destinations
#     to be verified — provisioning the address sends a confirmation email
#     to it, and the maintainer must click before the first forward works.

# Email Routing settings — provider v5 marks `enabled` and `skip_wizard`
# as read-only (the toggle is implicitly on once you have MX records and
# at least one rule; CF has no separate "off" mode). We declare the
# resource so it appears in state and gets a clean import path, but the
# only configurable input is the zone reference.
resource "cloudflare_email_routing_settings" "this" {
  zone_id = local.zone_id
}

# Verified destination — Cloudflare Email Routing forwards land here.
# Account-scoped (verified destinations are shared across all zones in
# the account). First-time provisioning triggers a confirmation email
# the maintainer must click — `cloudflare_email_routing_address` is a
# noop on re-apply once verified.
resource "cloudflare_email_routing_address" "destination" {
  account_id = var.cloudflare_account_id
  email      = var.email_destination_address
}

# Route alerts@<domain> → Worker.
#
# The Worker reads the message, checks DKIM/SPF + the sender allow-list,
# forwards verification emails to the verified destination, and POSTs
# actual alerts to the Ingest API. See workers/email-ingest/src/index.ts.
resource "cloudflare_email_routing_rule" "alerts_to_worker" {
  zone_id = local.zone_id
  name    = "alerts → email-ingest worker"
  enabled = true
  matchers = [{
    type  = "literal"
    field = "to"
    value = local.email_ingest_addr
  }]
  actions = [{
    type  = "worker"
    value = [var.email_ingest_worker_name]
  }]

  depends_on = [cloudflare_email_routing_settings.this]
}

# Catch-all reject. Anything sent to <anything-else>@<domain> bounces
# rather than silently disappearing — prevents accidentally creating new
# attack surface by typo (e.g. `admins@`, `support@`).
resource "cloudflare_email_routing_catch_all" "reject_rest" {
  zone_id = local.zone_id
  name    = "default reject"
  enabled = true
  matchers = [{
    type = "all"
  }]
  actions = [{
    type = "drop"
  }]

  depends_on = [cloudflare_email_routing_settings.this]
}
