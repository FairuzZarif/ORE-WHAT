using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Local service menu using the existing inventory and pickup interaction. No shared NPC lock.</summary>
[RequireComponent(typeof(PlayerInventory), typeof(PlayerCurrency))]
public sealed class CompanyOfficeUI : MonoBehaviour
{
    public static bool AnyOpen { get; private set; }
    public bool IsOpen => worker != null;
    public bool Pending { get; private set; }
    public int LastEarned { get; private set; }
    public Button SellAllButton { get; private set; }
    public CompanyWorkerNPC Worker => worker;
    PlayerInventory inventory;
    PlayerCurrency currency;
    PlayerEquipment equipment;
    PlayerAttributes attributes;
    Camera eye;
    CompanyWorkerNPC worker;
    NetworkPlayerEconomy boundAccount;
    GameObject panel;
    Text money, dialogue, total, receipt, ownership;
    readonly List<Text> quantities = new List<Text>(), values = new List<Text>();
    readonly List<Button> buttons = new List<Button>();
    readonly List<(Behaviour component, bool enabled)> suspended = new List<(Behaviour, bool)>();
    Font font;
    AudioSource audioSource;
    AudioClip saleSound;
    int openedFrame;
    CursorLockMode previousLock;
    bool previousCursor;
    float receiptUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AnyOpen = false;
    void Awake()
    {
        inventory = GetComponent<PlayerInventory>(); currency = GetComponent<PlayerCurrency>(); equipment = GetComponent<PlayerEquipment>();
        attributes = GetComponent<PlayerAttributes>(); eye = GetComponentInChildren<Camera>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0f;
        saleSound = MakeSaleSound(); BuildUI();
    }
    void OnEnable()
    {
        inventory.Changed += Refresh; currency.Changed += Refresh;
    }
    void OnDisable()
    {
        Close(); inventory.Changed -= Refresh; currency.Changed -= Refresh;
        if (boundAccount != null) boundAccount.SaleCompleted -= OnSale;
        boundAccount = null;
    }
    void OnDestroy() { if (saleSound != null) Destroy(saleSound); }
    void Update()
    {
        if (boundAccount != NetworkPlayerEconomy.Local)
        {
            if (boundAccount != null) boundAccount.SaleCompleted -= OnSale;
            boundAccount = NetworkPlayerEconomy.Local;
            if (boundAccount != null) boundAccount.SaleCompleted += OnSale;
        }
        if (!IsOpen) return;
        if (attributes != null && attributes.IsDead) { Close(); return; }
        if (!worker.CanInteract(eye.transform.position, transform.position)) { Close(); return; }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (Time.frameCount != openedFrame && Keyboard.current != null &&
            (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame)) Close();
        if (receiptUntil > 0 && Time.unscaledTime > receiptUntil) { receipt.text = ""; receiptUntil = 0; }
    }
    public bool Open(CompanyWorkerNPC npc)
    {
        if (IsOpen || npc == null || eye == null || (attributes != null && attributes.IsDead) ||
            !npc.CanInteract(eye.transform.position, transform.position) || (equipment.ActiveController != null && equipment.ActiveController.IsBusy)) return false;
        worker = npc; AnyOpen = true; Pending = false; LastEarned = 0; receiptUntil = 0; openedFrame = Time.frameCount;
        previousLock = Cursor.lockState; previousCursor = Cursor.visible;
        equipment.ShowUnarmedAction(false);
        Suspend(GetComponent<PlayerLook>()); Suspend(GetComponent<PlayerMovement>()); Suspend(GetComponent<PlayerCrouch>());
        Suspend(GetComponent<UnarmedAttack>()); Suspend(GetComponent<MiningController>()); Suspend(GetComponent<ItemPickupInteractor>());
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        BuildRows(); panel.SetActive(true); dialogue.text = "Got anything worth selling?"; receipt.text = ""; Refresh();
        return true;
    }
    void Suspend(Behaviour component)
    {
        if (component == null) return;
        suspended.Add((component, component.enabled)); component.enabled = false;
    }
    public void Close() => Close(false);
    // Death must capture the pre-menu input state before disabling it for the respawn period.
    internal void CloseBeforeDeath() => Close(true);
    void Close(bool beforeDeath)
    {
        if (!IsOpen) return;
        worker = null; AnyOpen = false;
        if (panel != null) panel.SetActive(false);
        bool alive = attributes == null || !attributes.IsDead;
        foreach (var state in suspended) if (state.component != null && (alive || beforeDeath)) state.component.enabled = state.enabled;
        suspended.Clear();
        if (alive) { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
    }
    public bool Sell(ItemData only = null)
    {
        if (!IsOpen || Pending || CompanySale.Quote(inventory, only) == 0 || !worker.CanInteract(eye.transform.position, transform.position)) return false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (boundAccount == null) { dialogue.text = "Connecting to the company ledger..."; return false; }
            Pending = true; Refresh();
            if (!boundAccount.RequestSale(worker, only)) { Pending = false; Refresh(); return false; }
            return true;
        }
        bool crystal = false;
        foreach (var slot in inventory.Slots) if (!slot.IsEmpty && slot.item.CompanyOre && (only == null || only == slot.item) && slot.item.ItemId == "crystal") crystal = true;
        bool sold = CompanySale.Commit(inventory, currency, only, out int earned);
        OnSale(earned, sold ? crystal ? "Now that's company property." : "Company appreciates your contribution." : "Come back when you've actually mined something.");
        return sold;
    }
    void OnSale(int earned, string message)
    {
        Pending = false; LastEarned = earned;
        if (IsOpen) dialogue.text = message;
        if (earned > 0)
        {
            receipt.text = "+$" + earned.ToString("N0"); receiptUntil = Time.unscaledTime + 3f;
            audioSource.PlayOneShot(saleSound, 0.3f);
        }
        Refresh();
    }
    void Refresh()
    {
        if (money != null) money.text = "$" + currency.Money.ToString("N0");
        if (!IsOpen || total == null) return;
        int quote = CompanySale.Quote(inventory);
        total.text = "TOTAL VALUE   $" + quote.ToString("N0");
        SellAllButton.interactable = quote > 0 && !Pending;
        for (int i = 0; i < worker.Ores.Length; i++)
        {
            quantities[i].text = "x" + inventory.Count(worker.Ores[i]);
            values[i].text = "$" + CompanySale.Quote(inventory, worker.Ores[i]).ToString("N0");
            buttons[i].interactable = !Pending && inventory.Count(worker.Ores[i]) > 0;
        }
        ownership.text = GetComponent<OreCarryController>()?.IsCarrying == true
            ? "Carried ore stays in the world. Close this menu and press F to store it first."
            : "The company buys ores stored in your hotbar inventory.";
        if (quote == 0 && !Pending && LastEarned == 0) dialogue.text = "Come back when you've actually mined something.";
    }

