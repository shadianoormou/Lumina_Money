using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaMoney.App.Services;

namespace LuminaMoney.App.ViewModels;

public partial class MembershipViewModel(StoreBillingService store, FinanceApiClient api) : ObservableObject
{
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsConfigured { get; set; }
    [ObservableProperty] public partial string CurrentPlan { get; set; } = "Free";
    [ObservableProperty] public partial string Status { get; set; } = "Checking secure store connection…";
    [ObservableProperty] public partial bool HasProducts { get; set; }
    public ObservableCollection<StoreProduct> Products { get; } = [];

    public async Task LoadAsync()
    {
        if (IsBusy) return; IsBusy = true; IsConfigured = store.IsConfigured;
        try
        {
            var entitlement = await api.GetSubscriptionStatusAsync(); CurrentPlan = entitlement.Plan;
            if (!store.IsConfigured) { Status = "Billing is disabled in this build. Release builds require IAPHUB App Store and Google Play credentials."; Products.Clear(); HasProducts = false; return; }
            var products = await store.GetProductsAsync(); Products.ReplaceWith(products); HasProducts = Products.Count > 0;
            Status = entitlement.IsPremium ? $"Active membership verified via {entitlement.Source}." : HasProducts ? "Choose a plan. Your store confirms the price before payment." : "No subscription products are available for this store account.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or UnauthorizedAccessException) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task BuyAsync(StoreProduct product)
    {
        if (IsBusy) return; IsBusy = true; Status = "Opening the secure store checkout…";
        try { var entitlement = await store.BuyAsync(product); CurrentPlan = entitlement.Plan; Status = entitlement.IsPremium ? "Lumina Plus is active and verified." : "The purchase is being verified. Use Restore purchases in a moment."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy) return; IsBusy = true; Status = "Restoring purchases securely…";
        try { var entitlement = await store.RestoreAsync(); CurrentPlan = entitlement.Plan; Status = entitlement.IsPremium ? "Purchase restored and verified." : "No active membership was restored."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ManageAsync()
    {
        if (IsBusy) return; IsBusy = true;
        try { await store.ManageAsync(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
}
