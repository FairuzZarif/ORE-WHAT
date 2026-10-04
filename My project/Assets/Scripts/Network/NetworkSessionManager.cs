using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hosting and joining online games. Lives on the NetworkManager prefab (with the NetworkManager and
/// UnityTransport), made by the main menu the first time Host or Join is pressed and kept across scenes.
///
/// Host: signs in to Unity Gaming Services (anonymously), reserves a Unity Relay server (so nobody has to open
/// ports), shows Relay's short join code and loads the map for everyone. Join: the same with the code. Relay
/// passes the traffic between the players; the host's game is the authority.
///
/// Relay is used directly rather than through Multiplayer Sessions/Lobby: a session also opens a live WebSocket
/// to the Lobby service, and on PCs or networks that intercept secure WebSockets (some antivirus "HTTPS scanning",
/// proxies) that connection never succeeds and creating a session hangs. Relay needs only HTTPS + UDP.
///
/// It also turns everything that can go wrong into a short message (bad code, game full, no internet,
/// host left...) and always gets the player back to the menu, never stuck on a loading screen.
///
/// For testing on one PC without services: type an address (e.g. 127.0.0.1) as the code to join a host that
/// was started with the -lan command-line argument (direct connection, port 7777).
/// </summary>
public class NetworkSessionManager : MonoBehaviour
{
    public enum Phase { Offline, Working, InGame }

    [Tooltip("The map everyone plays on. Must be in the build's scene list.")]
    [SerializeField] private string gameplayScene = "PlayerTest";
    [Tooltip("Where players go when they leave.")]
    [SerializeField] private string menuScene = "MainMenu";
    [SerializeField, Range(2, 16)] private int maxPlayers = 4;
    [Tooltip("The shared world (rocks, items), spawned by the host when the map has loaded.")]
    [SerializeField] private NetworkObject worldPrefab;
    [Tooltip("Give up on a service call or connection after this many seconds.")]
    [SerializeField, Min(5f)] private float timeoutSeconds = 20f;
    [Tooltip("Port for direct (-lan) games.")]
    [SerializeField] private ushort directPort = 7777;

    public static NetworkSessionManager Instance { get; private set; }

    public Phase Current { get; private set; } = Phase.Offline;
    /// <summary>What is happening, or what went wrong (shown by the menu).</summary>
    public string Status { get; private set; } = "";
    /// <summary>True if Status describes a problem.</summary>
    public bool StatusIsError { get; private set; }
    /// <summary>The code friends type to join (host and clients), or null.</summary>
    public string JoinCode { get; private set; }
    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    public event Action Changed;
    /// <summary>Short in-game notices ("Player 2 joined"), for MultiplayerHUD.</summary>
    public event Action<string> Notice;

    private NetworkManager net;
    private bool leaving;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        net = GetComponent<NetworkManager>();
        // A paused (unfocused) game would stop answering the other players and they'd be disconnected.
        Application.runInBackground = true;
        string[] args = Environment.GetCommandLineArgs(); // -nettimeout 60: longer waits (slow connections, testing)
        int t = Array.IndexOf(args, "-nettimeout");
        if (t >= 0 && t + 1 < args.Length && float.TryParse(args[t + 1], out float seconds)) timeoutSeconds = Mathf.Max(5f, seconds);
        int m = Array.IndexOf(args, "-maxplayers"); // -maxplayers 2: a smaller game (also used to test "game full")
        if (m >= 0 && m + 1 < args.Length && int.TryParse(args[m + 1], out int max)) maxPlayers = Mathf.Clamp(max, 1, 16);
        net.OnClientConnectedCallback += OnClientConnected;
        net.OnClientDisconnectCallback += OnClientDisconnected;
        net.OnTransportFailure += OnTransportFailure;
        net.NetworkConfig.ConnectionApproval = true;
        net.ConnectionApprovalCallback = ApproveConnection;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (net == null) return;
        net.OnClientConnectedCallback -= OnClientConnected;
        net.OnClientDisconnectCallback -= OnClientDisconnected;
        net.OnTransportFailure -= OnTransportFailure;
    }

    // ------------------------------------------------------------------ host / join

    /// <summary>Starts a game others can join. With -lan on the command line: a direct game on port 7777, no services.</summary>
    public async void Host()
    {
        if (Current != Phase.Offline) return;
        SetStatus("Starting a game...");
        Current = Phase.Working;
        try
        {
            if (HasArg("-lan"))
            {
                Transport.SetConnectionData("127.0.0.1", directPort, "0.0.0.0"); // listen on every network card
                if (!net.StartHost()) throw new Exception("Couldn't start hosting (is the port in use?)");
                JoinCode = $"LAN {LocalAddress()}";
            }
            else
            {
                await SignIn();
                SetStatus("Reserving a Relay server...");
                Allocation allocation = await WithTimeout(RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1)), "Reserving a Relay server");
                JoinCode = await WithTimeout(RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId), "Getting a join code");
                Transport.SetRelayServerData(allocation.ToRelayServerData("dtls")); // encrypted
                if (!net.StartHost()) throw new Exception("Couldn't start hosting.");
            }
            SetStatus("Loading the map...");
            net.SceneManager.OnLoadEventCompleted += OnMapLoaded;
            var result = net.SceneManager.LoadScene(gameplayScene, LoadSceneMode.Single);
            if (result != SceneEventProgressStatus.Started) throw new Exception($"Couldn't load the map ({result}).");
        }
        catch (Exception e)
        {
            await Fail(Describe(e, hosting: true));
        }
    }

    /// <summary>Joins a friend's game with its code (or, for testing, a direct address like 127.0.0.1).</summary>
    public async void Join(string code)
    {
        if (Current != Phase.Offline) return;
        code = (code ?? "").Trim();
        if (code.Length == 0) { SetStatus("Type the host's join code first.", error: true); return; }
        SetStatus("Joining...");
        Current = Phase.Working;
        try
        {
            if (LooksLikeAddress(code))
            {
                string[] parts = code.Split(':');
                Transport.SetConnectionData(parts[0], parts.Length > 1 && ushort.TryParse(parts[1], out ushort p) ? p : directPort);
                if (!net.StartClient()) throw new Exception("Couldn't start the connection.");
                JoinCode = $"LAN {parts[0]}";
            }
            else
            {
                await SignIn();
                SetStatus("Finding the game...");
                JoinAllocation allocation = await WithTimeout(RelayService.Instance.JoinAllocationAsync(code.ToUpperInvariant()), "Finding the game");
                Transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));
                if (!net.StartClient()) throw new Exception("Couldn't start the connection.");
                JoinCode = code.ToUpperInvariant();
            }

            // The host now sends us the map; give up if the connection never completes.
            SetStatus("Connecting to the host...");
            float giveUp = Time.realtimeSinceStartup + timeoutSeconds;
            while (net.IsListening && !net.IsConnectedClient && Time.realtimeSinceStartup < giveUp) await Task.Yield();
            if (!net.IsConnectedClient) throw new TimeoutException("Couldn't reach the host.");

            SetStatus("Loading the map...");
            giveUp = Time.realtimeSinceStartup + timeoutSeconds * 3f; // a big map can take a while
            while (net.IsConnectedClient && SceneManager.GetActiveScene().name != gameplayScene && Time.realtimeSinceStartup < giveUp) await Task.Yield();
            if (!net.IsConnectedClient) return; // OnClientDisconnected already explained why
            if (SceneManager.GetActiveScene().name != gameplayScene) throw new TimeoutException("The map never finished loading.");
            Current = Phase.InGame;
            SetStatus("");
        }
        catch (Exception e)
        {
            if (Current == Phase.Working) await Fail(Describe(e, hosting: false));
        }
    }

    /// <summary>Leaves the game (the host ends it for everyone) and goes back to the menu.</summary>
    public async void Leave() => await LeaveAndReturn(IsHost ? "You ended the game." : "You left the game.", error: false);

    // ------------------------------------------------------------------ events

    private void OnMapLoaded(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> done, System.Collections.Generic.List<ulong> timedOut)
    {
        if (sceneName != gameplayScene || !net.IsServer) return;
        net.SceneManager.OnLoadEventCompleted -= OnMapLoaded;
        if (worldPrefab != null && NetworkWorld.Instance == null)
            Instantiate(worldPrefab).Spawn(destroyWithScene: true);
        Current = Phase.InGame;
        SetStatus("");
    }

    private void OnClientConnected(ulong clientId)
    {
        if (net.IsServer && clientId != NetworkManager.ServerClientId) Notice?.Invoke($"Player {clientId} joined");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (leaving) return;
        if (net.IsServer)
        {
            // Someone else left: their avatar is removed by Netcode; the game goes on.
            if (clientId != NetworkManager.ServerClientId) Notice?.Invoke($"Player {clientId} left");
            return;
        }
        // We are a client and lost the host (it left, crashed, or the connection dropped), or it turned us away.
        _ = LeaveAndReturn(DisconnectMessage(net.DisconnectReason), error: true);
    }

    /// <summary>Netcode's reasons ("[Disconnect Event]...MaxConnectionAttempts...") in player words. The host's own reasons (e.g. "full") pass through.</summary>
    private string DisconnectMessage(string reason)
    {
        string r = reason ?? "";
        if (r.Contains("host shutting down") || r.Contains("HostShutdown") || r.Contains("ServerShutdown")) return "The host ended the game.";
        if (Current != Phase.InGame && (r.Length == 0 || r.Contains("MaxConnectionAttempts") || r.StartsWith("[Disconnect Event]")))
            return "Couldn't connect to the host. Check the code, and that the game is still running.";
        if (r.Length == 0 || r.StartsWith("[Disconnect Event]")) return "Lost the connection to the host.";
        return r;
    }

    /// <summary>Host: lets players in until the game is full (the Relay server is also reserved for that many players).</summary>
    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;
        response.Approved = isHostItself || net.ConnectedClientsIds.Count < maxPlayers;
        response.CreatePlayerObject = response.Approved;
        if (!response.Approved) response.Reason = "That game is full.";
    }

    private void OnTransportFailure()
    {
        if (!leaving) _ = LeaveAndReturn("The network connection failed.", error: true);
    }

    // ------------------------------------------------------------------ helpers

    private UnityTransport Transport => (UnityTransport)net.NetworkConfig.NetworkTransport;

    private static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            // A fresh anonymous profile per game launch, so two copies of the game on one PC are two different players.
            var options = new InitializationOptions().SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        Debug.Log($"[Ore What] Signed in to Unity services (player {AuthenticationService.Instance.PlayerId}, project {Application.cloudProjectId}).");
    }

    private async Task<T> WithTimeout<T>(Task<T> task, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != task)
        {
            // We've given up, but the call may still finish: log how (useful when diagnosing a slow or blocked network).
            _ = task.ContinueWith(t => Debug.LogWarning($"[Ore What] {what} {(t.IsFaulted ? "failed after the timeout: " + t.Exception.GetBaseException().Message : "finished after the timeout.")}"),
                                  TaskScheduler.FromCurrentSynchronizationContext());
            throw new TimeoutException($"{what} took too long.");
        }
        return await task;
    }

    private async Task Fail(string message)
    {
        await LeaveAndReturn(message, error: true);
    }

    /// <summary>Leaves the network (a host's Relay server and join code expire with it), then makes sure we're on the menu with the message showing.</summary>
    private async Task LeaveAndReturn(string message, bool error)
    {
        if (leaving) return;
        leaving = true;
        if (net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= OnMapLoaded;
        if (net.IsListening || net.ShutdownInProgress) net.Shutdown();
        while (net.ShutdownInProgress) await Task.Yield();

        JoinCode = null;
        Current = Phase.Offline;
        SetStatus(message, error);
        leaving = false;
        if (SceneManager.GetActiveScene().name != menuScene) SceneManager.LoadScene(menuScene);
    }

    private void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusIsError = error;
        if (error) Debug.LogWarning("[Ore What] " + text);
        else if (text.Length > 0) Debug.Log("[Ore What] " + text);
        Changed?.Invoke();
    }

    /// <summary>Turns an exception into a message a player understands.</summary>
    private static string Describe(Exception e, bool hosting)
    {
        Debug.LogWarning($"[Ore What] {e.GetType().Name}: {e.Message}");
        string m = (e.Message + " " + e.InnerException?.Message).ToLowerInvariant();
        switch (e)
        {
            case TimeoutException:
                return e.Message + " Check your internet connection and try again.";
            case RelayServiceException r when r.Reason == RelayExceptionReason.JoinCodeNotFound || r.Reason == RelayExceptionReason.InvalidRequest
                                          || r.Reason == RelayExceptionReason.InvalidArgument || r.Reason == RelayExceptionReason.AllocationNotFound:
                return "No game with that code. Check it, or the game may have ended.";
            case RelayServiceException r when r.Reason == RelayExceptionReason.RateLimited:
                return "Too many attempts. Wait a few seconds and try again.";
            case RelayServiceException r when r.Reason == RelayExceptionReason.NoSuitableRelay || r.Reason == RelayExceptionReason.RegionNotFound:
                return "No Relay server is available right now. Try again in a minute.";
            case AuthenticationException:
            case RequestFailedException when m.Contains("project") || m.Contains("environment") || m.Contains("forbidden") || m.Contains("unauthorized") || m.Contains("inactive"):
                return "Online services aren't available (sign-in failed, or Relay isn't enabled for this project in the Unity Dashboard).";
            case RequestFailedException:
                return (hosting ? "Couldn't create the game: " : "Couldn't join: ") + "online services didn't respond. Check your internet connection.";
            case ServicesInitializationException:
                return "Online services couldn't start. Check your internet connection.";
            default:
                return (hosting ? "Couldn't host: " : "Couldn't join: ") + e.Message;
        }
    }

    private static bool LooksLikeAddress(string code) => code.Contains(".") || code.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    private static bool HasArg(string arg)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, arg, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string LocalAddress()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
