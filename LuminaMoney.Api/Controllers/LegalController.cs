using System.Net;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuminaMoney.Api.Controllers;

[ApiController, AllowAnonymous, Route("legal")]
public sealed class LegalController(IConfiguration configuration) : ControllerBase
{
    private string Operator => Safe(Configured("Legal:OperatorName", "Lumina Money independent publisher"));
    private string Support => Safe(Configured("Legal:SupportEmail", "support@luminamoney.app"));
    private string PrivacyContact => Safe(Configured("Legal:PrivacyContactEmail", Configured("Legal:SupportEmail", "privacy@luminamoney.app")));
    private string OperatorCountry => Safe(Configured("Legal:OperatorCountry", "United Kingdom"));
    private int MinimumAge => Math.Clamp(configuration.GetValue("Legal:MinimumAge", 18), 13, 18);
    private string EffectiveDate
    {
        get
        {
            var raw = Configured("Legal:EffectiveDate", "2026-10-07");
            return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                : Safe(raw);
        }
    }

    [HttpGet("privacy")]
    public ContentResult Privacy() => Html("Privacy policy", $"""
        <h1>Privacy policy</h1><p class="meta">Effective {EffectiveDate} · {Operator}</p>
        <h2>Who controls your data</h2><p>{Operator}, an independent app publisher based in {OperatorCountry}, is the controller of the personal data Lumina Money processes. Contact <a href="mailto:{PrivacyContact}">{PrivacyContact}</a> for privacy requests.</p>
        <h2>What Lumina processes</h2><p>We process your name and email address; the accounts, transactions, categories, budgets, goals and bills you enter or import; device and sync metadata; subscription entitlement status; support messages; and Open Banking connection identifiers. When you connect a bank, transaction and account information comes from the bank through TrueLayer with your consent. Store entitlement data comes from Apple, Google and IAPHUB. We do not ask for or store your online-banking password.</p>
        <h2>Why we process it</h2><p>We use this data to provide budgeting, forecasting, synchronisation, account security, customer support, fraud and abuse prevention, subscription access, and consent-based bank connections. The principal lawful basis is performance of our contract with you. We also process limited security and service-integrity data for legitimate interests, meet legal obligations where applicable, and rely on consent where the law requires it.</p>
        <h2>Storage and security</h2><p>Cloud records are stored in Microsoft SQL Server. Sensitive local text and queued sync payloads are protected with AES-256-GCM; the device key is kept in Keychain or Keystore. Transport uses HTTPS. Access tokens expire and refresh tokens rotate with replay detection.</p>
        <h2>Service providers and international transfers</h2><p>Depending on enabled features, we use hosting, email and monitoring providers, Apple App Store or Google Play, IAPHUB for purchase validation, and TrueLayer for UK Open Banking. A provider may process data outside your country. Where required, we use an adequacy decision or approved contractual safeguards and you may request information about those safeguards from the privacy contact.</p>
        <h2>No sale or behavioural advertising</h2><p>Lumina does not sell your personal or financial data and does not use it to serve third-party behavioural advertising.</p>
        <h2>Your choices and rights</h2><p>You can export your account data and permanently delete your account in Profile &amp; security. You can withdraw Open Banking consent by disconnecting linked banks and through your bank. Depending on applicable law, you may ask to access, correct, erase, restrict, object to, or receive your data, and withdraw consent without affecting earlier lawful processing. You may complain to your local data-protection authority; UK users may contact the Information Commissioner’s Office.</p>
        <h2>Retention</h2><p>Active account data is kept while you use Lumina. Account deletion removes identity and owned finance data from the live service. Encrypted backups expire under the published backup-retention schedule unless law requires longer retention.</p>
        <h2>Forecasts and automated processing</h2><p>Lumina automatically calculates budgets, forecasts and financial-health indicators from the records available to it. These are informational tools and are not used to make decisions that produce legal or similarly significant effects about you.</p>
        <h2>Age</h2><p>Lumina is intended for people aged {MinimumAge} or older. We do not knowingly offer accounts to younger children. Contact the privacy address if you believe a child has created an account contrary to this requirement.</p>
        <h2>Policy changes</h2><p>We may update this notice when features, providers or laws change. Material changes will be brought to users’ attention in the app before they take effect where required.</p>
        <h2>Contact</h2><p>Privacy enquiries: <a href="mailto:{PrivacyContact}">{PrivacyContact}</a>. Support: <a href="mailto:{Support}">{Support}</a>.</p>
        """);

