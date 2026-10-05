using UnityEngine;

/// <summary>Shared transaction rules; used on the local player in SP and host inventory in MP.</summary>
public static class CompanySale
{
    public static int Quote(PlayerInventory inventory, ItemData only = null)
    {
        if (inventory == null || (only != null && !only.CompanyOre)) return 0;
        long value = 0;
        foreach (var slot in inventory.Slots)
            if (!slot.IsEmpty && slot.item.CompanyOre && (only == null || slot.item == only))
                value += (long)slot.amount * slot.item.Value;
        return value > int.MaxValue ? 0 : (int)value;
    }

    public static bool Commit(PlayerInventory inventory, PlayerCurrency currency, ItemData only, out int earned)
    {
        earned = Quote(inventory, only);
        if (earned <= 0 || currency == null || (long)currency.Money + earned > int.MaxValue) { earned = 0; return false; }
        // Called synchronously on the Unity main thread. Host snapshot publishing is deferred until both changes finish.
        inventory.RemoveCompanyOres(only);
        currency.SetAuthoritativeMoney(currency.Money + earned);
        return true;
    }
}
