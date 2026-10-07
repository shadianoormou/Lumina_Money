using LuminaMoney.Core;
namespace LuminaMoney.App.Services;
public sealed class SessionService
{
 private const string AccessKey="lumina.access_token",AccessExpiryKey="lumina.access_expiry",RefreshKey="lumina.refresh_token",RefreshExpiryKey="lumina.refresh_expiry",UserIdKey="lumina.user_id",DisplayNameKey="lumina.display_name",EmailKey="lumina.email",VerificationKey="lumina.requires_email_verification";
 public async Task<string?> GetValidAccessTokenAsync(){var expiry=await ReadDateAsync(AccessExpiryKey);return expiry>DateTime.UtcNow.AddSeconds(15)?await SecureStorage.Default.GetAsync(AccessKey):null;}
 public async Task<string?> GetRefreshTokenAsync(){var expiry=await ReadDateAsync(RefreshExpiryKey);return expiry>DateTime.UtcNow?await SecureStorage.Default.GetAsync(RefreshKey):null;}
 public async Task<bool> HasSessionAsync()=>await GetRefreshTokenAsync() is not null&&Guid.TryParse(await GetUserIdAsync(),out _);
 public Task<string?> GetUserIdAsync()=>SecureStorage.Default.GetAsync(UserIdKey);
 public Task<string?> GetDisplayNameAsync()=>SecureStorage.Default.GetAsync(DisplayNameKey);
 public Task<string?> GetEmailAsync()=>SecureStorage.Default.GetAsync(EmailKey);
 public async Task<bool> RequiresEmailVerificationAsync()=>bool.TryParse(await SecureStorage.Default.GetAsync(VerificationKey),out var value)&&value;
 public async Task<string> GetProfileIdAsync(){var userId=await GetUserIdAsync();return Guid.TryParse(userId,out var parsed)?$"user-{parsed:N}":"offline";}
 public async Task SaveAsync(AuthResponse auth){await SecureStorage.Default.SetAsync(AccessKey,auth.AccessToken);await SecureStorage.Default.SetAsync(AccessExpiryKey,auth.ExpiresUtc.ToString("O"));await SecureStorage.Default.SetAsync(RefreshKey,auth.RefreshToken);await SecureStorage.Default.SetAsync(RefreshExpiryKey,auth.RefreshExpiresUtc.ToString("O"));await SecureStorage.Default.SetAsync(UserIdKey,auth.UserId);await SecureStorage.Default.SetAsync(DisplayNameKey,auth.DisplayName);await SecureStorage.Default.SetAsync(EmailKey,auth.Email);await SecureStorage.Default.SetAsync(VerificationKey,auth.RequiresEmailVerification.ToString());}
 public void Clear(){foreach(var key in new[]{AccessKey,AccessExpiryKey,RefreshKey,RefreshExpiryKey,UserIdKey,DisplayNameKey,EmailKey,VerificationKey})SecureStorage.Default.Remove(key);}
 private static async Task<DateTime> ReadDateAsync(string key)=>DateTime.TryParse(await SecureStorage.Default.GetAsync(key),out var value)?value.ToUniversalTime():DateTime.MinValue;
}