    [HttpGet("terms")]
    public ContentResult Terms() => Html("Terms of service", $"""
        <h1>Terms of service</h1><p class="meta">Effective {EffectiveDate} · offered by {Operator}</p>
        <h2>Eligibility and your account</h2><p>You must be at least {MinimumAge}, provide accurate registration information, protect your credentials, and use the service lawfully. Each account is personal to its registered user.</p>
        <h2>The service</h2><p>Lumina provides personal budgeting, planning, reporting and account-aggregation tools to the public. It is independently published and is not a bank, lender, investment platform or credit-reference agency.</p>
        <h2>Not financial advice</h2><p>Lumina provides informational calculations and planning tools, not regulated financial, investment, tax, credit or legal advice. Forecasts can be wrong or incomplete. Check important decisions with a qualified professional.</p>
        <h2>Subscriptions</h2><p>Paid plans are billed by Apple App Store or Google Play at the price and renewal terms shown before purchase. Manage or cancel in your store account. Statutory rights and store refund rules continue to apply.</p>
        <h2>Open Banking</h2><p>Bank access requires your explicit consent through TrueLayer and may need periodic reconfirmation. Availability, freshness and coverage depend on your bank and the provider. You remain responsible for checking transactions against official bank records.</p>
        <h2>Availability and acceptable use</h2><p>Do not attack, reverse engineer, overload, automate abusive access to, or use Lumina for unlawful activity. We may suspend access to protect users or comply with law. We work to provide a reliable service but cannot promise uninterrupted availability.</p>
        <h2>Your content and responsibility</h2><p>You retain ownership of the financial records you enter. You give Lumina permission to process them only as needed to operate the service. You are responsible for checking imported data, backups, tax records and decisions made using the app.</p>
        <h2>Changes and termination</h2><p>Features and these terms may change as the service develops. Material changes will be communicated where required. You may stop using Lumina at any time; we may restrict an account for abuse, security risk or legal compliance.</p>
        <h2>Closing your account</h2><p>You may export and delete your account inside the app. Deletion is permanent. Details are available on the <a href="/legal/account-deletion">account deletion page</a>.</p>
        <h2>Law and statutory rights</h2><p>These terms do not exclude rights that cannot lawfully be excluded. Applicable consumer and data-protection rights in your country continue to apply.</p>
        <h2>Contact</h2><p>Questions: <a href="mailto:{Support}">{Support}</a>. The operator is based in {OperatorCountry}.</p>
        """);

    [HttpGet("support")]
    public ContentResult SupportPage() => Html("Support", $"""
        <h1>Lumina Money support</h1><p class="meta">Public support for every Lumina user</p>
        <h2>Contact</h2><p>Email <a href="mailto:{Support}">{Support}</a>. Include the app version, device model and a description of the problem. Never send your password, recovery code, full bank credentials or store password.</p>
        <h2>Account access</h2><p>Use <strong>Forgot password?</strong> on the sign-in screen. Verification and reset codes expire and can be used only once. Support will never ask for the code.</p>
        <h2>Billing</h2><p>Purchases, cancellation and refund requests are handled through the Apple App Store or Google Play account used to subscribe. Use Restore purchases inside Lumina after reinstalling or changing devices.</p>
        <h2>Bank connections</h2><p>Open Profile &amp; security to disconnect Lumina. You can also revoke consent in your bank’s security settings. Lumina never asks for your online-banking password.</p>
        <h2>Privacy and deletion</h2><p>Read the <a href="/legal/privacy">privacy policy</a>, export your data in the app, or follow the <a href="/legal/account-deletion">account deletion instructions</a>.</p>
        """);

    [HttpGet("account-deletion")]
    public ContentResult AccountDeletion() => Html("Account deletion", $"""
        <h1>Delete your Lumina account</h1><p class="meta">A direct, in-app deletion process</p>
        <h2>From iOS or Android</h2><ol><li>Open Lumina Money and sign in.</li><li>Open Profile &amp; security.</li><li>Export your data first if needed.</li><li>Enter your current password, type <strong>DELETE</strong>, and choose Permanently delete account.</li></ol>
        <h2>What is deleted</h2><p>Your identity, accounts, categories, transactions, splits, budgets, goals, bills, refresh sessions, subscription entitlement record, and bank-connection metadata are removed from the live SQL Server database. The app also removes that profile’s local device vault.</p>
        <h2>Backups</h2><p>Encrypted operational backups age out under the backup-retention schedule and are not restored for normal product use after deletion. Limited records may be retained only where law, fraud prevention, or dispute handling requires it.</p>
        <h2>Need help?</h2><p>If you cannot access the app, email <a href="mailto:{Support}">{Support}</a> from your registered address. We will verify identity before acting.</p>
        """);

    private string Configured(string key, string fallback) => string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!;
    private static string Safe(string value) => WebUtility.HtmlEncode(value);
    private static ContentResult Html(string title, string body) => new()
    {
        ContentType = "text/html; charset=utf-8",
        StatusCode = StatusCodes.Status200OK,
        Content = $$"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{title}} · Lumina Money</title><style>body{margin:0;background:#f4f7f8;color:#10242c;font:16px/1.65 system-ui,-apple-system,sans-serif}main{max-width:760px;margin:auto;padding:48px 22px 80px}.brand{color:#087b5b;font-weight:800;letter-spacing:.12em;font-size:12px}h1{font-size:42px;line-height:1.08;margin:20px 0 8px}h2{font-size:20px;margin:34px 0 8px}p,li{color:#49616a}.meta{color:#82939a}a{color:#174ea6}.card{background:white;border:1px solid #dde6e9;border-radius:26px;padding:28px;box-shadow:0 12px 36px #526b7514}</style></head><body><main><div class="brand">LUMINA MONEY</div><div class="card">{{body}}</div></main></body></html>"""
    };
}
