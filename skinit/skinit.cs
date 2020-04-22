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

        private const int slot = 19;
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
            guiCreator.registerImage(this, "GUI_1_1", "https://i.ibb.co/NN6RxY0/GUI-1-1.png");
            guiCreator.registerImage(this, "GUI_1_2", "https://i.ibb.co/YdTq4nc/GUI-2-1.png");
            guiCreator.registerImage(this, "GUI_1_3", "https://i.ibb.co/6BjwM3s/GUI-3-1.png");
            guiCreator.registerImage(this, "GUI_1_4", "https://i.ibb.co/j5cfVty/GUI-4-1.png");
            guiCreator.registerImage(this, "GUI_1_5", "https://i.ibb.co/1G1FXv3/GUI-5-1.png");
            guiCreator.registerImage(this, "GUI_1_6", "https://i.ibb.co/qrRmPhm/GUI-6-1.png");
            guiCreator.registerImage(this, "GUI_1_7", "https://i.ibb.co/Kx87XFz/GUI-7-1.png");
            guiCreator.registerImage(this, "GUI_1_8", "https://i.ibb.co/9ntZTXR/GUI-8-1.png");
            guiCreator.registerImage(this, "GUI_1_9", "https://i.ibb.co/PG5XTGj/GUI-9-1.png");
            guiCreator.registerImage(this, "GUI_1_10", "https://i.ibb.co/XJ8gkzv/GUI-10-1.png");
            guiCreator.registerImage(this, "GUI_1_11", "https://i.ibb.co/hsFnmd9/GUI-11-1.png");
            guiCreator.registerImage(this, "GUI_1_12", "https://i.ibb.co/qYw4VDh/GUI-12-1.png");
            guiCreator.registerImage(this, "GUI_1_13", "https://i.ibb.co/B6WRv3Q/GUI-13-1.png");
            guiCreator.registerImage(this, "GUI_1_14", "https://i.ibb.co/JH7BsnM/GUI-14-1.png");
            guiCreator.registerImage(this, "background", "https://i.ibb.co/5TyZK1c/Skin-Mockup.png");
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
            containerGUI.addImage("GUI_1_1", new Rectangle(660, 30, 1260, 540, 1920, 1080, true), "GUI_1_1", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_2", new Rectangle(1230, 570, 126, 242, 1920, 1080, true), "GUI_1_2", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_3", new Rectangle(1356, 570, 564, 71, 1920, 1080, true), "GUI_1_3", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_4", new Rectangle(1440, 641, 480, 89, 1920, 1080, true), "GUI_1_4", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_5", new Rectangle(1356, 730, 564, 82, 1920, 1080, true), "GUI_1_5", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_6", new Rectangle(394, 30, 266, 784, 1920, 1080, true), "GUI_1_6", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_7", new Rectangle(2, 30, 134, 779, 1920, 1080, true), "GUI_1_7", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_8", new Rectangle(2, 1056, 1920, 22, 1920, 1080, true), "GUI_1_8", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_9", new Rectangle(1230, 812, 691, 244, 1920, 1080, true), "GUI_1_9", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_10", new Rectangle(2, 814, 658, 23, 1920, 1080, true), "GUI_1_10", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_11", new Rectangle(2, 906, 658, 150, 1920, 1080, true), "GUI_1_11", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_12", new Rectangle(2, 837, 74, 71, 1920, 1080, true), "GUI_1_12", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_13", new Rectangle(640, 837, 20, 71, 1920, 1080, true), "GUI_1_13", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_14", new Rectangle(2, 0, 1920, 30, 1920, 1080, true), "GUI_1_14", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("background", new Rectangle(0, 0, 1920, 1080, 1920, 1080, true), "background", GuiContainer.Layer.hud, null, 0, 0);
            containerGUI.addPlainButton("close", new Rectangle(1827, 30, 64, 64, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText(""));
            containerGUI.display(container.player);
            container.player.ChatMessage("sendUIworked"); // debug
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