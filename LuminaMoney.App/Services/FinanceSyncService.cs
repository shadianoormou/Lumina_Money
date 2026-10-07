namespace LuminaMoney.App.Services;
public sealed class FinanceSyncService(FinanceDatabase database,FinanceApiClient api,SessionService session)
{
 public string? LastError { get; private set; }
 public int PendingCount { get; private set; }

 public async Task<bool> TrySyncAsync(CancellationToken ct=default)
 {
  var pending=await database.PendingAsync();PendingCount=pending.Count;LastError=null;
  if(!await session.HasSessionAsync()){LastError="Sign in to enable cloud sync";return false;}
  if(Connectivity.Current.NetworkAccess!=NetworkAccess.Internet){LastError=PendingCount==0?"No internet connection":"No internet · changes remain safely queued";return false;}
  try
  {
   foreach(var item in pending){await api.SendAsync(item,ct);await database.CompletePendingAsync(item.Sequence);PendingCount--;}
   var snapshot=await api.PullAsync(ct);await database.ReplaceFromServerAsync(snapshot);LastError=null;return true;
  }
  catch(OperationCanceledException) when(!ct.IsCancellationRequested){LastError="Cloud sync timed out · changes remain safely queued";return false;}
  catch(UnauthorizedAccessException ex){LastError=ex.Message;return false;}
  catch(HttpRequestException ex){LastError=string.IsNullOrWhiteSpace(ex.Message)?"Cloud service is temporarily unreachable":ex.Message;return false;}
  catch(InvalidOperationException ex){LastError=ex.Message;return false;}
 }
}
