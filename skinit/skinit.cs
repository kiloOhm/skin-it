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
            guiCreator.registerImage(this, "GUI_1_1", "https://i.ibb.co/V3VRWg7/Skin-Mockup-01.jpg");
            guiCreator.registerImage(this, "GUI_1_2", "https://i.ibb.co/nf93ptY/Skin-Mockup-02.jpg");
            guiCreator.registerImage(this, "GUI_1_3", "https://i.ibb.co/CWkFW7f/Skin-Mockup-03.jpg");
            guiCreator.registerImage(this, "GUI_1_4", "https://i.ibb.co/54DDKNv/Skin-Mockup-04.jpg");
            guiCreator.registerImage(this, "GUI_1_5", "https://i.ibb.co/3SsxSKD/Skin-Mockup-05.jpg");
            guiCreator.registerImage(this, "GUI_1_8", "https://i.ibb.co/JvHNJCb/Skin-Mockup-08.jpg");
            guiCreator.registerImage(this, "GUI_1_10", "https://i.ibb.co/wJ4f7jD/Skin-Mockup-10.jpg");
            guiCreator.registerImage(this, "GUI_1_11", "https://i.ibb.co/mvj4s5h/Skin-Mockup-11.jpg");
            guiCreator.registerImage(this, "GUI_1_12", "https://i.ibb.co/3FbQQsR/Skin-Mockup-12.jpg");
            guiCreator.registerImage(this, "GUI_1_13", "https://i.ibb.co/Z2Vr4k1/Skin-Mockup-13.jpg");
            guiCreator.registerImage(this, "GUI_1_15", "https://i.ibb.co/FVcSSgm/Skin-Mockup-15.jpg");
            guiCreator.registerImage(this, "GUI_1_16", "https://i.ibb.co/6b2g3Sx/Skin-Mockup-16.jpg");
            guiCreator.registerImage(this, "GUI_1_17", "https://i.ibb.co/1973FmT/Skin-Mockup-17.jpg");
            guiCreator.registerImage(this, "GUI_1_6", "https://i.ibb.co/MGMGxXB/Skin-Mockup-06.jpg");
            guiCreator.registerImage(this, "GUI_1_7", "https://i.ibb.co/wBCDVrR/Skin-Mockup-07.jpg");
            guiCreator.registerImage(this, "GUI_1_9", "https://i.ibb.co/R27hRD0/Skin-Mockup-09.jpg");
            guiCreator.registerImage(this, "GUI_1_14", "https://i.ibb.co/6Zmgrxj/Skin-Mockup-14.jpg");
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
            containerGUI.addImage("GUI_1_1", new Rectangle(0, 0, 393, 30, 1920, 1080, true), "GUI_1_1", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_2", new Rectangle(393, 0, 265, 837, 1920, 1080, true), "GUI_1_2", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_3", new Rectangle(658, 0, 570, 573, 1920, 1080, true), "GUI_1_3", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_4", new Rectangle(1228, 0, 692, 643, 1920, 1080, true), "GUI_1_4", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_5", new Rectangle(0, 30, 134, 807, 1920, 1080, true), "GUI_1_5", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_6", new Rectangle(1228, 643, 130, 88, 1920, 1080, true), "GUI_1_8", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_7", new Rectangle(1439, 643, 481, 88, 1920, 1080, true), "GUI_1_10", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_8", new Rectangle(1228, 731, 692, 326, 1920, 1080, true), "GUI_1_11", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_9", new Rectangle(134, 814, 259, 23, 1920, 1080, true), "GUI_1_12", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_10", new Rectangle(0, 837, 74, 243, 1920, 1080, true), "GUI_1_13", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_11", new Rectangle(637, 837, 21, 71, 1920, 1080, true), "GUI_1_15", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_12", new Rectangle(74, 908, 584, 172, 1920, 1080, true), "GUI_1_16", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_13", new Rectangle(658, 1057, 1262, 23, 1920, 1080, true), "GUI_1_17", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addImage("GUI_1_14", new Rectangle(134, 30, 259, 784, 1920, 1080, true), "GUI_1_6", GuiContainer.Layer.under, null, 0, 0);
            containerGUI.addImage("GUI_1_15", new Rectangle(658, 573, 570, 484, 1920, 1080, true), "GUI_1_7", GuiContainer.Layer.under, null, 0, 0);
            containerGUI.addImage("GUI_1_16", new Rectangle(1358, 643, 81, 88, 1920, 1080, true), "GUI_1_9", GuiContainer.Layer.under, null, 0, 0);
            containerGUI.addImage("GUI_1_17", new Rectangle(74, 837, 563, 71, 1920, 1080, true), "GUI_1_14", GuiContainer.Layer.under, null, 0, 0);
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