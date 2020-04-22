using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("skinit", "Ohm & Bunsen", "0.1.0")]
    [Description("Template")]
    class skinit : RustPlugin
    {
        #region fields
        DynamicConfigFile File;
        StoredData storedData;
        #endregion

        #region classes

        pub

        #endregion

        #region oxide hooks
        void Init()
        {
            permission.RegisterPermission("RPT.use", this);
            File = Interface.Oxide.DataFileSystem.GetFile("skinit/posData");
            loadData();
        }

        void Loaded()
        {
            lang.RegisterMessages(messages, this);
            cmd.AddChatCommand("skinit", this, nameof(skinitCommand));
            cmd.AddChatCommand("test", this, nameof(testCommand));
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            BasePlayer owner = container?.GetOwnerPlayer();
            if(!owner) return null;
            owner.ChatMessage($"CanAcceptItem: container:{container.uid}, isServer:{container.isServer}");
            return null;
        }
        #endregion

        #region commands
        //see Loaded() hook
        private void skinitCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "skinit.use"))
            {
                PrintToChat(player, lang.GetMessage("noPermission", this, player.UserIDString));
                return;
            }
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage("testing");
            timer.Once(0.5f, () => sendContainer(player));
        }

        private void sendContainer(BasePlayer player)
        {
            ItemContainer itemContainer = new ItemContainer
            {
                entityOwner = player,
                capacity = 1,
                isServer = true,
                allowedContents = ItemContainer.ContentsType.Generic,
            };

            itemContainer.GiveUID();

            PlayerLoot loot = player.inventory.loot;

            loot.Clear();
            loot.PositionChecks = false;
            loot.entitySource = player;
            loot.itemSource = null;
            loot.AddContainer(itemContainer);
            loot.SendImmediate();

            player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "generic");
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