    Transform rows;
    void BuildUI()
    {
        if (UnityEngine.EventSystems.EventSystem.current == null)
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        var canvasGO = new GameObject("CompanyOfficeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = canvasGO.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        money = Label("Money", canvasGO.transform, "$0", new Vector2(24, 124), new Vector2(300, 30), 24);
        money.rectTransform.anchorMin = money.rectTransform.anchorMax = Vector2.zero; money.alignment = TextAnchor.MiddleLeft;
        var backdrop = Rect("CompanyOfficePanel", canvasGO.transform, new Vector2(-380, 290), new Vector2(760, 580));
        backdrop.anchorMin = backdrop.anchorMax = new Vector2(0.5f, 0.5f);
        var image = backdrop.gameObject.AddComponent<Image>(); image.color = new Color(0.07f, 0.055f, 0.047f, 0.97f);
        var outline = backdrop.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0.8f, 0.62f, 0.3f); outline.effectDistance = new Vector2(2, -2);
        panel = backdrop.gameObject;
        Label("Title", backdrop, "COMPANY OFFICE", new Vector2(32, -24), new Vector2(600, 40), 30, new Color(1, 0.82f, 0.55f));
        dialogue = Label("Dialogue", backdrop, "", new Vector2(32, -76), new Vector2(690, 48), 22);
        rows = Rect("OreRows", backdrop, new Vector2(32, -142), new Vector2(696, 232));
        total = Label("Total", backdrop, "", new Vector2(32, -390), new Vector2(690, 38), 25, new Color(1, 0.82f, 0.55f));
        ownership = Label("Ownership", backdrop, "", new Vector2(32, -432), new Vector2(690, 44), 17);
        receipt = Label("Receipt", backdrop, "", new Vector2(32, -488), new Vector2(190, 50), 26, new Color(0.55f, 0.9f, 0.5f));
        SellAllButton = Button("SellAll", backdrop, "SELL ALL ORES", new Vector2(220, -494), new Vector2(285, 52));
        SellAllButton.onClick.AddListener(() => Sell());
        Button("Close", backdrop, "CLOSE  [ESC]", new Vector2(522, -494), new Vector2(206, 52)).onClick.AddListener(Close);
        panel.SetActive(false); Refresh();
    }
    void BuildRows()
    {
        foreach (Transform child in rows) Destroy(child.gameObject);
        quantities.Clear(); values.Clear(); buttons.Clear();
        for (int i = 0; i < worker.Ores.Length; i++)
        {
            ItemData item = worker.Ores[i]; float y = -i * 56;
            Label("Ore", rows, item.DisplayName, new Vector2(0, y), new Vector2(260, 40), 22);
            quantities.Add(Label("Quantity", rows, "", new Vector2(270, y), new Vector2(95, 40), 22));
            values.Add(Label("Value", rows, "", new Vector2(370, y), new Vector2(180, 40), 22));
            var button = Button("SellType", rows, "SELL", new Vector2(568, y), new Vector2(128, 40));
            button.onClick.AddListener(() => Sell(item)); buttons.Add(button);
        }
    }
    RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = position; r.sizeDelta = size; return r;
    }
    Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color? color = null)
    {
        var r = Rect(name, parent, position, size); var label = r.gameObject.AddComponent<Text>();
        label.font = font; label.fontSize = fontSize; label.text = text; label.color = color ?? new Color(0.94f, 0.91f, 0.85f);
        label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false; return label;
    }
    Button Button(string name, Transform parent, string text, Vector2 position, Vector2 size)
    {
        var r = Rect(name, parent, position, size); var image = r.gameObject.AddComponent<Image>(); image.color = new Color(0.24f, 0.19f, 0.12f);
        var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var label = Label("Label", r, text, Vector2.zero, size, 20); label.alignment = TextAnchor.MiddleCenter;
        return button;
    }
    static AudioClip MakeSaleSound()
    {
        const int rate = 22050; float[] samples = new float[(int)(rate * 0.22f)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            samples[i] = Mathf.Sin(2 * Mathf.PI * (t < 0.09f ? 660 : 880) * t) * 0.16f * Mathf.Exp(-t * 15) * Mathf.Clamp01(t / 0.004f);
        }
        var clip = AudioClip.Create("Company receipt", samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
    }
}
