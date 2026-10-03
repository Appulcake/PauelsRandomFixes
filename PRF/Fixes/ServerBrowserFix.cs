#if CLIENT
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking.Lobbies;
using Steamworks;

// ReSharper disable InconsistentNaming

namespace PRF.Fixes;

[Fix]
[HarmonyPatch]
internal sealed class ServerBrowserFix(ConfigFile config) : ConfigurableFix(config)
{
    private static readonly List<ServerLobbyInstance> CachedDedicatedServers = [];
    private static LobbyList? _activeLobbyList;
    private static LobbySearchFilter _lastUiFilter;
    private static bool _hasLastUiFilter;
    private static bool _refreshInProgress;
    
    protected override string Description =>
        $"{base.Description}\n" +
        "Fixes dedicated servers disappearing from server browser list when steam returns no dedicated servers. " +
        "This happens when the query gets rate limited, as every filter change (other than search by name) sends " +
        "a new query for every single server, and the game also throws out the entire list on such a refresh so when " +
        "no dedicated servers are returned (eNoServersListedOnMasterServer response), there'll be none in the list.\n\n" +
        "This fix makes all filters act on the local list without sending queries every time, and when a refresh " +
        "returns no entries, it just keeps showing the existing list it already had.";
    
    [HarmonyPatch(typeof(LobbyList), nameof(LobbyList.GetListOfLobbies))]
    [HarmonyPrefix]
    private static void LobbyListGetListOfLobbiesPrefix(LobbyList __instance)
    {
        if (_activeLobbyList == __instance)
            return;
        
        _activeLobbyList = __instance;
        _hasLastUiFilter = false;
        _refreshInProgress = false;
        CachedDedicatedServers.Clear();
    }
    
    // This is in case GetListOfLobbies() was suppressed from just a filter change, to still refresh the visible
    // local list entries
    [HarmonyPatch(typeof(LobbyList), nameof(LobbyList.GetListOfLobbies))]
    [HarmonyPostfix]
    private static void LobbyListGetListOfLobbiesPostfix(LobbyList __instance)
    {
        __instance.UpdateLobbyList();
    }
    
    [HarmonyPatch(typeof(LobbyList), nameof(LobbyList.OnDestroy))]
    [HarmonyPrefix]
    private static void LobbyListOnDestroyPrefix(LobbyList __instance)
    {
        if (_activeLobbyList != __instance)
            return;
        
        _activeLobbyList = null;
        _hasLastUiFilter = false;
        _refreshInProgress = false;
        CachedDedicatedServers.Clear();
    }
    
    // UpdateLobbyList() is normally used for text search, also make it apply other filters so this can be used
    // to filter the list locally
    [HarmonyPatch(typeof(LobbyList), nameof(LobbyList.UpdateLobbyList))]
    [HarmonyPostfix]
    private static void LobbyListUpdateLobbyListPostfix(LobbyList __instance)
    {
        foreach (var lobbyItem in from lobby in __instance.allLobbies
                 let lobbyItem = lobby.Value
                 where lobbyItem.gameObject.activeSelf && !MatchesLocalFilter(lobby.Key, __instance.activeFilter)
                 select lobbyItem)
            lobbyItem.gameObject.SetActive(false);
    }
    
    // Results usually come in async after filter changes, apply filtering to those right away
    [HarmonyPatch(typeof(LobbyList), nameof(LobbyList.OnLobbyDataUpdated))]
    [HarmonyPostfix]
    private static void LobbyListOnLobbyDataUpdatedPostfix(LobbyInstance lobby, LobbyList __instance)
    {
        if (__instance.allLobbies.TryGetValue(lobby, out var lobbyItem) && !MatchesLocalFilter(lobby, __instance.activeFilter))
            lobbyItem.gameObject.SetActive(false);
    }
    
    [HarmonyPatch(typeof(SteamLobby), nameof(SteamLobby.GetLobbiesList))]
    [HarmonyPrefix]
    private static bool SteamLobbyGetLobbiesListPrefix(ref LobbySearchFilter lobbyFilter)
    {
        if (_activeLobbyList == null)
            return true;
        
        var uiFilter = lobbyFilter;
        
        // Allow first ever query to go through too, this is when the server browser is fresh opened
        // If the filter is same as last, the filters weren't changed, so this is called from pressing Refresh button,
        // also allow that to go through
        // If filter is changed, that's because any of the filter elements were clicked/changed and that's what called
        // this, stop that from querying so those are local only
        // This is neatest way I found without invasively hooking into determining which button was pressed, since all
        // filter changes (other than server name text change) trigger this query
        
        var shouldQuerySteam = !_hasLastUiFilter || SameFilter(uiFilter, _lastUiFilter);
        _lastUiFilter = uiFilter;
        _hasLastUiFilter = true;
        if (!shouldQuerySteam)
            return false;
        
        // Cache existing dedicated servers (only if a refresh is not in progress, otherwise it gets overwritten
        // halfway with an empty list if you spam refresh button) before GetLobbiesList() rudely wipes them all
        
        if (!_refreshInProgress)
            CacheDedicatedServers();
        
        _refreshInProgress = true;
        
        // Always get full list of servers instead of only what matches the filter, so that the full list is available
        // internally to then further do local filtering on, otherwise only a subsection of servers matching the filter
        // will arrive from steam, where then changing filters would need a fresh query to get the rest
        
        lobbyFilter = BuildFullLobbyFilter(uiFilter);
        return true;
    }
    
