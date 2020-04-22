// Requires: GUICreator

#define DEBUG
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Oxide.Plugins.GUICreator;

namespace Oxide.Plugins
{
    [Info("skinit", "Ohm & Bunsen", "0.1.0")]
    [Description("Template")]
    class skinit : RustPlugin
    {
        #region references

        private GUICreator guiCreator;
        

        #endregion

        #region global
        private static skinit pluginInstance = null;

        public skinit()
        {
            pluginInstance = this;
        }

        DynamicConfigFile File;
        StoredData storedData;

        private const int slot = 20;
        #endregion

        #region classes

        public class virtualContainer:MonoBehaviour
        {
            public BasePlayer player;
            public ItemContainer itemContainer;
            public uint uid;
            public Item item = null;

            public virtualContainer() { }

            public void init(BasePlayer player)
            {
                this.player = player;
                itemContainer = new ItemContainer
                {
                    entityOwner = player,
                    playerOwner = player,
                    capacity = slot+1,
                    isServer = true,
                    allowedContents = ItemContainer.ContentsType.Generic
                };
                itemContainer.GiveUID();
                this.uid = itemContainer.uid;
            }

            public static virtualContainer find(BasePlayer player)
            {
                virtualContainer output = null;

                player.gameObject.TryGetComponent<virtualContainer>(out output);

                return output;
            }

            public void send()
            {
                if (player == null || itemContainer == null) return;
                PlayerLoot loot = player.inventory.loot;

                loot.Clear();
                loot.PositionChecks = false;
                loot.entitySource = player;
                loot.itemSource = null;
                loot.AddContainer(itemContainer);
                loot.SendImmediate();

                player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "generic");

                pluginInstance.sendUI(this);
                pluginInstance.Subscribe(nameof(CanAcceptItem));
            }

            public void close()
            {
                if(item != null) player.GiveItem(item);
                pluginInstance.closeUI(this);
                Destroy(this);
            }
        }

        #endregion

        #region oxide hooks
        void Init()
        {
            permission.RegisterPermission("skinit.use", this);
            File = Interface.Oxide.DataFileSystem.GetFile("skinit/posData");
            loadData();
        }

        void OnServerInitialized()
        {
            guiCreator = (GUICreator)Manager.GetPlugin("GUICreator");
            lang.RegisterMessages(messages, this);
            cmd.AddChatCommand("skinit", this, nameof(skinitCommand));
            cmd.AddChatCommand("test", this, nameof(testCommand));
            guiCreator.registerImage(this, "background", "https://i.ibb.co/44NqsyG/background.png");
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            var player = loot.gameObject.GetComponent<BasePlayer>();
            if (player != loot.entitySource)
                return;

#if DEBUG
            player.ChatMessage("OnPlayerLootEnd: closing virtualContainer");
#endif
            virtualContainer.find(player)?.close();
            Unsubscribe(nameof(CanAcceptItem));
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            BasePlayer player = container?.GetOwnerPlayer();
            if(!player) return null;
#if DEBUG
            player.ChatMessage($"CanAcceptItem: container:{container?.uid}, item:{item?.amount} x {item?.info?.displayName?.english}, targetPos:{targetPos}");
#endif
            virtualContainer vContainer = virtualContainer.find(player);
            if (vContainer == null) return null;
            if (vContainer.uid != container.uid) return null;
            if (targetPos != slot) return ItemContainer.CanAcceptResult.CannotAccept;
            vContainer.item = item;
            onItemInserted(vContainer, item);

            return null;
        }
        #endregion

        #region UI

        public void sendUI(virtualContainer container)
        {
            GuiContainer containerGUI = new GuiContainer(this, "background");
            containerGUI.addImage("background", new Rectangle(0, 0, 1920, 1080, 1920, 1080, true), "background", GuiContainer.Layer.menu, null, 0, 0);

        }

        public void closeUI(virtualContainer container)
        {

        }

        private void onItemInserted(virtualContainer container, Item item)
        {
#if DEBUG
            PrintToChat($"OnItemInserted: container:{container.uid}, owner:{container?.player?.displayName}, item:{item?.amount} x {item?.info?.displayName?.english}");
#endif
        }

        #endregion

        #region commands
        //see Loaded() hook
        private void skinitCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("skinnitCommand");
#endif
            if (!permission.UserHasPermission(player.UserIDString, "skinit.use"))
            {
                PrintToChat(player, lang.GetMessage("noPermission", this, player.UserIDString));
                return;
            }
            virtualContainer container = virtualContainer.find(player);
            if (container != null) Puts($"Skin-it: {player.displayName} already has a vContainer... this shouldn't happen");
            container = player.gameObject.AddComponent<virtualContainer>();
            container.init(player);
            timer.Once(0.5f, () => container.send());
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("testing");
#endif

        }

        #endregion

        #region data management
        private class StoredData
        {
            public List<Vector3> positionList = new List<Vector3>();

            public StoredData()
            {
            }
        }

        void saveData()
        {
            try
            {
                File.WriteObject(storedData);
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

        void loadData()
        {
            try
            {
                storedData = File.ReadObject<StoredData>();
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }
        #endregion

        #region Config
        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Allow Pos Command")]
            public bool allowPosCom = true;

        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
            }
            catch
            {
                Puts("Config data is corrupted, replacing with default");
                config = new ConfigData();
            }

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig() => config = new ConfigData();
        #endregion

        #region Localization
        Dictionary<string, string> messages = new Dictionary<string, string>()
        {
            {"posOutput", "Player Coordinates: X:{0}, Y:{1}, Z:{2}"},
            {"noPermission", "You don't have permission to use this command!"}
        };
        #endregion
    }
}