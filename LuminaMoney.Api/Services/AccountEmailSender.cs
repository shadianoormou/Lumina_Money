using System.Net;
using System.Net.Mail;

namespace LuminaMoney.Api.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct);
    Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken ct);
}

public sealed class SmtpAccountEmailSender(IConfiguration configuration) : IAccountEmailSender
{
    private readonly string? _host = configuration["Email:Smtp:Host"];
    private readonly int _port = configuration.GetValue("Email:Smtp:Port", 587);
    private readonly string? _username = configuration["Email:Smtp:Username"];
    private readonly string? _password = configuration["Email:Smtp:Password"];
    private readonly string? _fromAddress = configuration["Email:FromAddress"];
    private readonly string _fromName = configuration["Email:FromName"] ?? "Lumina Money";
    private readonly bool _enableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_host) && _port is > 0 and <= 65535
        && MailAddress.TryCreate(_fromAddress, out _)
        && (string.IsNullOrWhiteSpace(_username) == string.IsNullOrWhiteSpace(_password));

    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct) =>
        SendAsync(email, "Verify your Lumina Money email", displayName, code,
            "Enter this code in Lumina Money to verify your email address. It expires in 15 minutes.", ct);

    public Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken ct) =>
        SendAsync(email, "Reset your Lumina Money password", displayName, code,
            "Enter this code in Lumina Money to choose a new password. It expires in 15 minutes. If you did not request this, you can ignore this email.", ct);

    private async Task SendAsync(string email, string subject, string displayName, string code, string explanation, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Transactional email is not configured.");
        using var message = new MailMessage
        {
            From = new MailAddress(_fromAddress!, _fromName), Subject = subject, IsBodyHtml = true,
            Body = $"""
                <div style="font-family:system-ui,-apple-system,sans-serif;max-width:560px;margin:auto;color:#10242c">
                  <p style="color:#087b5b;font-weight:800;letter-spacing:.12em">LUMINA MONEY</p>
                  <h1 style="font-size:28px">Hello {WebUtility.HtmlEncode(displayName)},</h1>
                  <p>{WebUtility.HtmlEncode(explanation)}</p>
                  <div style="font-size:34px;font-weight:800;letter-spacing:.22em;background:#edf7f4;padding:20px;border-radius:16px;text-align:center">{code}</div>
                  <p style="color:#64777e;font-size:13px">Lumina Money will never ask you to send this code to another person.</p>
                </div>
                """
        };
        message.To.Add(new MailAddress(email));
        using var smtp = new SmtpClient(_host!, _port) { EnableSsl = _enableSsl };
        if (!string.IsNullOrWhiteSpace(_username)) smtp.Credentials = new NetworkCredential(_username, _password);
        await smtp.SendMailAsync(message, ct);
    }
}