    private static LobbySearchFilter BuildFullLobbyFilter(LobbySearchFilter uiFilter)
    {
        return new LobbySearchFilter
        {
            HideFull = false,
            HideEmpty = false,
            HidePasswordProtected = false,
            MissionPvpType = MissionPvpType.All,
            ServerType = FilterServerType.All,
            distanceFilter = null, // this means Worldwide/no distance and ping filtering
#pragma warning disable Harmony003
            ignoreVersionFilter = uiFilter.ignoreVersionFilter
#pragma warning restore Harmony003
        };
    }
    
    [HarmonyPatch(typeof(SteamLobby.ServerListRequest), nameof(SteamLobby.ServerListRequest.OnRefreshComplete))]
    [HarmonyPostfix]
    private static void ServerListRequestOnRefreshCompletePostfix(EMatchMakingServerResponse response,
        SteamLobby.ServerListRequest __instance)
    {
        var steamLobby = SteamLobby.instance;
        
        // Filter out CliJoinHandler creating temporary ServerListRequest in its JoinBySteamId()
        if (steamLobby == null || __instance != steamLobby.serverListRequest)
            return;
        
        _refreshInProgress = false;
        if (response != EMatchMakingServerResponse.eNoServersListedOnMasterServer)
            return;
        
        RestoreDedicatedServers();
    }
    
    // Only caching and re-adding dedicated servers, as player hosted lobbies seem to just always work and not
    // get rate limited, those can refresh whenever and no need to use cached versions for those
    private static void CacheDedicatedServers()
    {
        var list = _activeLobbyList;
        if (list == null)
            return;
        
        CachedDedicatedServers.Clear();
        foreach (var lobby in list.allLobbies.Keys)
            if (lobby is ServerLobbyInstance server)
                CachedDedicatedServers.Add(server);
    }
    
    private static void RestoreDedicatedServers()
    {
        var list = _activeLobbyList;
        if (list == null || CachedDedicatedServers.Count == 0)
            return;
        
        foreach (var server in CachedDedicatedServers)
        {
            // This marks server to be available to be updated by CheckPingServers()
            server.InList = true;
            list.OnLobbyDataUpdated(server);
        }
        
        list.UpdateLobbyList();
    }
    
    private static bool MatchesLocalFilter(LobbyInstance lobby, LobbySearchFilter filter)
    {
#pragma warning disable Harmony003
        if (filter.ServerType == FilterServerType.DedicatedServerOnly && !lobby.DedicatedServer) return false;
        if (filter.ServerType == FilterServerType.PlayerHostedOnly && lobby.DedicatedServer) return false;
        if (filter.HidePasswordProtected && lobby.IsPasswordProtected(out _)) return false;
        if (filter.MissionPvpType != MissionPvpType.All && lobby.MissionPvpType != filter.MissionPvpType) return false;
        if (lobby.GetPlayerCounts(out var current, out var max))
        {
            if (filter.HideEmpty && current == 0) return false;
            if (filter.HideFull && current >= max) return false;
        }
        
        // Prevent unknown ping from making the entry disappear
        var ping = lobby.CalculatePing();
        if (ping.HasValue && !filter.PingDistanceAllowed(ping.Value))
            return false;
        
        return true;
#pragma warning restore Harmony003
    }
    
    private static bool SameFilter(LobbySearchFilter a, LobbySearchFilter b)
    {
#pragma warning disable Harmony003
        return a.HideFull == b.HideFull && a.HideEmpty == b.HideEmpty &&
               a.HidePasswordProtected == b.HidePasswordProtected && a.MissionPvpType == b.MissionPvpType &&
               a.ServerType == b.ServerType && a.distanceFilter == b.distanceFilter &&
               a.ignoreVersionFilter == b.ignoreVersionFilter;
#pragma warning restore Harmony003
    }
}
#endif