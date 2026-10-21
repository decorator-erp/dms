# Local development: secrets

`src/Api/appsettings.json` commits no secrets. `JwtSettings:Key` and `DMS_SEED_ADMIN_PASSWORD` are present
but empty, and `Architecture.Tests` (`CommittedSecretsTests`) fails if either gets a value in any
`src/Api/appsettings*.json`. Supply both yourself.

## Development: user-secrets

```
dotnet user-secrets --project src/Api set "JwtSettings:Key" "<32+ random chars>"
dotnet user-secrets --project src/Api set "DMS_SEED_ADMIN_PASSWORD" "<a password meeting the policy>"
```

- **`JwtSettings:Key`**: at least 32 characters (`JwtOptions.Key` is `[MinLength(32)]`, validated at
  start-up). Without it the API refuses to start: `JwtSettings:Key is not configured`.
- **`DMS_SEED_ADMIN_PASSWORD`**: the password the seeded admin (`admin@system.dms`) is created with. It must
  meet the Identity password policy: at least 8 characters with an upper-case letter, a lower-case letter,
  a digit and a symbol.
- **Without the password secret, Development starts but creates no admin, and you can't sign in.** This
  is silent, not an error: `AdminUserSeeder` skips the admin in Development when the password is unset.
  Set the secret, then restart the API.
- The password is read only when the admin is first created. Once the admin exists, changing the secret
  doesn't change the admin's password.

`docker-compose.override.yml` mounts your user-secrets folder into the `api` container, so the same
secrets apply there.

## Production

Supply both as environment variables:

| Variable | Notes |
|---|---|
| `JwtSettings__Key` | Required on every start. At least 32 characters. |
| `DMS_SEED_ADMIN_PASSWORD` | Required whenever the database has no seeded admin yet; outside Development the API refuses to start without it. Once the admin exists it is no longer read. |

Never put either value in a committed file, including `appsettings.Development.json`.
