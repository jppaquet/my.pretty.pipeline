# Onboarding a new producing project

Phase 1+ ships an admin endpoint for this. Until then, mint a key by hand:

```sh
# 1. Generate a 32-byte secret, base32-encode, prefix with `npk_`.
KEY=npk_$(openssl rand 32 | base32 | tr -d '=' | tr '[:upper:]' '[:lower:]')

# 2. Hash it (Phase 1 ships a CLI for this; for now use the inline argon2 call).
HASH=$(echo -n "$KEY" | argon2 "$(az keyvault secret show --vault-name kv-notify-dev --name api-key-pepper --query value -o tsv)" -id -e)

# 3. Insert a project document.
az cosmosdb sql container item create \
  -a cosmos-notify-dev -g rg-notify-dev -d notify -c projects \
  -p '{ "id":"<project-id>", "projectId":"<project-id>", "displayName":"<name>", "keyHash":"'"$HASH"'", "active":true }'

# 4. Hand `$KEY` to the project. Document it in your secrets manager.
echo "$KEY"
```

Then in the producing project's code, paste the curl recipe from `SCHEMA.md` and
plumb `NOTIFY_URL` + `NOTIFY_KEY` from environment variables.
