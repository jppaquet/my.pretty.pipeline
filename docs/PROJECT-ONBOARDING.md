# Onboarding a new producing project

Phase 1+ ships an admin endpoint for this. Until then, mint a key by hand:

```sh
RG=rg-notify-dev
KV=$(az keyvault list -g "$RG" --query "[0].name" -o tsv)
COSMOS=$(az cosmosdb list -g "$RG" --query "[0].name" -o tsv)

# 1. Generate a 32-byte secret, base32-encode, prefix with `npk_`.
KEY=npk_$(openssl rand 32 | base32 | tr -d '=' | tr '[:upper:]' '[:lower:]')

# 2. Hash it (Phase 1 ships a CLI for this; for now use the inline argon2 call).
HASH=$(echo -n "$KEY" | argon2 "$(az keyvault secret show --vault-name "$KV" --name api-key-pepper --query value -o tsv)" -id -e)

# 3. Insert a project document.
az cosmosdb sql container item create \
  -a "$COSMOS" -g "$RG" -d notify -c projects \
  -p '{ "id":"<project-id>", "projectId":"<project-id>", "displayName":"<name>", "keyHash":"'"$HASH"'", "active":true }'

# 4. Hand `$KEY` to the project. Document it in your secrets manager.
echo "$KEY"
```

> Resource names are globally-unique-salted (`kv-notify-dev-XXXXXX`, `cosmos-notify-dev-XXXXXX`)
> so the snippet looks them up by RG instead of hardcoding.

Then in the producing project's code, paste the curl recipe from `SCHEMA.md` and
plumb `NOTIFY_URL` + `NOTIFY_KEY` from environment variables.
