using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Library.Utils;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Translates session paths exposed through <c>MyAPIGateway.Session</c>.
/// ModItem structs remain handled by the Roslyn rewriter.
/// </summary>
internal sealed class WrappedSession : IMySession
{
    private readonly IMySession _inner;

    public WrappedSession(IMySession inner)
    {
        _inner = inner;
    }

    public string CurrentPath => PathHelpers.ToWindowsPath(_inner.CurrentPath);
    public string ThumbPath   => PathHelpers.ToWindowsPath(_inner.ThumbPath);

    // The interface still requires these obsolete members.
#pragma warning disable CS0618

    public float AssemblerEfficiencyMultiplier => _inner.AssemblerEfficiencyMultiplier;
    public float AssemblerSpeedMultiplier => _inner.AssemblerSpeedMultiplier;
    public bool AutoHealing => _inner.AutoHealing;
    public uint AutoSaveInMinutes => _inner.AutoSaveInMinutes;
    public IMyCameraController CameraController => _inner.CameraController;
    public bool CargoShipsEnabled => _inner.CargoShipsEnabled;
    public bool ClientCanSave => _inner.ClientCanSave;
    public bool CreativeMode => _inner.CreativeMode;
    public IMyCamera Camera => _inner.Camera;
    public IMyPlayer LocalHumanPlayer => _inner.LocalHumanPlayer;
    public IMyWeatherEffects WeatherEffects => _inner.WeatherEffects;
    public IMyConfig Config => _inner.Config;
    public TimeSpan ElapsedPlayTime => _inner.ElapsedPlayTime;
    public bool EnableCopyPaste => _inner.EnableCopyPaste;
    public MyEnvironmentHostilityEnum EnvironmentHostility => _inner.EnvironmentHostility;
    public float GrinderSpeedMultiplier => _inner.GrinderSpeedMultiplier;
    public float HackSpeedMultiplier => _inner.HackSpeedMultiplier;
    public float InventoryMultiplier => _inner.InventoryMultiplier;
    public float CharactersInventoryMultiplier => _inner.CharactersInventoryMultiplier;
    public float BlocksInventorySizeMultiplier => _inner.BlocksInventorySizeMultiplier;
    public bool IsCameraControlledObject => _inner.IsCameraControlledObject;
    public bool IsCameraUserControlledSpectator => _inner.IsCameraUserControlledSpectator;
    public bool IsServer => _inner.IsServer;
    public short MaxFloatingObjects => _inner.MaxFloatingObjects;
    public short MaxBackupSaves => _inner.MaxBackupSaves;
    public short MaxPlayers => _inner.MaxPlayers;
    public MyOnlineModeEnum OnlineMode => _inner.OnlineMode;
    public float RefinerySpeedMultiplier => _inner.RefinerySpeedMultiplier;
    public bool ShowPlayerNamesOnHud => _inner.ShowPlayerNamesOnHud;
    public bool SurvivalMode => _inner.SurvivalMode;
    public bool ThrusterDamage => _inner.ThrusterDamage;
    public TimeSpan TimeOnBigShip => _inner.TimeOnBigShip;
    public TimeSpan TimeOnFoot => _inner.TimeOnFoot;
    public TimeSpan TimeOnJetpack => _inner.TimeOnJetpack;
    public TimeSpan TimeOnSmallShip => _inner.TimeOnSmallShip;
    public bool WeaponsEnabled => _inner.WeaponsEnabled;
    public float WelderSpeedMultiplier => _inner.WelderSpeedMultiplier;
    public ulong? WorkshopId => _inner.WorkshopId;
    public IMyVoxelMaps VoxelMaps => _inner.VoxelMaps;
    public IMyPlayer Player => _inner.Player;
    public IMyControllableEntity ControlledObject => _inner.ControlledObject;
    public MyObjectBuilder_SessionSettings SessionSettings => _inner.SessionSettings;
    public IMyFactionCollection Factions => _inner.Factions;
    public IMyDamageSystem DamageSystem => _inner.DamageSystem;
    public IMyGpsCollection GPS => _inner.GPS;
    public BoundingBoxD WorldBoundaries => _inner.WorldBoundaries;
    public MyPromoteLevel PromoteLevel => _inner.PromoteLevel;
    public bool HasCreativeRights => _inner.HasCreativeRights;
    public bool HasAdminPrivileges => _inner.HasAdminPrivileges;
    public Version Version => _inner.Version;
    public IMyOxygenProviderSystem OxygenProviderSystem => _inner.OxygenProviderSystem;
    public int GameplayFrameCounter => _inner.GameplayFrameCounter;
    public int TotalBotLimit => _inner.TotalBotLimit;

