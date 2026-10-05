using System;
using UnityEngine;

/// <summary>Per-player whole-dollar balance. Multiplayer writes come only from the host account snapshot.</summary>
[DisallowMultipleComponent]
public sealed class PlayerCurrency : MonoBehaviour
{
    [SerializeField, Min(0)] private int money;
    public int Money => money;
    public event Action Changed;
    internal void SetAuthoritativeMoney(int value)
    {
        if (value < 0 || value == money) return;
        money = value; Changed?.Invoke();
    }
}
