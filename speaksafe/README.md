# SpeakSafe

Anonymous sexual harassment reporting for Nigeria. Survivors and witnesses report what happened without giving their
name, get a private case code and PIN, and can connect with human rights, legal aid, counselling and medical
organisations through private conversations.

## What it does

| Who | Signs in with | Can |
|---|---|---|
| Reporter, no account | nothing (gets a case code + PIN) | Report, attach evidence, open the report later by code + PIN, chat with the organisation it was sent to, share it later, close it |
| Survivor account | nickname + passphrase (no name, no phone) | Everything above for all their reports, plus start conversations with any organisation; recovery code for a lost passphrase |
| Case officer | username + passphrase | See reports sent to their organisation, read narrative and evidence, message the reporter, mark in progress or close |
| Coordinator | PIN from appsettings | Totals by type, status, place and state; add and edit organisations; create and disable officers. **Never** sees narratives, messages or who reported |

Privacy and safety:

- Narratives, message bodies and evidence files are encrypted at rest with ASP.NET Data Protection (keys in
  `App_Data\keys`, protected with DPAPI on Windows). Evidence files are stored under random names in `App_Data\evidence`.
- Passphrases, PINs and recovery codes are PBKDF2-hashed. Case codes avoid look-alike characters (0/O, 1/I/L).
- Reports can be fully anonymous. Sharing a nickname needs an account and only the chosen organisation sees it.
- An officer only sees cases and conversations for their own organisation. Paused organisations can't sign in.
- No IP addresses are stored. Rate limits (sign-in, reports, chat) are in memory only.
- Strict CSP, `no-referrer`, no third-party requests (fonts are self-hosted), `no-store` on API responses.
- Quick exit button and Esc twice: swaps the page for a recipe, signs out and leaves for Google.
- Reports involving assault are flagged urgent and the reporter is shown 72-hour care advice and real helplines.

The six partner organisations in the seed are fictional and marked DEMO. The Resources section lists real Nigerian
services (112, WARIF 0800 9210 0009, Lagos DSVA 08000 333 333 / *6820#, NHRC 0800 647 2428, Mirabel Centre).

## Layout

```
src/SpeakSafe.Core    domain, services (accounts, reports, chat, orgs), memory store, seed. No packages.
src/SpeakSafe.Web     minimal API, cookie auth, SQL Server store (ADO.NET), schema at startup, wwwroot UI
tests/SpeakSafe.Tests dependency-free console test runner
deploy/deploy-dev.ps1 publish + IIS deploy for the dev PC
```

## Run it

Requirements: .NET 10 SDK. SQL Server (Express is fine) for the normal store.

```powershell
# Quick look, no database (data resets on restart)
$env:Store = 'Memory'; dotnet run --project src/SpeakSafe.Web

# Normal: SQL Server. Edit ConnectionStrings:SpeakSafe in src/SpeakSafe.Web/appsettings.json first.
dotnet run --project src/SpeakSafe.Web
```

Open http://localhost:5090. Development settings make the coordinator PIN `0000`. While `Demo:ShowLogins` is true
the sign-in page shows one-click demo sign-ins:

| Role | Sign in |
|---|---|
| Survivor | `BraveSun21` / `demo1234` |
| Case code | `SS-7Q2M` PIN `4821` (account holder's report), `SS-3HVK` PIN `1357` (anonymous, waiting for Lagos Rights Desk) |
| Officers | `kemi.csn`, `amaka.lrd`, `tobi.hcc`, `hauwa.nwj`, `chidi.wdc`, `ngozi.cpm` / `demo1234` |

Use two browsers (or a private window) to be the reporter and the case officer at the same time.

### Tests

```powershell
dotnet run --project tests/SpeakSafe.Tests      # 25 tests, exit code 1 on failure
dotnet build -c Release                         # baseline: 0 warnings, 0 errors
```

## Deploy to the dev PC (IIS)

Needs the ASP.NET Core 10 Hosting Bundle on the PC (run `iisreset` after installing it).

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\deploy-dev.ps1
```

The first run creates the `SpeakSafe` app pool and site on port **8086**, a firewall rule, a SQL login for
`IIS AppPool\SpeakSafe` (with `dbcreator`, so the app can create `SpeakSafeDb`), and `appsettings.Production.json`
with a random coordinator PIN (printed once). Later runs only replace the binaries. `appsettings.Production.json`
and `App_Data` are never overwritten.

**Back up `App_Data` with the database.** It holds the encryption keys; without them encrypted narratives,
messages and evidence can't be read. The keys are DPAPI-protected for the app pool identity, so keep the same
app pool identity when moving servers (or re-key before moving).

If the coordinator PIN was missed:
```powershell
(Get-Content C:\MUSBAUDEEN\Deploy\SpeakSafe\appsettings.Production.json | ConvertFrom-Json).Admin.Pin
```

### Database

On startup the app creates the database if it can, applies the schema (one batch per object, idempotent), and
seeds the demo organisations if `Organisations` is empty (`Seed:Enabled`). If the SQL login step was skipped:

```sql
CREATE LOGIN [IIS AppPool\SpeakSafe] FROM WINDOWS;
ALTER SERVER ROLE dbcreator ADD MEMBER [IIS AppPool\SpeakSafe];
```

### Before real use

In `appsettings.Production.json`:
- `"Demo": { "ShowLogins": false }` and `"Seed": { "Enabled": false }` on a fresh database (or pause the demo
  organisations from the coordinator screen).
- Serve over HTTPS and set `"ForceHttps": true`.
- Add real partner organisations and their officers from the coordinator screen.

## API (all under `/api/v1`, envelope `{ ok, data }` or `{ ok: false, error }`)

| Method | Path | Who |
|---|---|---|
| GET | `/meta`, `/orgs?service=&state=` | anyone |
| POST | `/auth/register`, `/auth/signin`, `/auth/recover`, `/auth/track`, `/auth/officer`, `/auth/admin`, `/auth/logout` | anyone |
| GET | `/me` | anyone |
| POST | `/reports` | anyone (an account-less reporter becomes the tracker of the new report) |
| GET | `/reports/mine` | survivor |
| GET | `/reports/{code}` | owner, tracker, officer of the receiving org |
| POST | `/reports/{code}/share`, `/reports/{code}/close`, `/reports/{code}/evidence` (multipart `file`) | owner, tracker |
| GET | `/evidence/{id}` | owner, tracker, officer of the receiving org |
| GET/POST | `/conversations`, `/conversations/{id}/messages?after=` | survivor, tracker, officer |
| GET | `/org/cases`; POST `/org/cases/{code}/status` | officer |
| GET | `/admin/overview`, `/admin/orgs/{id}/officers`; POST `/admin/orgs`, `/admin/orgs/{id}/officers`, `/admin/officers/{id}/active` | coordinator |
| GET | `/health` | anyone |

## Next rounds

- ss002: verify SQL Server on the dev PC; officer passphrase change; coordinator audit log (who changed which org).
- ss003: optional SMS/WhatsApp notification to officers when a new case arrives; USSD reporting short code.
- ss004: Yorùbá, Hausa, Igbo and Pidgin translations of the reporting flow.
- Later: retention policy (auto-delete closed reports after N months, survivor-initiated delete).
