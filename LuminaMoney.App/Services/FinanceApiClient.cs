using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using LuminaMoney.Core;
namespace LuminaMoney.App.Services;
public sealed class FinanceApiClient(HttpClient http,SessionService session)
{
 private readonly SemaphoreSlim _refreshLock=new(1,1);
 public Task<AuthResponse> LoginAsync(LoginRequest request,CancellationToken ct=default)=>PostAuthAsync("auth/login",request,ct);
 public Task<AuthResponse> RegisterAsync(RegisterRequest request,CancellationToken ct=default)=>PostAuthAsync("auth/register",request,ct);
 public Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request,CancellationToken ct=default)=>PostAuthAsync("auth/email/verify",request,ct);
 public Task<AccountActionResponse> ResendEmailVerificationAsync(string email,CancellationToken ct=default)=>PostPublicAsync<RequestPasswordResetRequest,AccountActionResponse>("auth/email/resend",new(email),ct);
 public Task<AccountActionResponse> RequestPasswordResetAsync(string email,CancellationToken ct=default)=>PostPublicAsync<RequestPasswordResetRequest,AccountActionResponse>("auth/password/forgot",new(email),ct);
 public async Task ResetPasswordAsync(ResetPasswordRequest request,CancellationToken ct=default){using var response=await http.PostAsJsonAsync("auth/password/reset",request,ct);await EnsureSuccessAsync(response,ct);}
 public Uri PublicUri(string path)=>new(http.BaseAddress??throw new InvalidOperationException("API base URL is missing."),$"../../{path.TrimStart('/')}");
 public async Task LogoutAsync(CancellationToken ct=default){var refresh=await session.GetRefreshTokenAsync();if(refresh is null)return;try{using var response=await http.PostAsJsonAsync("auth/logout",new RefreshRequest(refresh),ct);}catch(HttpRequestException){}}
 private async Task<AuthResponse> PostAuthAsync<T>(string path,T request,CancellationToken ct){using var response=await http.PostAsJsonAsync(path,request,ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException((await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken:ct))?.Message??"Authentication failed.");var auth=await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken:ct)??throw new InvalidOperationException("The server returned an invalid response.");await session.SaveAsync(auth);return auth;}
 private async Task<TResponse> PostPublicAsync<TRequest,TResponse>(string path,TRequest value,CancellationToken ct){using var response=await http.PostAsJsonAsync(path,value,ct);await EnsureSuccessAsync(response,ct);return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken:ct)??throw new InvalidOperationException("The server returned an invalid response.");}
 public async Task<SyncSnapshot> PullAsync(CancellationToken ct=default){using var request=await CreateAsync(HttpMethod.Get,"finance/sync",ct);using var response=await http.SendAsync(request,ct);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<SyncSnapshot>(cancellationToken:ct)??throw new InvalidOperationException("Invalid sync response.");}
 public Task<AccountExportResponse> ExportAccountAsync(CancellationToken ct=default)=>GetAuthorizedAsync<AccountExportResponse>("account/export",ct);
 public Task<SubscriptionStatusResponse> GetSubscriptionStatusAsync(CancellationToken ct=default)=>GetAuthorizedAsync<SubscriptionStatusResponse>("subscriptions/status",ct);
 public Task<SubscriptionStatusResponse> VerifySubscriptionAsync(VerifySubscriptionRequest value,CancellationToken ct=default)=>PostAuthorizedAsync<VerifySubscriptionRequest,SubscriptionStatusResponse>("subscriptions/verify",value,ct);
 public Task<BankProviderStatusResponse> GetBankProviderStatusAsync(CancellationToken ct=default)=>GetAuthorizedAsync<BankProviderStatusResponse>("bank-connections/provider",ct);
 public Task<IReadOnlyList<BankConnectionResponse>> GetBankConnectionsAsync(CancellationToken ct=default)=>GetAuthorizedAsync<IReadOnlyList<BankConnectionResponse>>("bank-connections",ct);
 public Task<StartBankConnectionResponse> StartBankConnectionAsync(CancellationToken ct=default)=>PostAuthorizedAsync<object,StartBankConnectionResponse>("bank-connections/truelayer/start",new{},ct);
 public Task<BankConnectionResponse> RefreshBankConnectionAsync(Guid id,CancellationToken ct=default)=>PostAuthorizedAsync<object,BankConnectionResponse>($"bank-connections/{id}/refresh",new{},ct);
 public async Task DeleteBankConnectionAsync(Guid id,CancellationToken ct=default){using var request=await CreateAsync(HttpMethod.Delete,$"bank-connections/{id}",ct);using var response=await http.SendAsync(request,ct);await EnsureSuccessAsync(response,ct);}
 public async Task DeleteAccountAsync(DeleteAccountRequest value,CancellationToken ct=default){using var request=await CreateAsync(HttpMethod.Delete,"account",ct);request.Content=JsonContent.Create(value);using var response=await http.SendAsync(request,ct);await EnsureSuccessAsync(response,ct);}
 public async Task SendAsync(PendingOperationRow operation,CancellationToken ct=default){using var request=await CreateAsync(new HttpMethod(operation.Method),operation.Path,ct);if(operation.Payload is not null)request.Content=new StringContent(operation.Payload,Encoding.UTF8,"application/json");using var response=await http.SendAsync(request,ct);await EnsureSuccessAsync(response,ct);}
 private async Task<T> GetAuthorizedAsync<T>(string path,CancellationToken ct){using var request=await CreateAsync(HttpMethod.Get,path,ct);using var response=await http.SendAsync(request,ct);await EnsureSuccessAsync(response,ct);return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct)??throw new InvalidOperationException("The server returned an invalid response.");}
 private async Task<TResponse> PostAuthorizedAsync<TRequest,TResponse>(string path,TRequest value,CancellationToken ct){using var request=await CreateAsync(HttpMethod.Post,path,ct);request.Content=JsonContent.Create(value);using var response=await http.SendAsync(request,ct);await EnsureSuccessAsync(response,ct);return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken:ct)??throw new InvalidOperationException("The server returned an invalid response.");}
 private static async Task EnsureSuccessAsync(HttpResponseMessage response,CancellationToken ct){if(response.IsSuccessStatusCode)return;var error=await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken:ct);throw new InvalidOperationException(error?.Message??$"The server returned {(int)response.StatusCode}.");}
 private async Task<HttpRequestMessage> CreateAsync(HttpMethod method,string path,CancellationToken ct){var request=new HttpRequestMessage(method,path);var token=await GetAccessTokenAsync(ct);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);return request;}
 private async Task<string> GetAccessTokenAsync(CancellationToken ct)
 {
  var access=await session.GetValidAccessTokenAsync();if(access is not null)return access;await _refreshLock.WaitAsync(ct);
  try{access=await session.GetValidAccessTokenAsync();if(access is not null)return access;var refresh=await session.GetRefreshTokenAsync()??throw new UnauthorizedAccessException("Sign in to sync.");
   using var response=await http.PostAsJsonAsync("auth/refresh",new RefreshRequest(refresh),ct);if(!response.IsSuccessStatusCode){session.Clear();throw new UnauthorizedAccessException("Your session has expired.");}var auth=await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken:ct)??throw new UnauthorizedAccessException("Invalid session response.");await session.SaveAsync(auth);return auth.AccessToken;}
  finally{_refreshLock.Release();}
 }
}
