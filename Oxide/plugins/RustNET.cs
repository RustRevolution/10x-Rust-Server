using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using System.Globalization;
using Facepunch;
using System.IO;

namespace Oxide.Plugins
{
    [Info("RustNET", "k1lly0u", "0.1.20", ResourceId = 0)]
    class RustNET : RustPlugin
    {
        #region Fields
        [PluginReference] Plugin Clans, Friends, ImageLibrary, LustyMap;

        public static RustNET ins;
        public static RestoreData restoreData;
        public static LinkManager linkManager;

        public static Dictionary<Colors, string> uiColors = new Dictionary<Colors, string>();

        private static int layerPlcmnt;
        private static bool isUnloading;

        private StoredData storedData;
        private DynamicConfigFile data;

        private List<TerminalManager> terminals = new List<TerminalManager>();
        private Dictionary<string, Plugin> modules = new Dictionary<string, Plugin>();
        private Dictionary<string, string> moduleIcons = new Dictionary<string, string>();

        private bool wipeDetected;
        private bool isInitialized;

        const string permUse = "rustnet.use";
        const string permPublic = "rustnet.public";

        const string burlapSack = "assets/prefabs/misc/burlap sack/generic_world.prefab";
        const int terminalId = 1523195708;

        private string dataDirectory = $"file://{Interface.Oxide.DataDirectory}{Path.DirectorySeparatorChar}RustNET{Path.DirectorySeparatorChar}Images{Path.DirectorySeparatorChar}";
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            isUnloading = false;

            lang.RegisterMessages(Messages, this);

            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permPublic, this);

            data = Interface.Oxide.DataFileSystem.GetFile("RustNET/terminals");
            restoreData = new RestoreData();
            linkManager = new LinkManager();