    public string Description { get => _inner.Description; set => _inner.Description = value; }
    public double CameraTargetDistance { get => _inner.CameraTargetDistance; set => _inner.CameraTargetDistance = value; }
    public DateTime GameDateTime { get => _inner.GameDateTime; set => _inner.GameDateTime = value; }
    public bool IsCameraAwaitingEntity { get => _inner.IsCameraAwaitingEntity; set => _inner.IsCameraAwaitingEntity = value; }
    public List<MyObjectBuilder_Checkpoint.ModItem> Mods { get => _inner.Mods; set => _inner.Mods = value; }
    public bool MultiplayerAlive { get => _inner.MultiplayerAlive; set => _inner.MultiplayerAlive = value; }
    public bool MultiplayerDirect { get => _inner.MultiplayerDirect; set => _inner.MultiplayerDirect = value; }
    public double MultiplayerLastMsg { get => _inner.MultiplayerLastMsg; set => _inner.MultiplayerLastMsg = value; }
    public string Name { get => _inner.Name; set => _inner.Name = value; }
    public float NegativeIntegrityTotal { get => _inner.NegativeIntegrityTotal; set => _inner.NegativeIntegrityTotal = value; }
    public string Password { get => _inner.Password; set => _inner.Password = value; }
    public float PositiveIntegrityTotal { get => _inner.PositiveIntegrityTotal; set => _inner.PositiveIntegrityTotal = value; }

    public event Action OnSessionReady
    {
        add => _inner.OnSessionReady += value;
        remove => _inner.OnSessionReady -= value;
    }

    public event Action OnSessionLoading
    {
        add => _inner.OnSessionLoading += value;
        remove => _inner.OnSessionLoading -= value;
    }

    public void BeforeStartComponents() => _inner.BeforeStartComponents();
    public void Draw() => _inner.Draw();
    public void GameOver() => _inner.GameOver();
    public void GameOver(MyStringId? customMessage) => _inner.GameOver(customMessage);
    public MyObjectBuilder_Checkpoint GetCheckpoint(string saveName) => _inner.GetCheckpoint(saveName);
    public MyObjectBuilder_Sector GetSector() => _inner.GetSector();
    public Dictionary<string, byte[]> GetVoxelMapsArray() => _inner.GetVoxelMapsArray();
    public MyObjectBuilder_World GetWorld() => _inner.GetWorld();
    public bool IsPausable() => _inner.IsPausable();

    public void RegisterComponent(MySessionComponentBase component, MyUpdateOrder updateOrder, int priority)
        => _inner.RegisterComponent(component, updateOrder, priority);

    public MySessionComponentBase GetComponentByInterfaceType<T>() => _inner.GetComponentByInterfaceType<T>();
    public bool TryGetComponentByInterfaceType<T>(out T sessionComponent) => _inner.TryGetComponentByInterfaceType(out sessionComponent);

    public bool Save(string customSaveName = null) => _inner.Save(customSaveName);

    public void SetCameraController(MyCameraControllerEnum cameraControllerEnum,
        IMyEntity cameraEntity = null, Vector3D? position = null)
        => _inner.SetCameraController(cameraControllerEnum, cameraEntity, position);

    public void SetAsNotReady() => _inner.SetAsNotReady();
    public void Unload() => _inner.Unload();
    public void UnloadDataComponents() => _inner.UnloadDataComponents();
    public void UnloadMultiplayer() => _inner.UnloadMultiplayer();
    public void UnregisterComponent(MySessionComponentBase component) => _inner.UnregisterComponent(component);
    public void Update(MyTimeSpan time) => _inner.Update(time);
    public void UpdateComponents() => _inner.UpdateComponents();

    public MyPromoteLevel GetUserPromoteLevel(ulong steamId) => _inner.GetUserPromoteLevel(steamId);
    public bool IsUserAdmin(ulong steamId) => _inner.IsUserAdmin(steamId);
    public bool IsUserPromoted(ulong steamId) => _inner.IsUserPromoted(steamId);

    public void SetComponentUpdateOrder(MySessionComponentBase component, MyUpdateOrder order)
        => _inner.SetComponentUpdateOrder(component, order);

    public bool TryGetAdminSettings(ulong steamId, out MyAdminSettingsEnum adminSettings)
        => _inner.TryGetAdminSettings(steamId, out adminSettings);

    public bool IsUserInvulnerable(ulong steamId) => _inner.IsUserInvulnerable(steamId);
    public bool IsUserShowAllPlayers(ulong steamId) => _inner.IsUserShowAllPlayers(steamId);
    public bool IsUserUseAllTerminals(ulong steamId) => _inner.IsUserUseAllTerminals(steamId);
    public bool IsUserUntargetable(ulong steamId) => _inner.IsUserUntargetable(steamId);
    public bool IsUserKeepOriginalOwnershipOnPaste(ulong steamId) => _inner.IsUserKeepOriginalOwnershipOnPaste(steamId);
    public bool IsUserIgnoreSafeZones(ulong steamId) => _inner.IsUserIgnoreSafeZones(steamId);
    public bool IsUserIgnorePCULimit(ulong steamId) => _inner.IsUserIgnorePCULimit(steamId);

#pragma warning restore CS0618
}
