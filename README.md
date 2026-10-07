# Lumina Money

Lumina Money is an offline-first personal-finance app for Android and iOS, backed by an ASP.NET Core API and SQL Server. The mobile client is built with .NET MAUI, C# and XAML; finance calculations are shared in a .NET class library.

> **Project status:** Active development. The repository contains working finance, sync and account flows, but a public paid launch still requires production infrastructure, store setup, real-device testing and the operational checks in [`ops/STORE_RELEASE_CHECKLIST.md`](ops/STORE_RELEASE_CHECKLIST.md).

## What’s included

- **Money management:** accounts, balances, transactions, transfers, split expenses, zero-based budgets, rollover, savings goals, recurring bills and subscriptions.
- **Planning and insight:** cash-flow charts, net worth, safe-to-spend, forecasts, spending reports and category progress.
- **Everyday workflows:** CSV import/export, reconciliation, reminders, biometric app lock, reduced-motion preference and profile/security settings.
- **Offline-first storage:** a per-profile SQLite vault, protected sensitive fields and a queued sync flow for authenticated cloud accounts.
- **Cloud services:** ASP.NET Core API, EF Core migrations, SQL Server 2022, account recovery, authenticated finance data, legal endpoints, subscription verification hooks and optional TrueLayer integration.
- **Mobile experience:** dark navy/blue interface, XAML screens, animated charts and transitions, Android in-app update support, and iOS platform scaffolding.

Optional integrations such as email delivery, Play/App Store billing and live Open Banking require operator-owned production credentials and provider approval. Their presence in source code does not mean those external services are configured for this repository.

## Architecture

```text
.NET MAUI client (Android / iOS)
  ├─ XAML interface and shared finance calculations
  ├─ local SQLite vault, protected fields and pending sync queue
  └─ HTTPS API client
             │
ASP.NET Core API
  ├─ authentication, account recovery and finance endpoints
  ├─ EF Core migrations and user-scoped data access
  └─ optional email, store and Open Banking providers
             │
SQL Server 2022+
```

SQL Server credentials belong only in the API's environment or secret store. Never put them in the mobile app or commit them to Git.

## Repository layout

| Path | Purpose |
| --- | --- |
| `LuminaMoney.App/` | .NET MAUI app, XAML pages, controls and platform integrations |
| `LuminaMoney.Api/` | ASP.NET Core API, EF Core context and migrations |
| `LuminaMoney.Core/` | Shared models and finance engine |
| `LuminaMoney.Tests/` | API, finance, bank-import and provider tests |
| `ops/` | Production runbook, backup script and store checklist |
| `.github/workflows/ci.yml` | Test and Android compile checks |

## Requirements

- .NET 10 SDK
- SQL Server 2022 or a compatible SQL Server instance
- For Android: MAUI Android workload and an Android SDK/emulator
- For iOS: macOS, Xcode and Apple signing assets (required to archive or publish)

Install the Android workload and restore packages:

```powershell
dotnet workload install maui-android
dotnet restore LuminaMoney.slnx
```

## Run locally

1. Create a local SQL Server database named `LuminaMoney` (or adjust the connection string below).
2. Create an API user-secrets store and set local-only configuration. Keep the generated keys private and stable for your development database:

```powershell
dotnet user-secrets init --project LuminaMoney.Api
dotnet user-secrets set "ConnectionStrings:FinanceDatabase" "Server=localhost;Database=LuminaMoney;Trusted_Connection=True;TrustServerCertificate=True" --project LuminaMoney.Api
$jwt = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$pepper = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:Key" $jwt --project LuminaMoney.Api
dotnet user-secrets set "Identity:AccountCodePepper" $pepper --project LuminaMoney.Api
```

3. Install the EF Core command-line tool if needed, apply the schema and start the API:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.1
dotnet ef database update --project LuminaMoney.Api --startup-project LuminaMoney.Api
dotnet run --project LuminaMoney.Api --launch-profile https
```

4. In another terminal, build and launch the Android app from Visual Studio or with:

```powershell
dotnet build LuminaMoney.App -f net10.0-android -c Debug
```

The Debug client uses `https://10.0.2.2:7252/api/v1/` from the Android emulator and `https://localhost:7252/api/v1/` on desktop. A physical device needs a reachable development API URL and suitable network/firewall configuration. Debug certificate exceptions are restricted to development hosts and are not part of Release builds.

## Configuration and deployment

Machine-specific `appsettings*.json` files are intentionally ignored. Configure the API using environment variables, .NET user-secrets locally, or the deployment secret manager. See [`.env.production.example`](.env.production.example), [`compose.production.yml`](compose.production.yml) and [`ops/PRODUCTION_RUNBOOK.md`](ops/PRODUCTION_RUNBOOK.md) for the production configuration and operations guidance.

Release builds intentionally fail unless production API, billing and signing settings are supplied. For Google Play, use the publisher-owned upload key and the release script in [`ops/Publish-GooglePlay.ps1`](ops/Publish-GooglePlay.ps1). Do not commit signing keys, passwords, service credentials, local databases or generated APKs/AABs.

iOS Release archiving requires a Mac with Xcode plus an Apple Developer account, signing certificate and provisioning profile. Store listing, privacy disclosures, testing and approval happen in the relevant store consoles; this repository does not publish the app for you.

## Verification

```powershell
dotnet test LuminaMoney.Tests/LuminaMoney.Tests.csproj -c Release
dotnet build LuminaMoney.Api/LuminaMoney.Api.csproj -c Release
dotnet build LuminaMoney.App/LuminaMoney.App.csproj -f net10.0-android -c Debug
```

The current test suite has **43 passing tests**. CI runs the test suite and compiles the Android target. Hardware performance, store billing, production email, live Open Banking and store update flows still need validation with real devices and configured provider accounts.

## License

No license file is included yet. Until the owner chooses and adds a license, public visibility does not grant permission to reuse or redistribute the code.
