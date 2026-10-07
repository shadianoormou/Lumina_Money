using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using LuminaMoney.Api.Data;
using LuminaMoney.Api.Security;
using LuminaMoney.Api.Services;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LuminaMoney.Api.Controllers;

[ApiController, Route("api/v1/auth"), EnableRateLimiting("auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    FinanceDbContext db,
    TokenService tokens,
    IAccountEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string VerifyEmailPurpose = "verify_email";
    private const string ResetPasswordPurpose = "reset_password";
    private bool RequireConfirmedEmail => configuration.GetValue("Identity:RequireConfirmedEmail", false);
    private int CodeLifetimeMinutes => Math.Clamp(configuration.GetValue("Identity:AccountCodeMinutes", 15), 5, 60);
    private int MaxCodeAttempts => Math.Clamp(configuration.GetValue("Identity:AccountCodeMaxAttempts", 5), 3, 10);
    private int ResendSeconds => Math.Clamp(configuration.GetValue("Identity:AccountCodeResendSeconds", 60), 30, 600);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 80)
            return BadRequest(new ApiError("display_name_required", "A display name up to 80 characters is required."));
        if (!ValidEmail(request.Email)) return BadRequest(new ApiError("invalid_email", "A valid email address is required."));
        if (string.IsNullOrEmpty(request.Password)) return BadRequest(new ApiError("password_required", "A password is required."));

        var user = new AppUser { Id = Guid.NewGuid(), Email = request.Email.Trim(), UserName = request.Email.Trim(), DisplayName = request.DisplayName.Trim() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new ApiError("registration_failed", "Registration could not be completed.",
                result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray())));

        await SeedStarterPlanAsync(user.Id, ct);
        if (RequireConfirmedEmail) await SendChallengeAsync(user, VerifyEmailPurpose, ct);
        return Ok(await IssueAsync(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        if (!ValidEmail(request.Email) || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new ApiError("invalid_credentials", "Email or password is incorrect."));
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Unauthorized(new ApiError("invalid_credentials", "Email or password is incorrect."));
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, true);
        if (result.IsLockedOut) return StatusCode(423, new ApiError("account_locked", "Account is temporarily locked."));
        if (!result.Succeeded) return Unauthorized(new ApiError("invalid_credentials", "Email or password is incorrect."));
        if (RequireConfirmedEmail && !user.EmailConfirmed) await SendChallengeAsync(user, VerifyEmailPurpose, ct);
        return Ok(await IssueAsync(user));
    }

    [HttpPost("email/resend")]
    public async Task<ActionResult<AccountActionResponse>> ResendVerification(RequestPasswordResetRequest request, CancellationToken ct)
    {
        if (ValidEmail(request.Email) && await users.FindByEmailAsync(request.Email.Trim()) is { EmailConfirmed: false } user)
            await SendChallengeAsync(user, VerifyEmailPurpose, ct);
        return Accepted(new AccountActionResponse("If the account needs verification, a new code has been sent."));
    }

    [HttpPost("email/verify")]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        var user = ValidEmail(request.Email) ? await users.FindByEmailAsync(request.Email.Trim()) : null;
        if (user is null || await ValidateChallengeAsync(user, VerifyEmailPurpose, request.Code, true, ct) is null)
            return BadRequest(new ApiError("invalid_verification_code", "The verification code is invalid or has expired."));

        user.EmailConfirmed = true;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) return Problem(statusCode: 500, title: "Email verification could not be completed.");
        await RevokeSessionsAsync(user.Id, ct);
        return Ok(await IssueAsync(user));
    }

    [HttpPost("password/forgot")]
    public async Task<ActionResult<AccountActionResponse>> ForgotPassword(RequestPasswordResetRequest request, CancellationToken ct)
    {
        if (ValidEmail(request.Email) && await users.FindByEmailAsync(request.Email.Trim()) is { } user)
            await SendChallengeAsync(user, ResetPasswordPurpose, ct);
        return Accepted(new AccountActionResponse("If an account exists for that email, a reset code has been sent."));
    }

    [HttpPost("password/reset")]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = ValidEmail(request.Email) ? await users.FindByEmailAsync(request.Email.Trim()) : null;
        if (user is null || await ValidateChallengeAsync(user, ResetPasswordPurpose, request.Code, false, ct) is not { } challenge)
            return BadRequest(new ApiError("invalid_reset_code", "The reset code is invalid or has expired."));

        var identityToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, identityToken, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new ApiError("password_reset_failed", "Choose a stronger password.",
                result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray())));
        challenge.UsedUtc = DateTime.UtcNow;
        if (!user.EmailConfirmed) { user.EmailConfirmed = true; await users.UpdateAsync(user); }
        await RevokeSessionsAsync(user.Id, ct);
        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized(new ApiError("invalid_refresh_token", "The session has expired. Please sign in again."));
        var hash = TokenService.Hash(request.RefreshToken);
        var existing = await db.RefreshTokens.Include(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (existing is null || existing.ExpiresUtc <= DateTime.UtcNow)
            return Unauthorized(new ApiError("invalid_refresh_token", "The session has expired. Please sign in again."));
        var familyId = existing.FamilyId ?? existing.Id;
        if (existing.RevokedUtc is not null)
        {
            var activeFamily = await db.RefreshTokens.Where(x => x.UserId == existing.UserId && (x.FamilyId == familyId || x.Id == familyId) && x.RevokedUtc == null).ToListAsync(ct);
            foreach (var token in activeFamily) token.RevokedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Unauthorized(new ApiError("refresh_token_reuse", "This session was revoked because an old refresh token was reused."));
        }
        existing.RevokedUtc = DateTime.UtcNow;
        return Ok(await IssueAsync(existing.User, familyId));
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return NoContent();
        var hash = TokenService.Hash(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (existing is not null) { existing.RevokedUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        return NoContent();
    }

    private async Task<AuthResponse> IssueAsync(AppUser user, Guid? familyId = null)
    {
        var token = tokens.Create(user);
        db.RefreshTokens.Add(new RefreshTokenEntity { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = token.RefreshTokenHash, ExpiresUtc = token.RefreshExpiresUtc, FamilyId = familyId ?? Guid.NewGuid() });
        await db.SaveChangesAsync();
        return new(token.AccessToken, token.AccessExpiresUtc, token.RefreshToken, token.RefreshExpiresUtc, user.Id.ToString(), user.DisplayName,
            user.Email ?? user.UserName ?? "", RequireConfirmedEmail && !user.EmailConfirmed);
    }

    private async Task SendChallengeAsync(AppUser user, string purpose, CancellationToken ct)
    {
        if (!emailSender.IsConfigured || string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Account email was not sent for user {UserId}: transactional email is not configured", user.Id);
            return;
        }
        var now = DateTime.UtcNow;
        if (await db.AccountActionChallenges.AnyAsync(x => x.UserId == user.Id && x.Purpose == purpose && x.CreatedUtc > now.AddSeconds(-ResendSeconds), ct)) return;
        var previous = await db.AccountActionChallenges.Where(x => x.UserId == user.Id && x.Purpose == purpose && x.UsedUtc == null).ToListAsync(ct);
        foreach (var item in previous) item.UsedUtc = now;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.AccountActionChallenges.Add(new AccountActionChallengeEntity
        {
            Id = Guid.NewGuid(), UserId = user.Id, Purpose = purpose, CodeHash = HashCode(user.Id, purpose, code),
            CreatedUtc = now, ExpiresUtc = now.AddMinutes(CodeLifetimeMinutes)
        });
        await db.SaveChangesAsync(ct);
        try
        {
            if (purpose == VerifyEmailPurpose) await emailSender.SendVerificationCodeAsync(user.Email, user.DisplayName, code, ct);
            else await emailSender.SendPasswordResetCodeAsync(user.Email, user.DisplayName, code, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException)
        {
            logger.LogError(ex, "Transactional account email failed for user {UserId}", user.Id);
        }
    }

    private async Task<AccountActionChallengeEntity?> ValidateChallengeAsync(AppUser user, string purpose, string? code, bool consume, CancellationToken ct)
    {
        var challenge = await db.AccountActionChallenges.Where(x => x.UserId == user.Id && x.Purpose == purpose && x.UsedUtc == null)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.ExpiresUtc <= DateTime.UtcNow || challenge.FailedAttempts >= MaxCodeAttempts || string.IsNullOrWhiteSpace(code)) return null;
        var expected = Convert.FromBase64String(challenge.CodeHash);
        var actual = Convert.FromBase64String(HashCode(user.Id, purpose, code.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            challenge.FailedAttempts++;
            if (challenge.FailedAttempts >= MaxCodeAttempts) challenge.UsedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (consume) { challenge.UsedUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        return challenge;
    }

    private string HashCode(Guid userId, string purpose, string code)
    {
        var pepper = configuration["Identity:AccountCodePepper"] ?? throw new InvalidOperationException("Identity account-code pepper is missing.");
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes($"{userId:N}|{purpose}|{code}")));
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedUtc == null).ToListAsync(ct);
        foreach (var token in active) token.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 254 && new EmailAddressAttribute().IsValid(value);

    private async Task SeedStarterPlanAsync(Guid userId, CancellationToken ct)
    {
        db.Accounts.Add(new AccountEntity { Id = Guid.NewGuid(), UserId = userId, Name = "Everyday account", Type = (int)AccountType.Current, Currency = "GBP", Color = "#2F72F4" });
        db.Categories.AddRange(
            new CategoryEntity { Id = Guid.NewGuid(), UserId = userId, Name = "Home", Icon = "⌂", Color = "#10B981", MonthlyTarget = 1200, SortOrder = 1 },
            new CategoryEntity { Id = Guid.NewGuid(), UserId = userId, Name = "Food", Icon = "◉", Color = "#F2A93B", MonthlyTarget = 450, SortOrder = 2 },
            new CategoryEntity { Id = Guid.NewGuid(), UserId = userId, Name = "Transport", Icon = "↗", Color = "#2F72F4", MonthlyTarget = 250, SortOrder = 3 });
        await db.SaveChangesAsync(ct);
    }
}