            layerPlcmnt = LayerMask.GetMask("Construction", "Default", "World", "Terrain");
        }

        private void OnServerInitialized()
        {
            ins = this;
            LoadData();

            if (wipeDetected)
            {
                storedData.terminals = new List<StoredData.SavedEntity>();
                SaveData();
            }

            foreach (var color in configData.Interface.UIColors)
                uiColors.Add(color.Key, UI.Color(color.Value.Color, color.Value.Alpha));

            LoadPluginImages();

            InvokeHandler.Invoke(ServerMgr.Instance, InitializeAllTerminals, 5f);
        }

        private void OnNewSave(string filename) => wipeDetected = true;

        private void OnServerSave()
        {
            if (isInitialized)
                SaveData();
        }

        private void Unload()
        {
            isUnloading = true;

            if (InvokeHandler.IsInvoking(ServerMgr.Instance, InitializeAllTerminals))
                InvokeHandler.CancelInvoke(ServerMgr.Instance, InitializeAllTerminals);

            if (isInitialized)
                SaveData();

            Interface.Call("DestroyAllLinks");
            linkManager.DestroyAllLinks();

            ItemPlacement[] placements = UnityEngine.Object.FindObjectsOfType<ItemPlacement>();
            if (placements != null)
            {
                foreach (ItemPlacement placer in placements)
                {
                    placer.CancelPlacement();
                    UnityEngine.Object.Destroy(placer);
                }
            }

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerDisconnected(player);

            ins = null;
            restoreData = null;
            linkManager = null;
            uiColors = null;
        }

        private void OnPlayerWound(BasePlayer player) => CuiHelper.DestroyUi(player, RustNET_Panel);

        private object OnPlayerDie(BasePlayer player, HitInfo info)
        {
            CuiHelper.DestroyUi(player, RustNET_Panel);
            if (player != null && info != null)
            {
                ItemPlacement placement = player.GetComponent<ItemPlacement>();
                if (placement != null)
                {
                    placement.OnPlayerDeath();
                    return null;
                }
                else
                {
                    Controller controller = linkManager.IsDummyPlayer(player);
                    if (controller != null)
                    {
                        if (!controller.isDead)
                            controller.OnPlayerDeath(info);
                        return false;
                    }
                }
            }
            return null;
        }

        private object OnNpcPlayerResume(NPCPlayerApex npcPlayer)
        {
            if (linkManager.IsDummyPlayer(npcPlayer))
                return false;
            return null;
        }

        private object CanBeTargeted(BasePlayer player, GunTrap gunTrap) => CanBeTargetedGeneric(player);

        private object CanBeTargeted(BasePlayer player, FlameTurret flameTurret) => CanBeTargetedGeneric(player);

        private object CanBeTargeted(BaseCombatEntity entity, AutoTurret turret)
        {
            if (entity == null)
                return null;

            BasePlayer player = entity as BasePlayer;
            if (player == null || !player.IsNpc)
                return null;

            Controller controller = linkManager.IsDummyPlayer(player);
            if (controller != null)
            {
                if (turret.IsAuthed(controller.player))
                    return false;
            }
            return null;
        }

        private object CanBeTargetedGeneric(BasePlayer player)
        {
            if (player == null || !player.IsNpc)
                return null;

            Controller controller = linkManager.IsDummyPlayer(player);
            if (controller != null)
            {
                if (controller.player.IsBuildingAuthed())
                    return false;
            }
            return null;
        }

        private void OnEntityKill(BaseNetworkable networkable) => linkManager.OnEntityDeath(networkable);        

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null)
                return;

            if (input.WasJustPressed(BUTTON.USE))
            {
                RaycastHit hit;
                if (Physics.SphereCast(player.eyes.position, 0.2f, Quaternion.Euler(player.serverInput.current.aimAngles) * Vector3.forward, out hit, 3f))
                {
                    TerminalManager terminal = hit.GetEntity()?.GetComponent<TerminalManager>();
                    if (terminal != null)
                    {
                        LinkManager.Link link = linkManager.GetLinkOf(terminal);
                        if (link != null)
                            OnTerminalInteraction(player, link);
                    }
                }
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            Controller controller = player.GetComponent<Controller>();
            if (controller != null)
                UnityEngine.Object.Destroy(controller);

            CuiHelper.DestroyUi(player, RustNET_Panel);
        }

        private object OnRunPlayerMetabolism(PlayerMetabolism metabolism, BaseCombatEntity entity, float delta)
        {
            if (entity.GetComponent<Controller>())
            {                
                return true;
            }
            return null;
        }
        #endregion

        #region Functions
        private void InitializeAllTerminals()
        {
            for (int i = 0; i < storedData.terminals.Count; i++)
            {
                StoredData.SavedEntity savedEntity = storedData.terminals.ElementAt(i);

                DroppedItem droppedItem = SpawnDroppedItem(terminalId, new Vector3(savedEntity.x, savedEntity.y, savedEntity.z), new Vector3(savedEntity.eX, savedEntity.eY, savedEntity.eZ));
                TerminalManager terminal = droppedItem.gameObject.AddComponent<TerminalManager>();
                terminal.SetTerminalID(savedEntity.uniqueId, savedEntity.isPublic);                
            }
            isInitialized = true;
        }

        private void InitializeNewTerminal(BasePlayer player, DroppedItem droppedItem)
        {
            BuildingManager.Building building = GetBuilding(droppedItem.transform.position, 0.5f);
            if (building != null)
            {
                if (linkManager.GetLinkOf(building) != null)
                {
                    SendReply(player, msg("Error.AlreadyRegistered", player.userID));
                    player.GiveItem(ItemManager.CreateByItemID(terminalId), BaseEntity.GiveItemReason.PickedUp);
                    return;
                }
            }
            TerminalManager terminal = SpawnDroppedItem(terminalId, droppedItem.transform.position, droppedItem.transform.eulerAngles).gameObject.AddComponent<TerminalManager>();
            terminal.SetTerminalID(-1, false);
        }

        private void InitializeNewTerminalPublic(BasePlayer player, DroppedItem droppedItem)
        {
            TerminalManager terminal = SpawnDroppedItem(terminalId, droppedItem.transform.position, droppedItem.transform.eulerAngles).gameObject.AddComponent<TerminalManager>();
            terminal.SetTerminalID(-1, true);
        }

        private void OnTerminalInteraction(BasePlayer player, LinkManager.Link link)
        {
            object success = Interface.CallHook("CanUseTerminal", player);
            if (success != null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                SendReply(player, msg("Warning.NoPermission", player.userID));
                return;
            }

            if (!link.IsPublicLink && (configData.Interface.RequirePrivilege && !player.IsBuildingAuthed()))
            {
                SendReply(player, msg("Warning.AccessPrivilege", player.userID));
                return;
            }

            if (modules.Count == 0)
            {
                SendReply(player, msg("Warning.NoChildPlugins", player.userID));
                return;
            }

            DisplayToPlayer(player, link.terminal.terminalId, string.Empty);
        }

        private T ParseType<T>(string type)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), type, true);
            }
            catch
            {
                return default(T);
            }
        }

        private string GetHelpString(ulong playerId, bool title)
        {
            if (title)
                return msg("UI.Help.Title", playerId);
            else
            {
                string message = msg("UI.Help", playerId);
                if (permission.UserHasPermission(playerId.ToString(), permPublic))
                    message += msg("UI.Help.Public", playerId);
                return message;
            }
        }
        #endregion

        #region Universal Functions
        public static void RegisterModule(string title, Plugin plugin)
        {
            if (ins == null)
                return;

            if (!ins.modules.ContainsKey(title))
                ins.modules.Add(title, plugin);
        }

        public static void UnregisterModule(string title)
        {
            if (ins.modules.ContainsKey(title))
                ins.modules.Remove(title);
        }

        public static DroppedItem SpawnDroppedItem(int itemId, Vector3 position, Vector3 rotation)
        {
            Item item = ItemManager.CreateByItemID(itemId);

            BaseEntity worldEntity = GameManager.server.CreateEntity(burlapSack, position, Quaternion.Euler(rotation));
            WorldItem worldItem = worldEntity as WorldItem;
            if (worldItem != null)
                worldItem.InitializeItem(item);

            worldItem.enableSaving = false;
            worldEntity.Spawn();
            item.SetWorldEntity(worldEntity);

            UnityEngine.Object.Destroy(worldEntity.GetComponent<Rigidbody>());
            UnityEngine.Object.Destroy(worldEntity.GetComponent<EntityCollisionMessage>());
            UnityEngine.Object.Destroy(worldEntity.GetComponent<PhysicsEffects>());

            DroppedItem droppedItem = worldEntity.GetComponent<DroppedItem>();
            droppedItem.allowPickup = false;
            droppedItem.CancelInvoke(droppedItem.IdleDestroy);

            return droppedItem;
        }

        public static BuildingManager.Building GetBuilding(Vector3 position, float radius)
        {
            BuildingManager.Building building = null;

            List<BuildingBlock> list = Facepunch.Pool.GetList<BuildingBlock>();
            Vis.Entities<BuildingBlock>(position, radius, list, 2097152, QueryTriggerInteraction.Collide);

            if (list.Count > 0)
                building = list[0].GetBuilding();

            Facepunch.Pool.FreeList<BuildingBlock>(ref list);
            return building;
        }

        public static BuildingManager.Building GetBuilding(BaseEntity entity) => GetBuilding(entity.WorldSpaceBounds().position, entity.WorldSpaceBounds().extents.magnitude + 0.5f);

        public static BuildingManager.Building GetBuilding(BaseEntity entity, out BuildingBlock parentEntity)
        {
            BuildingManager.Building building = null;

            OBB obb = entity.WorldSpaceBounds();
            List<BuildingBlock> list = Facepunch.Pool.GetList<BuildingBlock>();
            Vis.Entities<BuildingBlock>(obb.position, 0.5f + obb.extents.magnitude, list, 2097152, QueryTriggerInteraction.Collide);

            if (list.Count > 0)
            {
                parentEntity = list[0];
                building = parentEntity.GetBuilding();
            }
            else parentEntity = null;

            Facepunch.Pool.FreeList<BuildingBlock>(ref list);

            return building;
        }

        public static BaseEntity FindEntityFromRay(BasePlayer player)
        {
            Ray ray = new Ray(player.eyes.position, Quaternion.Euler(player.serverInput.current.aimAngles) * Vector3.forward);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, ins.configData.Placement.MaxDistance))
                return null;

            var hitEnt = hit.collider.GetComponentInParent<BaseEntity>();
            if (hitEnt != null)
                return hitEnt;
            return null;
        }

        public static void OpenInventory(BasePlayer player, BaseEntity entity, ItemContainer container)
        {
            player.inventory.loot.Clear();
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.entitySource = entity;
            player.inventory.loot.itemSource = null;
            player.inventory.loot.AddContainer(container);
            player.inventory.loot.SendImmediate();
            player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "generic");
            player.SendNetworkUpdate();
        }

        public static void StripInventory(BasePlayer player)
        {
            Item[] allItems = player.inventory.AllItems();

            for (int i = allItems.Length - 1; i >= 0; i--)
            {
                Item item = allItems[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }
        #endregion

        #region Link Manager
        public class LinkManager
        {
            public List<Link> links = new List<Link>();

            public Link GetLinkOf(int terminalId) => links.Find(x => x.terminal.terminalId == terminalId) ?? null;

            public Link GetLinkOf(TerminalManager terminal) => links.Find(x => x.terminal == terminal) ?? null;

            public Link GetLinkOf(BuildingManager.Building building) => links.Find(x => x.terminal.parentEntity?.GetBuilding() == building) ?? null;

            public DroppedItem GetTerminalOf(int terminalId) => links.Find(x => x.terminal.terminalId == terminalId)?.terminal.droppedItem ?? null;

            public Controller IsDummyPlayer(BasePlayer player)
            {
                foreach (Link link in links)
                {
                    foreach (Controller controller in link.controllers)
                    {
                        if (controller.dummyPlayer == player)
                            return controller;
                    }
                }
                return null;
            }

            public bool IsValidTerminal(int terminalId) => GetLinkOf(terminalId) != null;

            public void AddNewLink(TerminalManager terminal)
            {
                if (GetLinkOf(terminal) == null)
                    links.Add(new Link(terminal));
            }

            public void OnEntityDeath(BaseNetworkable networkable)
            {
                for (int i = links.Count - 1; i >= 0; i--)
                    links.ElementAt(i).OnEntityDeath(networkable);
            }

            public void DestroyAllLinks()
            {
                foreach (Link link in links)
                    link.DestroyLink();
            }

            public class Link
            {
                public List<Controller> controllers { get; private set; }
                public TerminalManager terminal { get; private set; }

                public Link() { }
                public Link(TerminalManager terminal)
                {
                    controllers = new List<Controller>();
                    this.terminal = terminal;
                    Interface.Call("OnTerminalCreated", terminal);
                }

                public bool IsPublicLink
                {
                    get
                    {
                        return terminal.isPublic;
                    }
                }

                public void OpenLink(Controller controller)
                {
                    if (!controllers.Contains(controller))
                    {
                        controllers.Add(controller);
                        ins.LustyMap?.Call("DisableMaps", controller.player);
                        controller.player.DismountObject();
                        controller.player.EnsureDismounted();
                    }
                }

                public void CloseLink(Controller controller)
                {
                    if (controllers.Contains(controller))
                    {
                        ins.LustyMap?.Call("EnableMaps", controller.player);
                        controllers.Remove(controller);
                    }
                }

                public void OnEntityDeath(BaseNetworkable networkable)
                {
                    if (networkable is DroppedItem)
                    {
                        TerminalManager terminal = networkable.GetComponent<TerminalManager>();
                        if (terminal != null && terminal == this.terminal)
                            DestroyLink(false);
                    }
                    else if (networkable is BuildingBlock)
                    {
                        if ((networkable as BuildingBlock) == terminal.parentEntity)
                        {
                            DestroyLink(false);
                        }
                    }
                }

                public void DestroyLink(bool shutdown = true)
                {
                    if (!shutdown)
                    {
                        Interface.Call("OnLinkDestroyed", terminal.terminalId);
                        linkManager.links.Remove(this);
                    }
                    else Interface.Call("OnLinkShutdown", terminal.terminalId);
                    UnityEngine.Object.Destroy(terminal);
                }
            }
        }

        public class Controller : MonoBehaviour
        {
            public BasePlayer player { get; private set; }
            public BasePlayer dummyPlayer { get; private set; }
            private LinkManager.Link baseLink;
            public bool isDead;

            public virtual void Awake()
            {
                player = GetComponent<BasePlayer>();
                enabled = false;
            }

            public virtual void OnDestroy()
            {
                if (!isDead)
                {
                    DestroyDummyPlayer();
                    restoreData.RestorePlayer(player);
                }
                baseLink?.CloseLink(this);
            }

            private void CreateDummyPlayer()
            {
                dummyPlayer = (BasePlayer)GameManager.server.CreateEntity("assets/prefabs/npc/scientist/scientist.prefab", player.transform.position, player.transform.rotation);
                dummyPlayer.displayName = player.displayName;
                dummyPlayer.enableSaving = false;
                dummyPlayer.GetComponent<NPCPlayerApex>().NewAI = false;
                dummyPlayer.Spawn();

                NPCPlayerApex _npcPlayer = dummyPlayer.GetComponent<NPCPlayerApex>();
                if (_npcPlayer != null)
                {
                    _npcPlayer.IsDormant = true;
                    _npcPlayer.StopMoving();
                    _npcPlayer.Pause();

                    _npcPlayer.CancelInvoke(_npcPlayer.EquipTest);
                    _npcPlayer.CancelInvoke(_npcPlayer.ServerThink_Internal);
                    _npcPlayer.CancelInvoke(_npcPlayer.RadioChatter);

                    _npcPlayer.GetComponent<Scientist>().LootSpawnSlots = new LootContainer.LootSpawnSlot[0];

                    _npcPlayer.enabled = false;
                    _npcPlayer.GetNavAgent.enabled = false;
                    _npcPlayer.GetComponent<Rust.Ai.AiManagedAgent>().enabled = false;
                    _npcPlayer.GetComponent<Apex.AI.Components.UtilityAIComponent>().enabled = false;
                }                

                StripInventory(dummyPlayer);
                restoreData.CloneToPlayer(player.userID, dummyPlayer);
            }

            private void DestroyDummyPlayer()
            {
                if (dummyPlayer == null)
                    return;

                StripInventory(dummyPlayer);
                ins.NextTick(() =>
                {
                    if (dummyPlayer != null && !dummyPlayer.IsDestroyed)
                        dummyPlayer.Kill();
                });
            }

            public void InitiateLink(int terminalId)
            {
                baseLink = linkManager.GetLinkOf(terminalId);
                baseLink.OpenLink(this);
                restoreData.AddData(player);
                CreateDummyPlayer();

                player.metabolism.bleeding.value = 0;
                player.metabolism.poison.value = 0;
                player.metabolism.radiation_level.value = 0;
                player.metabolism.radiation_poison.value = 0;
                player.metabolism.wetness.value = 0;
            }

            public virtual void OnPlayerDeath(HitInfo info)
            {
                isDead = true;
                DestroyDummyPlayer();
                restoreData.RestorePlayer(player);
                InvokeHandler.Invoke(this, () =>
                {
                    Destroy(this);
                    player.Die(new HitInfo(info.Initiator, player, info.damageTypes.GetMajorityDamageType(), info.damageTypes.Total()));
                }, 1f);
            }
        }
        #endregion

        #region Terminal Manager
        public class TerminalManager : MonoBehaviour
        {
            public DroppedItem droppedItem { get; private set; }
            public BuildingBlock parentEntity { get; private set; }
            public int terminalId { get; private set; }
            public bool isPublic;

            private void Awake()
            {
                droppedItem = GetComponent<DroppedItem>();
                enabled = false;

                OBB obb = droppedItem.WorldSpaceBounds();
                List<BuildingBlock> list = Pool.GetList<BuildingBlock>();
                Vis.Entities<BuildingBlock>(obb.position, 0.5f + obb.extents.magnitude, list, 2097152, QueryTriggerInteraction.Collide);

                if (list.Count > 0)
                    parentEntity = list[0];

                Pool.FreeList<BuildingBlock>(ref list);
            }

            public void SetTerminalID(int terminalId = -1, bool isPublic = false)
            {
                this.isPublic = isPublic;
                if (!isPublic)
                {
                    if (parentEntity == null)
                    {
                        Destroy(this);
                        return;
                    }
                }
                else parentEntity = null;                

                if (terminalId == -1)
                    this.terminalId = GenerateID();
                else this.terminalId = terminalId;

                linkManager.AddNewLink(this);
                ins.terminals.Add(this);
            }

            private int GenerateID()
            {
                int randomId = UnityEngine.Random.Range(1000, 9999);
                if (ins.terminals.Exists(x => x.terminalId == randomId))
                    return GenerateID();
                return randomId;
            }

            private void OnDestroy()
            {
                if (droppedItem != null)
                {
                    droppedItem.DestroyItem();
                    if (!droppedItem.IsDestroyed)
                        droppedItem.Kill();
                    droppedItem = null;
                }

                if (!isUnloading)
                    ins.terminals.Remove(this);
            }
        }
        #endregion

        #region Item Placement
        public class ItemPlacement : MonoBehaviour
        {
            private BasePlayer player;
            public DroppedItem droppedItem;
            private BuildingBlock hitEntity = null;
            private bool isValidPlacement;

            private Action<BasePlayer, DroppedItem> callback;
            private Action<BasePlayer, DroppedItem, int> callbackTerminal;

            private int placingItemId;
            public int terminalId;

            private float placementDistance;
            private Vector3 rotation = Vector3.zero;
            private Vector3 offset = Vector3.zero;
            private bool rotateEyes;
            private bool ignoreRestriction;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                enabled = false;

                placementDistance = ins.configData.Placement.MaxDistance;
            }

            private void FixedUpdate()
            {
                Item activeItem = player.GetActiveItem();
                if (activeItem == null || activeItem.info.itemid != placingItemId)
                    CancelPlacement();

                isValidPlacement = false;

                InputState input = player.serverInput;
                Vector3 eyePosition = player.transform.position + (player.modelState.ducked ? Vector3.up * 0.7f : Vector3.up * 1.5f);

                RaycastHit hit;
                if (Physics.Raycast(new Ray(player.transform.position + (player.modelState.ducked ? Vector3.up * 0.7f : Vector3.up * 1.5f), Quaternion.Euler(input.current.aimAngles) * Vector3.forward), out hit, placementDistance, layerPlcmnt))
                {
                    droppedItem.transform.position = hit.point + ((droppedItem.transform.forward * offset.x) + (droppedItem.transform.up * offset.y) + (droppedItem.transform.right * offset.z));
                    isValidPlacement = hitEntity = hit.GetEntity()?.GetComponent<BuildingBlock>();
                }
                else
                {
                    droppedItem.transform.position = new Ray(eyePosition, Quaternion.Euler(input.current.aimAngles) * Vector3.forward).GetPoint(2);
                    isValidPlacement = false;
                }
                Quaternion lookRotation = Quaternion.LookRotation(eyePosition - droppedItem.transform.position, Vector3.up);
                droppedItem.transform.rotation = rotateEyes ? lookRotation * Quaternion.Euler(rotation) : Quaternion.Euler(droppedItem.transform.eulerAngles.x, lookRotation.eulerAngles.y, droppedItem.transform.eulerAngles.z) * Quaternion.Euler(rotation);

                if (input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                {
                    if (!ignoreRestriction && !isValidPlacement)
                    {
                        player.ChatMessage(string.Format(msg("Placement.RequireParent", player.userID), droppedItem.item.info.displayName.english));
                        return;
                    }
                    else PlaceItem();
                }
                else if (input.WasJustPressed(BUTTON.FIRE_SECONDARY))
                    CancelPlacement();
            }

            public void SetRequiredItem(int placingItemId, Vector3 rotation, Vector3 offset, bool rotateEyes, bool ignoreRestriction, Action<BasePlayer, DroppedItem> callback, Action<BasePlayer, DroppedItem, int> callbackTerminal)
            {
                this.placingItemId = placingItemId;
                this.callback = callback;
                this.callbackTerminal = callbackTerminal;
                this.rotateEyes = rotateEyes;
                this.ignoreRestriction = ignoreRestriction;
                this.rotation = rotation;
                this.offset = offset;
                SpawnWorldItem();
                player.ChatMessage(string.Format(msg("Placement.PlaceItem", player.userID), droppedItem.item.info.displayName.english));
            }

            private void SpawnWorldItem()
            {
                droppedItem = SpawnDroppedItem(placingItemId, player.transform.position, new Vector3());
                enabled = true;
            }

            public void CancelPlacement()
            {
                enabled = false;
                player.ChatMessage(string.Format(msg("Placement.Cancelled", player.userID), droppedItem.item.info.displayName.english));
                droppedItem.DestroyItem();
                droppedItem.Kill();
                Destroy(this);
            }

            public void PlaceItem()
            {
                enabled = false;
                player.ChatMessage(string.Format(msg("Placement.Success", player.userID), droppedItem.item.info.displayName.english));
                player.inventory.containerBelt.Take(null, placingItemId, 1);
                callback?.Invoke(player, droppedItem);
                callbackTerminal?.Invoke(player, droppedItem, terminalId);
                droppedItem.DestroyItem();
                droppedItem.Kill();
                Destroy(this);
            }

            public void OnPlayerDeath()
            {
                CancelPlacement();
                Destroy(this);
            }
        }
        #endregion

        #region Teleportation
        public static void MovePosition(BasePlayer player, Vector3 destination, bool sleep)
        {
            if (sleep)
            {
                if (player.net?.connection != null)
                    player.ClientRPCPlayer(null, player, "StartLoading");
                StartSleeping(player);
                player.MovePosition(destination);
                if (player.net?.connection != null)
                    player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                if (player.net?.connection != null)
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.UpdateNetworkGroup();
                player.SendNetworkUpdateImmediate(false);
                if (player.net?.connection == null) return;
                try { player.ClearEntityQueue(null); } catch { }
                player.SendFullSnapshot();
            }
            else
            {
                player.MovePosition(destination);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                player.SendNetworkUpdateImmediate();
                try { player.ClearEntityQueue(null); } catch { }
            }
        }

        private static void StartSleeping(BasePlayer player)
        {
            if (player.IsSleeping())
                return;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
            if (!BasePlayer.sleepingPlayerList.Contains(player))
                BasePlayer.sleepingPlayerList.Add(player);
            player.CancelInvoke("InventoryUpdate");
        }
        #endregion

        #region Friends
        public static bool IsFriendlyPlayer(ulong playerId, ulong friendId)
        {
            if (playerId == friendId || IsFriend(playerId, friendId) || IsClanmate(playerId, friendId))
                return true;
            return false;
        }

        private static bool IsClanmate(ulong playerId, ulong friendId)
        {
            if (!ins.Clans) return false;
            object playerTag = ins.Clans?.Call("GetClanOf", playerId);
            object friendTag = ins.Clans?.Call("GetClanOf", friendId);
            if ((playerTag is string && !string.IsNullOrEmpty((string)playerTag)) && (friendTag is string && !string.IsNullOrEmpty((string)friendTag)))
                if (playerTag == friendTag) return true;
            return false;
        }

        private static bool IsFriend(ulong playerID, ulong friendID)
        {
            if (!ins.Friends) return false;
            return (bool)ins.Friends?.Call("AreFriends", playerID, friendID);
        }
        #endregion

        #region UI
        public const string RustNET_Panel = "RustNET_Panel";

        static public class UI
        {
            static public CuiElementContainer Container(string color, string aMin, string aMax, bool useCursor = false, string parent = "Overlay", string panel = RustNET_Panel)
            {
                var NewElement = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = {Color = color},
                            RectTransform = {AnchorMin = aMin, AnchorMax = aMax},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
                return NewElement;
            }

            static public void Panel(ref CuiElementContainer container, string color, string aMin, string aMax, string panel = RustNET_Panel)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax }
                },
                panel);
            }

            static public void Label(ref CuiElementContainer container, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter, string panel = RustNET_Panel)
            {
                container.Add(new CuiLabel
                {
                    Text = { FontSize = size, Align = align, Text = text, Font = "droidsansmono.ttf" },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax }

                },
                panel);
            }

            static public void Button(ref CuiElementContainer container, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, string panel = RustNET_Panel)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                    Text = { Text = text, FontSize = size, Align = align, Font = "droidsansmono.ttf" }
                },
                panel);
            }
            static public void Image(ref CuiElementContainer container, string png, string aMin, string aMax, string panel = RustNET_Panel)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent {Png = png },
                        new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax }
                    }
                });
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                    hexColor = hexColor.Substring(1);
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion

        #region UI Generation
        public enum Colors { Background, Panel, Button, Selected }
       
        public void DisplayToPlayer(BasePlayer player, int terminalId, string pluginTitle, int page = 0)
        {
            if (!linkManager.IsValidTerminal(terminalId))
            {
                CuiHelper.DestroyUi(player, RustNET_Panel);
                SendReply(player, msg("Warning.TerminalDestroyed", player.userID));
                return;
            }

            if (string.IsNullOrEmpty(pluginTitle))
                CreateSelectionPage(player, terminalId);
            else modules[pluginTitle].Call("CreateConsoleWindow", player, terminalId, page);
        }

        public CuiElementContainer GetBaseContainer(BasePlayer player, int terminalId, string pluginTitle = "", bool showHelp = true)
        {
            CuiElementContainer container = UI.Container(uiColors[Colors.Background], "0.15 0.15", "0.85 0.95", true);

            UI.Panel(ref container, uiColors[Colors.Panel], "0 1", "0.9985 1.035");
            UI.Button(ref container, uiColors[Colors.Button], "X", 10, "0.97 1.005", "0.99 1.03", "rustnet.close");
            if (showHelp)
                UI.Button(ref container, uiColors[Colors.Button], msg("UI.HelpButton", player.userID), 10, "0.86 1.005", "0.96 1.03", $"rustnet.help {terminalId} {pluginTitle}");
            UI.Label(ref container, string.Format(msg("UI.Title", player.userID), Version), 12, "0.05 1", "0.8 1.035", TextAnchor.MiddleLeft);
            UI.Image(ref container, GetImage("TerminalOverlay"), "0 0", "1 1");

            bool isPublicTerminal = false;
            LinkManager.Link link = linkManager.GetLinkOf(terminalId);
            if (link != null)
                isPublicTerminal = link.IsPublicLink;

            UI.Label(ref container, terminalId == 0 ? msg("UI.TerminalID.Help", player.userID) : isPublicTerminal ? string.Format(msg("UI.TerminalID.Public", player.userID), terminalId.ToString()) :string.Format(msg("UI.TerminalID", player.userID), terminalId.ToString()), 12, "0.05 0.94", "0.8 0.98", TextAnchor.MiddleLeft);
            UI.Label(ref container, msg("UI.Logon", player.userID), 12, "0.05 0.9", "0.8 0.94", TextAnchor.MiddleLeft);
            UI.Label(ref container, msg("UI.Password", player.userID), 12, "0.05 0.86", "0.8 0.9", TextAnchor.MiddleLeft);

            return container;
        }

        private void CreateSelectionPage(BasePlayer player, int terminalId, int page = 0)
        {
            CuiElementContainer container = GetBaseContainer(player, terminalId);
            UI.Panel(ref container, uiColors[Colors.Panel], "0.04 0.765", "0.96 0.8");
            UI.Label(ref container, msg("UI.MakeSelection", player.userID), 12, "0.05 0.765", "0.96 0.8", TextAnchor.MiddleLeft);

            int count = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                LinkManager.Link link = linkManager.GetLinkOf(terminalId);
                if (link != null && link.IsPublicLink)
                {
                    if (!(bool)modules.ElementAt(i).Value.Call("AllowPublicAccess"))
                        continue;
                }
                string pluginTitle = modules.ElementAt(i).Key;
                float[] position = GetButtonPosition(count);

                UI.Image(ref container, GetImage(pluginTitle), $"{ position[0]} {position[1]}", $"{position[2]} {position[3]}");
                UI.Button(ref container, "0 0 0 0", "", 0, $"{position[0]} {position[1]}", $"{position[2]} {position[3]}", $"rustnet.changepage {terminalId} {pluginTitle} 0");

                count++;
            }

            CuiHelper.DestroyUi(player, RustNET_Panel);
            CuiHelper.AddUi(player, container);
        }

        private float[] GetButtonPosition(int i)
        {
            float offsetX = 0;
            float offsetY = 0;

            if (i >= 0 && i < 5)
            {
                offsetX = (0.0416f + 0.15f) * i;
                offsetY = 0.5f;
            }
            if (i > 4 && i < 10)
            {
                offsetX = (0.0416f + 0.15f) * (i - 5);
                offsetY = 0.2f;
            }

            return new float[] { 0.0416f + offsetX, offsetY, 0.0416f + offsetX + 0.15f, offsetY + 0.22f };
        }

        private void DisplayHelpMenu(BasePlayer player, int terminalId, string pluginTitle = "")
        {
            CuiElementContainer container = GetBaseContainer(player, terminalId, string.Empty, false);

            UI.Panel(ref container, uiColors[Colors.Panel], "0.04 0.765", "0.96 0.8");
            UI.Label(ref container, string.IsNullOrEmpty(pluginTitle) ? GetHelpString(player.userID, true) : (string)modules[pluginTitle].Call("GetHelpString", player.userID, true), 12, "0.05 0.765", "0.8 0.8", TextAnchor.MiddleLeft);

            if (terminalId != 0)
                UI.Button(ref container, uiColors[Colors.Button], msg("UI.Return", player.userID), 11, "0.82 0.765", "0.96 0.8", $"rustnet.changepage {terminalId} {pluginTitle}");

            UI.Label(ref container, string.IsNullOrEmpty(pluginTitle) ? GetHelpString(player.userID, false) : (string)modules[pluginTitle].Call("GetHelpString", player.userID, false), 11, "0.05 0.02", "0.96 0.745", TextAnchor.UpperLeft);

            CuiHelper.DestroyUi(player, RustNET_Panel);
            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region UI Commands 
        [ConsoleCommand("rustnet.changepage")]
        private void ccmdChangePage(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            string title = arg.Args.Length >= 2 ? arg.GetString(1) : string.Empty;
            int page = arg.Args.Length >= 3 ? arg.GetInt(2) : 0; ;
            DisplayToPlayer(player, arg.GetInt(0), title, page);
        }
        
        [ConsoleCommand("rustnet.help")]
        private void ccmdHelp(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            DisplayHelpMenu(player, arg.GetInt(0), arg.Args.Length == 2 ? arg.GetString(1) : string.Empty);
        }

        [ConsoleCommand("rustnet.close")]
        private void ccmdCloseUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, RustNET_Panel);
        }
        #endregion

        #region Image Storage
        private Dictionary<string, string> queuedImages = new Dictionary<string, string>();
        private void LoadPluginImages(int attempts = 0)
        {
            if (attempts > 3)
            {
                PrintError("ImageLibrary not found. Unable to load terminal overlay image");
                return;
            }

            if (ImageLibrary == null)
            {
                timer.In(5, () => LoadPluginImages(++attempts));
                return;
            }

            AddImage("TerminalOverlay", configData.Interface.TerminalOverlay);

            foreach (var image in queuedImages)
                AddImage(image.Key, image.Value);
        }

        public void AddImage(string imageName, string fileName)
        {
            if (ImageLibrary == null)
                queuedImages.Add($"RustNET.{imageName}", fileName);
            else
            {
                if ((bool)ImageLibrary.Call("HasImage", $"RustNET.{imageName}", 0UL))
                    return;
                ImageLibrary.Call("AddImage", fileName.StartsWith("www") || fileName.StartsWith("http") ? fileName : dataDirectory + fileName, $"RustNET.{imageName}", 0UL);
            }
        }

        public string GetImage(string imageName)
        {
            if (!moduleIcons.ContainsKey(imageName))
                moduleIcons.Add(imageName, (string)ImageLibrary?.Call("GetImage", $"RustNET.{imageName}"));
            return moduleIcons[imageName];
        }
        #endregion

        #region Chat Commands
        [ChatCommand("rustnet")]
        private void cmdRustNET(BasePlayer player, string command, string[] args) => DisplayHelpMenu(player, 0, string.Empty);        

        [ChatCommand("terminal")]
        private void cmdTerminal(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                SendReply(player, $"<color=#ce422b>{Title}  <color=#939393>v</color>{Version}</color>");
                SendReply(player, msg("Help.Main", player.userID));
                SendReply(player, msg("Help.Add", player.userID));
                if (permission.UserHasPermission(player.UserIDString, permPublic))
                    SendReply(player, msg("Help.AddPublic", player.userID));
                SendReply(player, msg("Help.Remove", player.userID));
                return;
            }

            if (player.GetComponent<ItemPlacement>())
            {
                SendReply(player, msg("Warning.ToolActivated", player.userID));
                return;
            }

            if (configData.Interface.RequirePrivilege && !player.IsBuildingAuthed() && !player.IsAdmin)
            {
                SendReply(player, msg("Error.NoPrivilege", player.userID));
                return;
            }

            switch (args[0].ToLower())
            {
                case "add":
                    Item activeItem = player.GetActiveItem();
                    if (activeItem == null || activeItem.info.itemid != terminalId)
                    {
                        SendReply(player, msg("Error.HoldComputer", player.userID));
                        return;
                    }

                    bool ignoreRestriction = permission.UserHasPermission(player.UserIDString, permPublic) && (args.Length > 1 && args[1].ToLower() == "public");

                    ItemPlacement placement = player.gameObject.AddComponent<ItemPlacement>();
                    if (ignoreRestriction)
                        placement.SetRequiredItem(terminalId, new Vector3(0, 180, 0), new Vector3(-0.3f, 0, 0), false, true, InitializeNewTerminalPublic, null);
                    else placement.SetRequiredItem(terminalId, new Vector3(0, 180, 0), new Vector3(-0.3f, 0, 0), false, false, InitializeNewTerminal, null);
                    return;
                case "remove":
                    TerminalManager terminal = FindEntityFromRay(player)?.GetComponent<TerminalManager>();
                    if (terminal == null)
                    {
                        SendReply(player, msg("Error.NoEntity", player.userID));
                        return;
                    }

                    if (terminal.parentEntity == null)
                    {
                        if (!permission.UserHasPermission(player.UserIDString, permPublic))
                        {
                            SendReply(player, msg("Error.PublicLink", player.userID));
                            return;
                        }
                    }

                    Interface.Call("OnTerminalRemoved", terminal.terminalId);

                    LinkManager.Link link = linkManager.GetLinkOf(terminal);
                    if (link != null)
                        link.DestroyLink(false);

                    player.GiveItem(ItemManager.CreateByItemID(terminalId), BaseEntity.GiveItemReason.PickedUp);
                    SendReply(player, msg("Success.Remove", player.userID));
                    return;
                default:
                    break;
            }
        }
        #endregion        

        #region Config        
        private ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Item Placement Options")]
            public PlacementOptions Placement { get; set; }
            [JsonProperty(PropertyName = "RustNET Interface Options")]
            public InterfaceOptions Interface { get; set; }

            public class PlacementOptions
            {
                [JsonProperty(PropertyName = "Item placement maximum distance from player")]
                public float MaxDistance { get; set; }
            }

            public class InterfaceOptions
            {
                [JsonProperty(PropertyName = "Require building privilege to access the interface")]
                public bool RequirePrivilege { get; set; }
                [JsonProperty(PropertyName = "Terminal overlay image URL")]
                public string TerminalOverlay { get; set; }
                [JsonProperty(PropertyName = "UI Panel Colors")]
                public Dictionary<Colors, UIColor> UIColors { get; set; }

                public class UIColor
                {
                    [JsonProperty(PropertyName = "Color (hex)")]
                    public string Color { get; set; }
                    [JsonProperty(PropertyName = "Alpha (0.0 - 1.0)")]
                    public float Alpha { get; set; }
                }
            }            
            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Placement = new ConfigData.PlacementOptions
                {
                    MaxDistance = 4f
                },
                Interface = new ConfigData.InterfaceOptions
                {
                    RequirePrivilege = true,
                    TerminalOverlay = "http://www.chaoscode.io/oxide/Images/RustNET/terminal.png",
                    UIColors = new Dictionary<Colors, ConfigData.InterfaceOptions.UIColor>
                    {
                        [Colors.Background] = new ConfigData.InterfaceOptions.UIColor
                        {
                            Alpha = 0.98f,
                            Color = "#1c2c24"
                        },
                        [Colors.Panel] = new ConfigData.InterfaceOptions.UIColor
                        {
                            Alpha = 1f,
                            Color = "#13221b"
                        },
                        [Colors.Button] = new ConfigData.InterfaceOptions.UIColor
                        {
                            Alpha = 0.9f,
                            Color = "#214633"
                        },
                        [Colors.Selected] = new ConfigData.InterfaceOptions.UIColor
                        {
                            Alpha = 0.9f,
                            Color = "#387857"
                        }
                    }
                },               
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();
            
            configData.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Data Management
        private void SaveData()
        {
            storedData.terminals = new List<StoredData.SavedEntity>();
            foreach (TerminalManager terminal in terminals)
                storedData.terminals.Add(new StoredData.SavedEntity(terminal));

            data.WriteObject(storedData);
        }

        private void LoadData()
        {
            try
            {
                storedData = data.ReadObject<StoredData>();
            }
            catch
            {
                storedData = new StoredData();
            }
        }

        public class StoredData
        {
            public List<SavedEntity> terminals = new List<SavedEntity>();

            public class SavedEntity
            {
                public int uniqueId;
                public float x, y, z;
                public float eX, eY, eZ;
                public bool isPublic;

                public SavedEntity() { }
                public SavedEntity(TerminalManager terminal)
                {
                    uniqueId = terminal.terminalId;
                    x = terminal.droppedItem.transform.position.x;
                    y = terminal.droppedItem.transform.position.y;
                    z = terminal.droppedItem.transform.position.z;
                    eX = terminal.droppedItem.transform.eulerAngles.x;
                    eY = terminal.droppedItem.transform.eulerAngles.y;
                    eZ = terminal.droppedItem.transform.eulerAngles.z;
                    isPublic = terminal.isPublic;
                }
            }
        }

        public class RestoreData
        {
            public Hash<ulong, PlayerData> restoreData = new Hash<ulong, PlayerData>();

            public void AddData(BasePlayer player)
            {
                restoreData[player.userID] = new PlayerData(player);
            }

            public void RemoveData(ulong playerId)
            {
                if (HasRestoreData(playerId))
                    restoreData.Remove(playerId);
            }

            public bool HasRestoreData(ulong playerId) => restoreData.ContainsKey(playerId);

            public void RestorePlayer(BasePlayer player)
            {
                PlayerData playerData;
                if (restoreData.TryGetValue(player.userID, out playerData))
                {
                    if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
                    {
                        ins.timer.Once(1, () => RestorePlayer(player));
                        return;
                    }

                    playerData.SetStats(player);
                    MovePosition(player, playerData.position, true);
                    RestoreAllItems(player, playerData);
                }
            }

            public void CloneToPlayer(ulong playerId, BasePlayer player)
            {
                PlayerData playerData;
                if (restoreData.TryGetValue(playerId, out playerData))                
                    RestoreItems(player, playerData.containerWear, "wear");                
            }

            private void RestoreAllItems(BasePlayer player, PlayerData playerData)
            {
                if (player == null || !player.IsConnected)
                    return;

                StripInventory(player);

                if (RestoreItems(player, playerData.containerBelt, "belt") && RestoreItems(player, playerData.containerWear, "wear") && RestoreItems(player, playerData.containerMain, "main"))
                    RemoveData(player.userID);
            }

            private bool RestoreItems(BasePlayer player, ItemData[] itemData, string type)
            {
                ItemContainer container = type == "belt" ? player.inventory.containerBelt : type == "wear" ? player.inventory.containerWear : player.inventory.containerMain;

                for (int i = 0; i < itemData.Length; i++)
                {
                    ItemData data = itemData[i];
                    if (data.amount < 1)
                        continue;

                    Item item = CreateItem(data);
                    item.position = data.position;
                    item.SetParent(container);
                }
                return true;
            }

            private Item CreateItem(ItemData itemData)
            {
                Item item = ItemManager.CreateByItemID(itemData.itemid, itemData.amount, itemData.skin);
                item.condition = itemData.condition;
                item.maxCondition = itemData.maxCondition;
                if (itemData.instanceData != null)
                    item.instanceData = itemData.instanceData;

                BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
                if (weapon != null)
                {
                    if (!string.IsNullOrEmpty(itemData.ammotype))
                        weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(itemData.ammotype);
                    weapon.primaryMagazine.contents = itemData.ammo;
                }

                FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
                if (flameThrower != null)
                    flameThrower.ammo = itemData.ammo;

                if (itemData.contents != null)
                {
                    foreach (ItemData contentData in itemData.contents)
                    {
                        Item newContent = ItemManager.CreateByItemID(contentData.itemid, contentData.amount);
                        if (newContent != null)
                        {
                            newContent.condition = contentData.condition;
                            newContent.MoveToContainer(item.contents);
                        }
                    }
                }
                return item;
            }

            public class PlayerData
            {
                public float[] stats;
                public Vector3 position;
                public ItemData[] containerMain;
                public ItemData[] containerWear;
                public ItemData[] containerBelt;

                public PlayerData() { }

                public PlayerData(BasePlayer player)
                {
                    stats = GetStats(player);
                    position = player.transform.position;
                    containerBelt = GetItems(player.inventory.containerBelt).ToArray();
                    containerMain = GetItems(player.inventory.containerMain).ToArray();
                    containerWear = GetItems(player.inventory.containerWear).ToArray();
                }

                private IEnumerable<ItemData> GetItems(ItemContainer container)
                {
                    return container.itemList.Select(item => new ItemData
                    {
                        itemid = item.info.itemid,
                        amount = item.amount,
                        ammo = item.GetHeldEntity() is BaseProjectile ? (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents : item.GetHeldEntity() is FlameThrower ? (item.GetHeldEntity() as FlameThrower).ammo : 0,
                        ammotype = (item.GetHeldEntity() as BaseProjectile)?.primaryMagazine.ammoType.shortname ?? null,
                        position = item.position,
                        skin = item.skin,
                        condition = item.condition,
                        maxCondition = item.maxCondition,
                        instanceData = item.instanceData ?? null,
                        contents = item.contents?.itemList.Select(item1 => new ItemData
                        {
                            itemid = item1.info.itemid,
                            amount = item1.amount,
                            condition = item1.condition
                        }).ToArray()
                    });
                }

                private float[] GetStats(BasePlayer player) => new float[] { player.health, player.metabolism.hydration.value, player.metabolism.calories.value };

                public void SetStats(BasePlayer player)
                {
                    player.health = stats[0];
                    player.metabolism.hydration.value = stats[1];
                    player.metabolism.calories.value = stats[2];
                    player.metabolism.SendChangesToClient();
                }
            }

            public class ItemData
            {
                public int itemid;
                public ulong skin;
                public int amount;
                public float condition;
                public float maxCondition;
                public int ammo;
                public string ammotype;
                public int position;
                public ProtoBuf.Item.InstanceData instanceData;
                public ItemData[] contents;
            }
        }
        #endregion

        #region Localization
        public static string msg(string key, ulong playerId = 0U) => ins.lang.GetMessage(key, ins, playerId == 0U ? null : playerId.ToString());

        private Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Help.Main"] = "<color=#ce422b>/rustnet</color><color=#939393> - Display the help menu for using RustNET</color>",
            ["Help.Add"] = "<color=#ce422b>/terminal add</color><color=#939393> - Activate the terminal placement tool</color>",
            ["Help.AddPublic"] = "<color=#ce422b>/terminal add public</color><color=#939393> - Activate the public terminal placement tool. These terminals can be placed anywhere</color>",
            ["Help.Remove"] = "<color=#ce422b>/terminal remove</color><color=#939393> - Remove the terminal you are looking at</color>",
            ["Warning.ToolActivated"] = "<color=#939393>You must de-activate the placement tool before using these commands</color>",
            ["Error.NoPrivilege"] = "<color=#939393>You must have building privilege to use these commands</color>",
            ["Error.HoldComputer"] = "<color=#939393>You must be holding a targeting computer to activate the placement tool</color>",
            ["Error.NoEntity"] = "<color=#939393>You are not looking at a valid terminal</color>",
            ["Error.AlreadyRegistered"] = "<color=#939393>This building already has a RustNET terminal</color>",
            ["Error.PublicLink"] = "<color=#939393>You do not have permission to remove public terminals</color>",
            ["Success.Remove"] = "<color=#939393>You have removed the terminal from this base</color>",
            ["Warning.TerminalDestroyed"] = "<color=#ce422b>The terminal has been destroyed!</color>",
            ["Warning.NoPermission"] = "<color=#939393>You do not have permission to access this terminal</color>",
            ["Warning.AccessPrivilege"] = "<color=#939393>You need to be authorized on the tool cupboard to use this terminal</color>",
            ["Warning.NoChildPlugins"] = "<color=#939393>No remote plugins loaded! Unable to access the terminal</color>",
            ["Placement.PlaceItem"] = "<color=#939393>Use the <color=#ce422b>fire</color> button to place the {0}</color>",
            ["Placement.RequireParent"] = "<color=#939393>This {0} can only be placed on building blocks</color>",
            ["Placement.Cancelled"] = "<color=#ce422b>{0} placement cancelled!</color>",
            ["Placement.Success"] = "<color=#ce422b>{0} placed!</color>",
            ["UI.Title"] = "<color=#28ffa6>CHAOS INDUSTRIES (TM) RustNET BASELINK PROTOCOL v{0}</color>",
            ["UI.Enable"] = "Automation Enabled",
            ["UI.Disable"] = "Automation Disabled",
            ["UI.Controlled"] = "Player Controlled",
            ["UI.Inventory"] = "Access Inventory",
            ["UI.Control"] = "Remote Control",
            ["UI.Back"] = "Back",
            ["UI.Next"] = "Next",
            ["UI.Page"] = "Page {0}/{1}",
            ["UI.TerminalID"] = "<color=#28ffa6>TERMINAL ID: </color>{0}",
            ["UI.TerminalID.Public"] = "<color=#28ffa6>TERMINAL ID: </color>PUBLIC-{0}",
            ["UI.TerminalID.Help"] = "<color=#28ffa6>TERMINAL ID: </color>HELP TERMINAL",
            ["UI.Select.Terminal"] = "> <color=#28ffa6>Terminals</color> <",
            ["UI.Logon"] = "<color=#28ffa6>LOGON    : **********</color>",
            ["UI.Password"] = "<color=#28ffa6>PASSWORD : **************</color>",
            ["UI.Exit"] = "> <color=#28ffa6>LOGOFF</color> <",
            ["UI.MakeSelection"] = "> <color=#28ffa6>Make a selection to continue...</color>",
            ["UI.HelpButton"] = "> <color=#28ffa6>HELP</color> <",
            ["UI.Return"] = "> <color=#28ffa6>RETURN</color> <",
            ["UI.MainMenu"] = "> <color=#28ffa6>MAIN MENU</color> <",                       
            ["UI.Help.Title"] = "> <color=#28ffa6>Terminal Help Menu</color> <",
            ["UI.Help"] = "> Some notes on terminals\n\n> You can only place 1 terminal per building.\n> Terminals can only be placed on building blocks.\n> Terminals are linked to the block they are on, if the block is destroyed so is the terminal.\n> If at any stage your terminal is destroyed all remote links will be severed and will need to be reset to a new terminal.\n> To access a terminal you need to be authorized on the base tool cupboard\n\n> Creating a terminal\nStep 1. Acquire a targeting computer.\nStep 2. Place the targeting computer in your hands.\nStep 3. Type <color=#28ffa6>/terminal add</color>. The terminal placement tool is now activated! You can now place the terminal in your base by pressing the <color=#28ffa6>FIRE</color> button, or cancel placement by pressing the <color=#28ffa6>AIM</color> button.\n\n> Removing a terminal\nStep 1. Look at the terminal you want to remove.\nStep 2. Type <color=#28ffa6>/terminal remove</color> to remove the terminal (This will remove all remote links to the terminal)",
            ["UI.Help.Public"] = "\n\n> Creating a public terminal\nThese terminals can be placed anywhere and accessed by anyone, although they should NOT be placed on building blocks or player bases.\nStep 1. Place the targeting computer in your hands.\nStep 2. Type <color=#28ffa6>/terminal add public</color>. You can now place the terminal anywhere by pressing the <color=#28ffa6>FIRE</color> button, or cancel placement by pressing the <color=#28ffa6>AIM</color> button."
        };
        #endregion
    }
}
