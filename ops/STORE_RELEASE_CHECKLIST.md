# iOS and Android store release checklist

## Shared

- Real HTTPS API hostname, production SQL Server, backup/restore drill, monitoring alerts and on-call owner are verified.
- Production SMTP credentials, separate account-code pepper, verified sending domain, SPF/DKIM/DMARC, bounce alerts, email verification and password recovery are tested without exposing whether an email is registered.
- IAPHUB products `lumina_plus_monthly` and `lumina_plus_yearly`, server API key, webhook URL/token and store credentials are configured.
- TrueLayer production client is approved; return URI is registered; consent, reconnect, Lumina disconnect and bank-side consent revocation journeys pass on physical devices.
- Public `/legal/privacy`, `/legal/terms`, `/legal/support` and `/legal/account-deletion` pages use the independent publisher's final legal name, country, effective date, minimum age and monitored contact addresses, and have legal review.
- Threat model, dependency scan, penetration test and closed beta are complete; critical/high findings are closed.
- Accessibility, reduced motion, screen readers, large text, offline/poor-network behaviour and data export/deletion are tested.

## Apple

- Distribution certificate, App Store provisioning profile, bundle ID and App Store Connect app record belong to the operator.
- For individual or sole-proprietor enrollment, use the publisher's real legal name and confirm that it is acceptable as the public App Store seller name.
- Face ID purpose string, privacy manifest, App Privacy answers and Sign in requirements match actual behaviour.
- Subscription group, localized products, review notes, sandbox purchase/restore/cancel/renewal and account deletion pass on physical iPhone/iPad.
- Archive with the real API and IAPHUB build properties; upload through Xcode/Transporter and test the TestFlight build.

## Google Play

- Confirm the required Play Console account type before release. The app is independently owned, but Google's current policy places money-management/financial-service apps in a category that may require an Organization developer account and associated verification.
- The verified publisher/operator name, public developer name, address, support email/phone/website and payments profile are final and consistent with the privacy policy.
- Operator-owned upload key and Play App Signing are enabled; the keystore and recovery material are backed up in an operator-controlled secrets system outside source control.
- Package name is permanently reserved as `com.luminamoney.personalfinance`; every rollout uses a unique, increasing `versionCode`.
- The AAB targets Android 16 / API 36. Data safety, privacy URL, account-deletion URL, ads declaration, content rating, target audience and financial-features declaration match actual behaviour.
- Base plans/offers, license testers, purchase/restore/cancel/renewal and notification permission pass on representative API 23, 33 and current Android devices.
- Install an internal/closed-track build from Google Play and verify normal flexible update, priority 4/5 immediate update, download completion/restart and an interrupted immediate update resumed from foreground.
- Verify notification states: first contextual permission request, allowed delivery, denied delivery, system-level revocation warning, direct settings recovery, reboot and rescheduling after app data restore.
- Build the signed artifact only with `ops/Publish-GooglePlay.ps1`; retain its version and SHA-256 in the release record. Never upload a locally side-loaded/debug-signed bundle.
- Complete internal testing, any account-specific closed-testing gate, staged production rollout, crash/ANR review and rollback decision before 100% production. New personal accounts are normally subject to Google's current 12-testers-for-14-continuous-days gate; an Organization requirement, if applied to this finance app, supersedes that personal-account path.
