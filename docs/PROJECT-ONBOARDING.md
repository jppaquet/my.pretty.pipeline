# Onboarding a new producing project

This doc is for **adding a producer** (a script, cron job, CI pipeline that
publishes notifications). If you're standing up the whole stack from a
fresh fork, start with [FORK-SETUP.md](FORK-SETUP.md) first — that walks
the Apple Developer + Azure + GitHub steps; this doc takes over once your
backend is live.

## Preferred path: the admin app

Once you've enabled the admin plane (FORK-SETUP.md §7) and the SWA is
deployed (PR-2), mint producer keys from the browser:

1. Open `https://<your-swa>.azurestaticapps.net`, sign in, MFA.
2. Switch to the **projects** tab → **mint new project**.
3. Enter the project id (charset `[A-Za-z0-9._-]`, ≤64 chars) and a
   display name (≤128 chars). Click **mint**.
4. The `npk_…` cleartext key is shown **exactly once** with a copy button
   and a "I have stored this key" confirm. Paste it into your secret
   store *now* — losing it means re-minting the project.
5. Hand the key to the producer; it goes into `NOTIFY_KEY` env var on
   that producer's deploy. Combine with `NOTIFY_URL` (the Function App
   hostname) and follow the curl recipe in [SCHEMA.md](SCHEMA.md).

The key never exists server-side in cleartext — the Function App only
holds an argon2id hash + per-key random salt, with a Key-Vault-stored
pepper that makes a stolen Cosmos snapshot useless on its own.

## Fallback: mint by hand (no admin app yet)

Skip this section if you can use the SPA above. Otherwise, mint a key
directly against Cosmos via the bash recipe below. **Caveat**: the bash
flow uses `argon2` CLI parameters that produce a hash *incompatible* with
how `ApiKeyHasher` (the C# verifier in the Function App) computes them —
the canonical path now is the admin app. The recipe is kept here only
for forks that haven't yet stood up the admin SPA.

```sh
RG=rg-notify-dev
KV=$(az keyvault list -g "$RG" --query "[0].name" -o tsv)
COSMOS=$(az cosmosdb list -g "$RG" --query "[0].name" -o tsv)

# 1. Generate a 32-byte secret, base32-encode, prefix with `npk_`.
KEY=npk_$(openssl rand 32 | base32 | tr -d '=' | tr '[:upper:]' '[:lower:]')

# 2. Hash it. (Mismatches ApiKeyHasher's salt/pepper split — see caveat.)
HASH=$(echo -n "$KEY" | argon2 "$(az keyvault secret show --vault-name "$KV" --name api-key-pepper --query value -o tsv)" -id -e)

# 3. Insert a project document.
az cosmosdb sql container item create \
  -a "$COSMOS" -g "$RG" -d notify -c projects \
  -p '{ "id":"<project-id>", "projectId":"<project-id>", "displayName":"<name>", "keyHash":"'"$HASH"'", "active":true }'

# 4. Hand `$KEY` to the project.
echo "$KEY"
```

> Resource names are globally-unique-salted (`kv-notify-dev-XXXXXX`, `cosmos-notify-dev-XXXXXX`)
> so the snippet looks them up by RG instead of hardcoding.
