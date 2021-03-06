﻿// Requires: GUICreator

//#define DEBUG
//#define DEBUG2
//#define DEBUG3
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.DiscordObjects;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Oxide.Plugins.GUICreator;

namespace Oxide.Plugins
{
    [Info("skinit", "Ohm & Bunsen", "1.5.0")]
    [Description("GUI based Item skinning")]
    class skinit : RustPlugin
    {
        #region references
        [PluginReference]
        private Plugin ServerRewards, Economics;

        private GUICreator guiCreator;

        #endregion

        #region global
        private static skinit PluginInstance = null;

        public skinit()
        {
            PluginInstance = this;
        }

        DynamicConfigFile SkinsFile;
        SkinsData skinsData;

        DynamicConfigFile RequestsFile;
        RequestData requestData;


        private const int slot = 0;
        #endregion

        #region classes

        [JsonObject(MemberSerialization.OptIn)]
        public class Request
        {
            [JsonProperty(PropertyName = "Requester Steam ID")]
            public ulong userID;
            [JsonProperty(PropertyName = "Skin ID")]
            public ulong skinID;
            [JsonProperty(PropertyName = "Category")]
            public string category;
            [JsonProperty(PropertyName = "Skin")]
            public Skin skin;

            public Request() { }

            public Request(ulong userID, ulong skinID)
            {
                this.userID = userID;
                this.skinID = skinID;
                this.category = config.defaultCatName;
            }

            public void init(Action<Skin> callback)
            {
                PluginInstance.skinWebRequest(skinID, (skin) =>
                {
                    if (skin != null)
                    {
                        this.skin = skin;
                        PluginInstance.saveRequestsData();
                        PluginInstance.guiCreator.registerImage(PluginInstance, skin.safename, skin.url, imgSizeX: config.imgRes, imgSizeY: config.imgRes);
                    }
                    callback?.Invoke(skin);
                });
            }

            public void approve(string category = null)
            {
                if (category == null) category = this.category;
                PluginInstance.addSkin(skin, category);
                PluginInstance.saveSkinsData();
            }

            public void returnToQueue()
            {
                PluginInstance.requestData.returnRequest(this);
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Skinnable
        {
            [JsonProperty(PropertyName = "Item Shortname")]
            public string shortname;
            [JsonProperty(PropertyName = "Categories")]
            public List<Category> categories = new List<skinit.Category>();

            public Skinnable(string shortname)
            {
                this.shortname = shortname;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Category
        {
            [JsonProperty(PropertyName = "Name")]
            public string name;
            public string safename => Regex.Replace(name, " ", "_");
            [JsonProperty(PropertyName = "Item Shortname")]
            public string shortname;
            [JsonProperty(PropertyName = "Require Oxide Permission")]
            public bool perm = false;
            [JsonProperty(PropertyName = "Skins")]
            public List<Skin> skins = new List<skinit.Skin>();
            public Skinnable skinnable => PluginInstance.skinsData.GetSkinnable(this.shortname);

            public Category(string name, string shortname)
            {
                this.name = name;
                this.shortname = shortname;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        public class Skin
        {
            [JsonProperty(PropertyName = "Name")]
            public string name;
            [JsonProperty(PropertyName = "original Name")]
            public string title;
            public string safename => Regex.Replace(title, " ", "_") + $"_{url}";
            [JsonProperty(PropertyName = "Category")]
            public string category;
            [JsonProperty(PropertyName = "Item Shortname")]
            public string shortname;
            [JsonProperty(PropertyName = "Skin ID")]
            public ulong id;
            [JsonProperty(PropertyName = "Preview URL")]
            public string url;
            public Skinnable skinnable => PluginInstance.skinsData.GetSkinnable(this.shortname);
            public Category categoryObject => PluginInstance.skinsData.GetCategory(this.skinnable, this.category);

            public Skin(string name, string category, string shortname, ulong id, string url)
            {
                this.title = name;
                this.name = name;
                this.category = category;
                this.shortname = shortname;
                this.id = id;
                this.url = url;
            }
        }

        public class virtualContainer : MonoBehaviour
        {
            public BasePlayer player;
            public ItemContainer itemContainer;
            public uint uid;
            public Item item = null;

            public virtualContainer() { }

            public void init(BasePlayer player)
            {
#if DEBUG2
                player.ChatMessage("initialized virtual container");
#endif
                this.player = player;
                itemContainer = new ItemContainer
                {
                    entityOwner = player,
                    playerOwner = player,
                    capacity = slot + 1,
                    isServer = true,
                    allowedContents = ItemContainer.ContentsType.Generic
                };
                itemContainer.GiveUID();
                this.uid = itemContainer.uid;
            }

            public static virtualContainer find(BasePlayer player)
            {
#if DEBUG2
                player.ChatMessage("finding virtual container");
#endif
                virtualContainer output = null;

                player.gameObject.TryGetComponent<virtualContainer>(out output);

                return output;
            }

            public void send()
            {
#if DEBUG2
                player.ChatMessage("sending virtual container");
#endif
                if (player == null || itemContainer == null) return;
                PlayerLoot loot = player.inventory.loot;

                loot.Clear();
                loot.PositionChecks = false;
                loot.entitySource = player;
                loot.itemSource = null;
                loot.AddContainer(itemContainer);
                loot.SendImmediate();

                player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "lantern");

                PluginInstance.sendUI(this);
                PluginInstance.Subscribe(nameof(CanAcceptItem));
            }

            public void close()
            {
#if DEBUG2
                player.ChatMessage("closing virtual container");
#endif
                if (item != null) player.GiveItem(item);
                PluginInstance.closeUI(this);
                Destroy(this);
            }
        }

        #endregion

        #region oxide hooks
        void Init()
        {
            permission.RegisterPermission("skinit.use", this);
            permission.RegisterPermission("skinit.admin", this);
            permission.RegisterPermission("skinit.free", this);

            permission.RegisterPermission("skinit.attire", this);
            permission.RegisterPermission("skinit.deployable", this);
            permission.RegisterPermission("skinit.tool", this);
            permission.RegisterPermission("skinit.weapon", this);
            SkinsFile = Interface.Oxide.DataFileSystem.GetFile("skinit/skins");
            loadSkinsData();
            RequestsFile = Interface.Oxide.DataFileSystem.GetFile("skinit/requests");
            loadRequestsData();
        }

        void OnServerInitialized()
        {
            //references
            guiCreator = (GUICreator)Manager.GetPlugin("GUICreator");
            if (config.useServerRewards && ServerRewards == null) Puts("ServerRewards not loaded! get it at https://umod.org/plugins/server-rewards");
            if ((config.useServerRewards || config.useEconomics) && Economics == null) Puts("Economics not loaded! get it at https://umod.org/plugins/economics");
            if (guiCreator == null)
            {
                Puts("GUICreator missing! This shouldn't happen");
                return;
            }

            InitDiscord();
            ServerMgr.Instance.StartCoroutine(InitApprovedSkins());

            #region config processing

            //re-register skin images
            ServerMgr.Instance.StartCoroutine(skinsData.RegisterImages());

            //add new skins
            Dictionary<string, List<ulong>> toBeAdded = new Dictionary<string, List<ulong>>();
            foreach (string shortname in config.skins.Keys)
            {
                Skinnable item = skinsData.GetSkinnable(shortname);
                if (item == null)
                {
                    item = new Skinnable(shortname);
                    skinsData.items.Add(item);
                }
                foreach (string category in config.skins[shortname].Keys)
                {
                    Category cat = skinsData.GetCategory(item, category);
                    if (cat == null)
                    {
                        cat = new Category(category, item.shortname);
                        item.categories.Add(cat);
                    }
                    foreach (ulong id in config.skins[shortname][category])
                    {
                        if (skinsData.GetSkin(cat, id) == null)
                        {
                            if (!toBeAdded.ContainsKey(cat.name)) toBeAdded.Add(cat.name, new List<ulong>());
                            toBeAdded[cat.name].Add(id);
                        }
                    }

                }
            }
            foreach (string cat in toBeAdded.Keys)
            {
                ServerMgr.Instance.StartCoroutine(AddSkins(toBeAdded[cat], cat, false));
            }

            //delete removed skins
            skinsData.items.RemoveAll(item => !config.skins.ContainsKey(item.shortname));
            foreach (Skinnable item in skinsData.items)
            {
                item.categories.RemoveAll(cat => !config.skins[item.shortname].ContainsKey(cat.name));
                foreach (Category cat in item.categories)
                {
                    cat.skins.RemoveAll(skin => !config.skins[item.shortname][cat.name].Contains(skin.id));
                }
            }
            saveSkinsData();

            //delete approved skins
            List<ulong> toBeRemoved = new List<ulong>();
            foreach (Skinnable item in skinsData.items)
            {
                foreach (Category cat in item.categories)
                {
                    cat.skins.RemoveAll(skin => IsApproved(skin.id));
                }
            }
            saveSkinsData();
            
            foreach(ulong id in toBeRemoved)
            {
                Puts($"purging approved skin: {id} from data and config");
                RemoveSkinFromConfig(id);
            }

            #endregion

            //commands
            cmd.AddChatCommand(config.command, this, nameof(skinitCommand));
            cmd.AddConsoleCommand("skinit.add", this, nameof(addCommand));
            cmd.AddConsoleCommand("skinit.approved", this, nameof(approvedCommand));

            //images
            guiCreator.registerImage(this, "GUI_1_1", "https://i.imgur.com/jqRb4f5.jpg");
            guiCreator.registerImage(this, "GUI_1_2", "https://i.imgur.com/A1Pcc45.jpg");
            guiCreator.registerImage(this, "GUI_1_3", "https://i.imgur.com/rIzu5vi.jpg");
            guiCreator.registerImage(this, "GUI_1_4", "https://i.imgur.com/WjF3WwR.jpg");
            guiCreator.registerImage(this, "GUI_1_5", "https://i.imgur.com/SHpMWVQ.jpg");
            guiCreator.registerImage(this, "GUI_1_8", "https://i.imgur.com/36EBskB.jpg");
            guiCreator.registerImage(this, "GUI_1_10", "https://i.imgur.com/x53Yq2f.jpg");
            guiCreator.registerImage(this, "GUI_1_11", "https://i.imgur.com/Cy961Zc.jpg");
            guiCreator.registerImage(this, "GUI_1_12", "https://i.imgur.com/wdEg6lD.jpg");
            guiCreator.registerImage(this, "GUI_1_13", "https://i.imgur.com/UFSftQD.jpg");
            guiCreator.registerImage(this, "GUI_1_15", "https://i.imgur.com/G7B5NTh.jpg");
            guiCreator.registerImage(this, "GUI_1_16", "https://i.imgur.com/CLfx3BO.jpg");
            guiCreator.registerImage(this, "GUI_1_17", "https://i.imgur.com/q0sECdd.jpg");
            // guiCreator.registerImage(this, "GUI_1_6", "https://i.imgur.com/jXNQyWB.jpg");
            guiCreator.registerImage(this, "GUI_1_7", "https://i.imgur.com/IfsZhBv.jpg");
            guiCreator.registerImage(this, "GUI_1_9", "https://i.imgur.com/gA0yP5Z.jpg");
            guiCreator.registerImage(this, "GUI_1_14", "https://i.imgur.com/yHLqrQc.jpg");
            guiCreator.registerImage(this, "GUI_1_Fill_1", "https://i.imgur.com/F24t7V8.jpg");
            guiCreator.registerImage(this, "GUI_1_Fill_2", "https://i.imgur.com/Fae4VRR.jpg");
            guiCreator.registerImage(this, "Text_1", "https://i.imgur.com/mirJ3cR.png");
            guiCreator.registerImage(this, "availableSkinsPanel", "https://i.imgur.com/ZhArsw3.png");
            guiCreator.registerImage(this, "previewPanel", "https://i.imgur.com/MrTYRlK.png");
            guiCreator.registerImage(this, "button_ADD", "https://i.imgur.com/tbqAmyU.png");
            guiCreator.registerImage(this, "button_CHECK", "https://i.imgur.com/8f81Ave.png");
            guiCreator.registerImage(this, "button_CATEGORIES", "https://i.imgur.com/nvKQode.png");
            guiCreator.registerImage(this, "button_RENAME", "https://i.imgur.com/Jh3QKSH.png");
            guiCreator.registerImage(this, "button_REMOVE", "https://i.imgur.com/2a5zp68.png");
            guiCreator.registerImage(this, "popup_REMOVE", "https://i.imgur.com/ewo1fFE.png");
            guiCreator.registerImage(this, "popup_CATEGORIES", "https://i.imgur.com/tUjpJ5T.png");
            guiCreator.registerImage(this, "popup_RENAME", "https://i.imgur.com/smEo4kf.png");
            guiCreator.registerImage(this, "arrow_up", "https://i.imgur.com/u6Bbq5a.png");
            guiCreator.registerImage(this, "arrow_down", "https://i.imgur.com/iY9Wa2A.png");
            guiCreator.registerImage(this, "new_arrow_down", "https://i.imgur.com/O4uSIAy.png");
            guiCreator.registerImage(this, "dropdown", "https://i.imgur.com/8QZmPFq.png");
            guiCreator.registerImage(this, "popup_ADDNEW", "https://i.imgur.com/sryvzoF.png");
            guiCreator.registerImage(this, "popup_ADDNEWCATEGORY", "https://i.imgur.com/X9Q4Tyb.png");
            guiCreator.registerImage(this, "popup_CHECKREQUESTS", "https://i.imgur.com/jObsFrN.png");
            guiCreator.registerImage(this, "popup_PROMPT", "https://i.imgur.com/6qwc5Jr.png");
            guiCreator.registerImage(this, "requestButtons", "https://i.imgur.com/ns3JGeV.png");
            guiCreator.registerImage(this, "popup_HISTORY", "https://i.imgur.com/0CaZijM.png");
            guiCreator.registerImage(this, "lock_unlock", "https://i.imgur.com/kfqmFTR.png");
            guiCreator.registerImage(this, "lock_lock", "https://i.imgur.com/4Qx4tgi.png");
            guiCreator.registerImage(this, "lock_unlockgreen", "https://i.imgur.com/ZMnJgDb.png");
            guiCreator.registerImage(this, "lock_lockgreen", "https://i.imgur.com/Np52FK8.png");
           
            //lang
            lang.RegisterMessages(messages, this);

            //hooks
            Unsubscribe(nameof(CanAcceptItem));
        }

        void Unload()
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                virtualContainer container = virtualContainer.find(player);
                if (container != null) container.close();
            }
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            var player = loot.gameObject.GetComponent<BasePlayer>();
            if (player != loot.entitySource) return;

            virtualContainer container = virtualContainer.find(player);
            if (container == null) return;
#if DEBUG
            player.ChatMessage($"OnPlayerLootEnd: closing virtualContainer {container.player}");
#endif
            container.close();
            Unsubscribe(nameof(CanAcceptItem));
        }

        private object CanLootPlayer(BasePlayer looter, UnityEngine.Object target)
        {
            if (looter != target) return null;
#if DEBUG2
            looter.ChatMessage("CanLootPlayer: searching for virtualContainer");
#endif
            var container = virtualContainer.find(looter);
            if (container == null) return null;

            return true;
        }

        ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            BasePlayer player = container?.GetOwnerPlayer();
            if (!player) return null;
            virtualContainer vContainer = virtualContainer.find(player);
            if (vContainer == null) return null;
            if (item?.parent?.uid == vContainer.uid)
            {
                onItemRemoved(vContainer, item);
                vContainer.item = null;
                return null;
            }
            if (vContainer.uid != container?.uid) return null;
            if (targetPos != slot) return ItemContainer.CanAcceptResult.CannotAccept;
            vContainer.item = item;
            onItemInserted(vContainer, item);

            return null;
        }
        #endregion

        #region UI

        #region UI parameters
        float FadeIn = 0.2f;
        float FadeOut = 0.2f;
        #endregion
        //Intitialize UI, call relevant functions to start upon keyboard command
        public void sendUI(virtualContainer container)
        {
            StringBuilder sb = new StringBuilder();
            if (container.player.IPlayer.HasPermission("skinit.attire")) sb.Append("attire ");
            if (container.player.IPlayer.HasPermission("skinit.deployable")) sb.Append("deployables ");
            if (container.player.IPlayer.HasPermission("skinit.tool")) sb.Append("tools ");
            if (container.player.IPlayer.HasPermission("skinit.weapon")) sb.Append("weapons ");
            string skinPermissions = sb.ToString().ToUpper();
            skinPermissions =  skinPermissions.Trim();
            skinPermissions = Regex.Replace(skinPermissions, " ", ", ");
#if DEBUG
            container.player.ChatMessage("sending UI");
#endif
            GuiContainer containerGUI = new GuiContainer(this, "background");
            //containerGUI.addImage("GUI_1_1", new Rectangle(0, 0, 392, 30, 1921, 1080, true), "GUI_1_1", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_2", new Rectangle(392, 0, 271, 837, 1921, 1081, true), "GUI_1_2", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_3", new Rectangle(663, 0, 562, 576, 1921, 1081, true), "GUI_1_3", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_4", new Rectangle(1225, 0, 695, 643, 1921, 1081, true), "GUI_1_4", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_5", new Rectangle(0, 30, 134, 807, 1921, 1081, true), "GUI_1_5", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_6", new Rectangle(1225, 643, 133, 89, 1921, 1081, true), "GUI_1_8", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_7", new Rectangle(1439, 643, 481, 89, 1921, 1081, true), "GUI_1_10", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_8", new Rectangle(1225, 732, 695, 322, 1921, 1081, true), "GUI_1_11", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_9", new Rectangle(134, 814, 258, 23, 1921, 1081, true), "GUI_1_12", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_10", new Rectangle(0, 837, 74, 243, 1921, 1081, true), "GUI_1_13", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_11", new Rectangle(631, 837, 32, 71, 1921, 1081, true), "GUI_1_15", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_12", new Rectangle(74, 908, 589, 172, 1921, 1081, true), "GUI_1_16", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_13", new Rectangle(663, 1054, 1257, 26, 1921, 1081, true), "GUI_1_17", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_Fill_1", new Rectangle(1204, 146, 39, 430, 1921, 1081, true), "GUI_1_Fill_1", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_Fill_2", new Rectangle(0, 0, 134, 92, 1921, 1081, true), "GUI_1_Fill_2", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //// containerGUI.addImage("GUI_1_14", new Rectangle(134, 30, 258, 784, 1921, 1081, true), "GUI_1_6", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_15", new Rectangle(663, 576, 562, 478, 1921, 1081, true), "GUI_1_7", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_16", new Rectangle(1358, 643, 81, 89, 1921, 1081, true), "GUI_1_9", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            //containerGUI.addImage("GUI_1_17", new Rectangle(74, 837, 557, 71, 1921, 1081, true), "GUI_1_14", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            //containerGUI.addImage("Text_1", new Rectangle(1334, 925, 460, 121, 1920, 1080, true), "Text_1", GuiContainer.Layer.menu, null, FadeIn, FadeOut);

            containerGUI.addPanel("Text_Permissions_1", new Rectangle(80, 907, 471, 43, 1920, 1080, true, Rectangle.Anchors.BottomLeft), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("According to your permissions, you may skin...", 15, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_Permissions_2", new Rectangle(80, 945, 471, 43, 1920, 1080, true, Rectangle.Anchors.BottomLeft), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{skinPermissions}", 18, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_2", new Rectangle(1249, 731, 570, 50, 1920, 1080, true, Rectangle.Anchors.BottomCenter), GuiContainer.Layer.menu, new GuiColor(50, 50, 50, 1), 0, 0, new GuiText("ITEM TO BE SKINNED", 23, new GuiColor(255, 255, 255, 0.3f)));
            containerGUI.display(container.player);
            panelOneBackground(container.player);

            buttonsLeft(container.player);
            skinitButton(container);
        }

        //Create "SKIN-IT" button and define what occurs on click
        public enum buttonStates { idle, noSelected, noPermission, cantAfford, redundant , success };
        public void skinitButton(virtualContainer container, Skin activeSkin = null, Item item = null, buttonStates flag = buttonStates.idle)
        {
            BasePlayer player = container.player;
            Action<BasePlayer, string[]> Skinit = (bPlayer, input) =>
            {
                if (activeSkin == null || item == null)
                {
                    skinitButton(container, flag: buttonStates.noSelected);
                }
                if (item.skin == activeSkin.id)
                {
                    skinitButton(container, activeSkin, item, skinit.buttonStates.redundant);
                    return;
                }
                if (buySkin(container, item, activeSkin))
                {
                    skinitButton(container, flag: buttonStates.success);
                }
                else
                {
                    if(!hasPermission(player, item, activeSkin.categoryObject))
                    {
                        skinitButton(container, activeSkin, item, flag: buttonStates.noPermission);
                    }
                    else if(getCost(player, item, activeSkin.categoryObject) > getPoints(player))
                    {
                        skinitButton(container, activeSkin, item, flag: buttonStates.cantAfford);
                    }
                }

            };
            GuiContainer containerGUI = new GuiContainer(this, "skinitButton", "background");

            Rectangle pos = new Rectangle(1585, 833, 234, 82, 1920, 1080, true, Rectangle.Anchors.BottomCenter);

            if (flag == buttonStates.success)
            {
                containerGUI.addPlainButton("checkout_success", pos, GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("SUCCESS", 30, new GuiColor(134, 190, 41, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "background") == null) return;
                    skinitButton(container);
                });
            }
            else if (flag == buttonStates.noPermission)
            {
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                containerGUI.addPlainButton("checkout_failure", pos, GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("NO PERMISSION", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "background") == null) return;
                    skinitButton(container, activeSkin, item);
                });
            }
            else if (flag == buttonStates.cantAfford)
            {
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                containerGUI.addPlainButton("checkout_failure", pos, GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CAN'T AFFORD", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "background") == null) return;
                    skinitButton(container, activeSkin, item);
                });
            }
            else if (flag == buttonStates.noSelected)
            {
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                containerGUI.addPlainButton("checkout_attempt", pos, GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("NO SKIN SELECTED!", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "background") == null) return;
                    skinitButton(container);
                });
            }
            else if(flag == skinit.buttonStates.redundant)
            {
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                containerGUI.addPlainButton("checkout_redundant", pos, GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("ALREADY HAS THIS!", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => 
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "background") == null) return;
                    skinitButton(container);
                });
            }
            else 
            {
                containerGUI.addPlainButton("checkout", pos, GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("SKIN-IT!", 30, new GuiColor(134, 190, 41, 0.8f)), Skinit);
            }
            containerGUI.display(player);
        }

        //Define a function which will split a list into lists of a given size (default = 30)
        public static List<List<T>> SplitIntoChunks<T>(List<T> list, int chunkSize = 30)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentException("chunkSize must be greater than 0.");
            }

            List<List<T>> retVal = new List<List<T>>();
            int index = 0;
            while (index < list.Count)
            {
                int count = list.Count - index > chunkSize ? chunkSize : list.Count - index;
                retVal.Add(list.GetRange(index, count));

                index += chunkSize;
            }

            return retVal;
        }

        //Create background for the "Available Skins" and "Preview" panel
        public void panelOneBackground(BasePlayer player) // also background for preview panel
        {
            GuiContainer containerGUI = new GuiContainer(this, "panelOneBackground", "background");
            containerGUI.addImage("availableSkinsPanel", new Rectangle(452, 32, 1021, 451, 1920, 1080, true), "availableSkinsPanel", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("previewPanel", new Rectangle(1492, 32, 389, 451, 1920, 1080, true), "previewPanel", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addPanel("previewPanelText", new Rectangle(1501, 0, 371, 74, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("PREVIEW", 30, new GuiColor(255, 255, 255, 1)));
            containerGUI.addPanel("availableSkinsPanelText", new Rectangle(696, 0, 534, 74, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("AVAILABLE SKINS", 30, new GuiColor(255, 255, 255, 1)));
            containerGUI.display(player);
        }

        //Define a function which will destroy all popups upon call
        public void destroyPopups(BasePlayer player)
        {
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupRemove");
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupRename");
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupCategories");
            GuiTracker.getGuiTracker(player).destroyGui(this, "categorySelection");
            GuiTracker.getGuiTracker(player).destroyGui(this, "dropdown");
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupAddnew");
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupReviewRequests");
            GuiTracker.getGuiTracker(player).destroyGui(this, "popupPendingRequests");
            GuiTracker.getGuiTracker(player).destroyGui(this, "suggestNewStepOne");
            GuiTracker.getGuiTracker(player).destroyGui(this, "suggestNewStepTwo");
            GuiTracker.getGuiTracker(player).destroyGui(this, "suggestNewStepThree");

        }

        //Populate Available Skins with Pages of 30 Items Each and Right/Left Functionality
        public void availableSkins(BasePlayer player, Item item, List<List<Skin>> skinListOfLists, int activeSkin = 0, int page = 0)
        {

            double OriginX = 487;
            double xSpacing;
            double OriginY;
            double OriginY1 = 87;
            double OriginY2 = 210;
            double OriginY3 = 339;
            double Height = 95;
            double maximumWidth = 950;
            int picturesEachRow = 10;
            int numberOfRows = 3;
            int totalPictures = picturesEachRow * numberOfRows;
            double widthEach = maximumWidth / picturesEachRow;


            List<Skin> skinList = skinListOfLists[page];
            GuiContainer containerGUI = new GuiContainer(this, "panelOne", "categories");
            int i = 0;
            foreach (Skin s in skinList)
            {
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    availableSkins(player, item, skinListOfLists, activeSkin = index, page);
                    //staffOnlyButtonsRight(player, skinList[index]);
                    previewPanel(bPlayer, item, skinList[index]);
                    skinitButton(virtualContainer.find(player), skinList[index], item);
                    destroyPopups(player);

                };
                if (i < picturesEachRow)
                {
                    xSpacing = OriginX + (widthEach * i);
                    OriginY = OriginY1;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);


                }
                else if (i >= picturesEachRow && i < (picturesEachRow * 2))
                {
                    xSpacing = (OriginX + (widthEach * i)) - (widthEach * picturesEachRow);
                    OriginY = OriginY2;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                else if (i < totalPictures)
                {
                    xSpacing = (OriginX + (widthEach * i)) - ((widthEach * picturesEachRow) * 2);
                    OriginY = OriginY3;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                i++;
            }

            Action<BasePlayer, string[]> GoRight = (bPlayer, input) =>
            {
                if (page == skinListOfLists.Count - 1)
                {
                    page = 0;
                    availableSkins(player, item, skinListOfLists, activeSkin = 0, page);
                    destroyPopups(player);

                } else
                {
                    page += 1;
                    availableSkins(player, item, skinListOfLists, activeSkin = 0, page);
                    destroyPopups(player);
                }
            };
            Action<BasePlayer, string[]> GoLeft = (bPlayer, input) =>
            {
                if (page == 0)
                {
                    page = skinListOfLists.Count - 1;
                    availableSkins(player, item, skinListOfLists, activeSkin = 0, page);
                    destroyPopups(player);
                } else
                {
                    page -= 1;
                    availableSkins(player, item, skinListOfLists, activeSkin = 0, page);
                    destroyPopups(player);
                }
            };
            if (skinListOfLists.Count > 1)
            {
                containerGUI.addPlainButton("goRight", new Rectangle(1437, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(">>", 25, new GuiColor(255, 255, 255, 0.8f)), GoRight);
                containerGUI.addPlainButton("goLeft", new Rectangle(431, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText("<<", 25, new GuiColor(255, 255, 255, 0.8f)), GoLeft);
            }

            containerGUI.display(player);

        }

        //Populate Preview Panel with Selected Skin from Available Skins
        public void previewPanel(BasePlayer player, Item item, Skin skin)
        {
            GuiContainer containerGUI = new GuiContainer(this, "previewPanel", "panelOne");

            containerGUI.addImage("previewPicture", new Rectangle(1520, 66, 332, 333, 1920, 1080, true), skin.safename, GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
            containerGUI.addPanel("previewPictureText", new Rectangle(1501, 399, 371, 74, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{skin.name}", 20, new GuiColor(255, 255, 255, 0.5f)));
            containerGUI.addPanel("Text_CostToSkin", new Rectangle(1349, 690, 426, 32, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"COST TO SKIN: {getCost(player, item, skin.categoryObject)}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            containerGUI.display(player);
            staffOnlyButtonsRight(player, skin);
        }
         
        public void sendCategories(BasePlayer player, Item item, List<Category> categoriesList, int activeCategory = 0, int page = 0)
        {
            double OriginY = 494;
            double Height = 46;
            double maximumWidth = 1429;
            
            double OriginX = 452;
            int fontSize = 15;
            bool isStaff = isAdmin(player); // Placeholder to make category padlock appear 
            string lockImage = "lock_lock";
            List<List<Category>> ListOfCategories = SplitIntoChunks<Category>(categoriesList, 5);
            List<Category> activeCategoriesList = ListOfCategories[page];

            double widthEach = maximumWidth / activeCategoriesList.Count;


            GuiContainer containerGUI = new GuiContainer(this, "categories", "background");
            containerGUI.addPanel("Text_AccountBalance", new Rectangle(1349, 790, 426, 35, 1920, 1080, true, Rectangle.Anchors.BottomCenter), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"ACCOUNT BALANCE: {getPoints(player)}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            int i = 0;

            foreach (Category Cat in activeCategoriesList)
            {
                double xSpacing = OriginX + (widthEach * i);
                double xSpacingIndented = xSpacing+(widthEach*.025);
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    if (activeCategory == index) return;
                    sendCategories(bPlayer, item, categoriesList, index, page);
                };
                Action<BasePlayer, string[]> lockChange = (bPlayer, input) =>
                {
                    toggleCategoryPerm(Cat);
                    sendCategories(player, item, categoriesList, activeCategory, page);
                };

                if (isStaff == true && Cat.name != config.defaultCatName)
                {
                    if (Cat.perm)
                    {
                        lockImage = "lock_lock";
                    } 
                    else { lockImage = "lock_unlock"; }
                }
                if (i == activeCategory)
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText(Cat.name.ToUpper(), fontSize, new GuiColor(134, 190, 41, 0.8f)), callback);
                    if (isStaff == true && Cat.name != config.defaultCatName) containerGUI.addButton($"padlockbutton_{i}", new Rectangle(xSpacingIndented, OriginY, 25, 40, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText("", fontSize, new GuiColor(0, 0, 0, 0)), lockChange, imgName: $"{lockImage}green");
                }
                else
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(Cat.name.ToUpper(), fontSize, new GuiColor(255, 255, 255, 0.8f)), callback);
                    if (isStaff == true && Cat.name != config.defaultCatName) containerGUI.addButton($"padlockbutton_{i}", new Rectangle(xSpacingIndented, OriginY, 25, 40, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText("", fontSize, new GuiColor(0, 0, 0, 0)), lockChange, imgName: lockImage);
                }
                i++;
            }
            Action<BasePlayer, string[]> goUp = (bPlayer, input) =>
            {
                sendCategories(player, item, categoriesList, activeCategory, page + 1);
            };
            Action<BasePlayer, string[]> goDown = (bPlayer, input) =>
            {
                sendCategories(player, item, categoriesList, activeCategory, page - 1);
            };

            if(page < ListOfCategories.Count -1) containerGUI.addPlainButton("goUp", new Rectangle(maximumWidth+OriginX, OriginY, 46, 46, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(74, 29, 33, 0), FadeIn, FadeOut, new GuiText(">>", 20, new GuiColor(255, 255, 255, 0.5f)), goUp);
            if(page > 0) containerGUI.addPlainButton("goDown", new Rectangle(OriginX-50, OriginY, 46, 46, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(74, 29, 33, 0), FadeIn, FadeOut, new GuiText("<<", 20, new GuiColor(255, 255, 255, 0.5f)), goDown);

            containerGUI.display(player);
            int whereYouAre = (page * 5) + activeCategory;
            sendSkins(player, item, categoriesList[whereYouAre]);

        }

        public void sendSkins(BasePlayer player, Item item, Category category)
        {
            List<List<Skin>> ListOfLists = SplitIntoChunks<Skin>(category.skins, 30);
            availableSkins(player, item, ListOfLists);
        }

        //Create staff-only buttons to the right
        public void staffOnlyButtonsRight(BasePlayer player, Skin activeSkin) // Creates buttons for staff only to see when clicking on an item
        {
            bool isStaff = isAdmin(player);
            if (isStaff == true)
            {
                GuiContainer containerGUI = new GuiContainer(this, "buttonsRight", "categories");

                // Function when you click the remove button
                Action<BasePlayer, string[]> removeSkin = (bPlayer, input) =>
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(this, "popupRemove") == null)
                    {
                        destroyPopups(player);
                        popupRemove(player, activeSkin);
                    }
                    else
                    {
                        destroyPopups(player);
                    }
                };
                // Function when you click the rename button
                Action<BasePlayer, string[]> renameSkin = (bPlayer, input) =>
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(this, "popupRename") == null)
                    {
                        destroyPopups(player);
                        popupRename(player, activeSkin);
                    }
                    else
                    {
                        destroyPopups(player);
                    }
                };
                // Function when you click the categories button
                Action<BasePlayer, string[]> categorySkin = (bPlayer, input) =>
                {
                    if (GuiTracker.getGuiTracker(player).getContainer(this, "popupCategories") == null)
                    {
                        destroyPopups(player);
                        popupCategories(player, activeSkin);
                    }
                    else
                    {
                        destroyPopups(player);
                    }
                };

                // Button to change category of skin
                containerGUI.addImage("categories_image", new Rectangle(1539, 351, 67, 67, 1920, 1080, true), "button_CATEGORIES", GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                containerGUI.addPlainButton("categories_button", new Rectangle(1539, 351, 67, 67, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), categorySkin);
                // Button to rename skin
                containerGUI.addImage("rename_image", new Rectangle(1653, 354, 62, 62, 1920, 1080, true), "button_RENAME", GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                containerGUI.addPlainButton("rename_button", new Rectangle(1653, 354, 62, 62, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), renameSkin);
                // Button to remove skin
                containerGUI.addImage("remove_image", new Rectangle(1765, 354, 62, 62, 1920, 1080, true), "button_REMOVE", GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                containerGUI.addPlainButton("remove_button", new Rectangle(1765, 354, 62, 62, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), removeSkin);

                containerGUI.display(player);
            }
        }

        //Define what happens when you click remove, rename, or recategorize buttons
        // Popup for when you click the remove button
        public void popupRemove(BasePlayer player, Skin activeSkin)
        {
            if (GuiTracker.getGuiTracker(player).getContainer(this, "popupRemove") == null)
            {
                destroyPopups(player);
                GuiContainer containerGUI = new GuiContainer(this, "popupRemove", "background");
                Skin skin = activeSkin;
                Action<BasePlayer, string[]> confirm = (bPlayer, input) =>
                {
                    
                    PluginInstance.removeSkin(skin);
                    destroyPopups(player);
                    prompt(player, $"Removed {activeSkin.name}", "SKIN REMOVED");
                    virtualContainer container = virtualContainer.find(player);
                    if (skinsData.GetSkinnable(container.item.info.shortname) != null) onItemInserted(container, container.item);
                    else onItemRemoved(container, container.item);
                };
                Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
                {
                    destroyPopups(player);
                    staffOnlyButtonsRight(player, activeSkin);
                };
                containerGUI.addImage("popup_Remove", new Rectangle(1444, 417, 474, 211, 1920, 1080, true), "popup_REMOVE", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                containerGUI.addPanel("header", new Rectangle(1488, 469, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("REMOVE THIS SKIN?", 20, new GuiColor(255, 255, 255, 0.8f)));
                containerGUI.addPlainButton("cancel", new Rectangle(1502, 528, 163, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("NO", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
                containerGUI.addPlainButton("confirm", new Rectangle(1684, 528, 163, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("YES", 20, new GuiColor(134, 190, 41, 0.8f)), confirm);
                containerGUI.display(player);
            }
            else
            {
                destroyPopups(player);
            }
        }

        // Popup for when you click the rename button
        public void popupRename(BasePlayer player, Skin activeSkin)
        {
            if (GuiTracker.getGuiTracker(player).getContainer(this, "popupRename") == null)
            {
                destroyPopups(player);
                GuiContainer containerGUI = new GuiContainer(this, "popupRename", "background" );
                Skin skin = activeSkin;
                Action<BasePlayer, string[]> confirm = (bPlayer, input) =>
                {
                    if (input.Length == 0) return;
                    StringBuilder newName = new StringBuilder();
                    int i = 1;
                    foreach(string s in input)
                    {
                        newName.Append(s);
                        if (i != input.Length) newName.Append(" ");
                        i++;
                    }
                    
                    destroyPopups(player);
                    prompt(player, $"Renamed {skin.name} to {newName}.", "SKIN RENAMED");
                    skin.name = newName.ToString();
                    saveSkinsData();
                    virtualContainer container = virtualContainer.find(player);
                    onItemInserted(container, container.item);
                };
                Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
                {
                    destroyPopups(player);
                    staffOnlyButtonsRight(player, activeSkin);
                };

                GuiTracker.getGuiTracker(player).destroyGui(this, "popupRemove");
                GuiTracker.getGuiTracker(player).destroyGui(this, "popupCategories");
                containerGUI.addImage("popup_Rename", new Rectangle(1444, 417, 474, 361, 1920, 1080, true), "popup_RENAME", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                containerGUI.addInput("newname", new Rectangle(1488, 540, 371, 59, 1920, 1080, true), confirm, GuiContainer.Layer.menu, null, new GuiColor("white"), 24, new GuiText("", 20), 0, 0);
                containerGUI.addPanel("confirm", new Rectangle(1488, 611, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), 0, 0, new GuiText("PRESS ENTER TO CONFIRM", 20, new GuiColor(134, 190, 41, 0.8f)));
                containerGUI.addPanel("header", new Rectangle(1488, 469, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0,0, 0), 0, 0, new GuiText("RENAME THIS SKIN", 20, new GuiColor(255, 255, 255, 0.8f)));
                containerGUI.addPlainButton("cancel", new Rectangle(1488, 682, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
                containerGUI.display(player);
            } else
            {
                destroyPopups(player);
            }
            
        }

        // Popup for when you click the category button
        public void popupCategories(BasePlayer player, Skin activeSkin, bool dropDownActive = false, bool error = false, string activeSelection = "Click to Select a Category", bool dontClose=false)
        {

            if(GuiTracker.getGuiTracker(player).getContainer(this, "popupCategories")==null || dontClose == true) {
                if (dontClose == false)
                {
                    destroyPopups(player);
                }
                    GuiContainer containerGUI = new GuiContainer(this, "popupCategories", "background");
            
            Action<string> callback = (option) =>
                {
                    GuiTracker.getGuiTracker(player).destroyGui(this, "dropdown");
                    popupCategories(player, activeSkin, dropDownActive = false, error, activeSelection = option, dontClose: true);
                };
                Action<BasePlayer, string[]> triggerdropdown = (bPlayer, input) =>
                {

                        
                    if(dropDownActive == false)
                    {
                        dropDownActive = true;
                        popupCategories(player, activeSkin, true, false, dontClose: true) ;
                        List<string> options = new List<string>();
                        foreach(Category cat in GetCategories(player, activeSkin.skinnable))
                        {
                            options.Add(cat.name);
                        }
                        Predicate<string> predicate = (input2) =>
                        {
                            if (options.Contains(input2)) return false;
                            return true;
                        };
                        dropdown(player, options, new Rectangle(1449, 600, 446, 429, 1920, 1080, true), callback, 0, true, predicate);
                    }
                    else {
                        destroyPopups(player);
                        popupCategories(player, activeSkin, false, false, dontClose: true);
                        dropDownActive = false;

                    }
                };
                Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
                {
                    destroyPopups(player);
                };
                Action<BasePlayer, string[]> confirm = (bPlayer, input) =>
                {

                        PluginInstance.changeSkinCategory(activeSkin, activeSelection);
                        destroyPopups(player);
                        prompt(player, $"The skin {activeSkin.name} has been moved to {activeSelection}.", "SKIN MOVED");
                        virtualContainer container = virtualContainer.find(player);
                        onItemInserted(container, container.item);
                };

                if (dropDownActive == false)
                {
                    containerGUI.addImage("arrow_image", new Rectangle(1772, 518, 119, 112, 1920, 1080, true), "arrow_down", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                }
                else
                {
                    containerGUI.addImage("arrow_image", new Rectangle(1772, 518, 119, 112, 1920, 1080, true), "arrow_up", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);

                };
                    containerGUI.addImage("popup_Categories", new Rectangle(1444, 417, 474, 361, 1920, 1080, true), "popup_CATEGORIES", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                containerGUI.addPlainButton("arrow_button", new Rectangle(1487, 541, 316, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0.5f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText(activeSelection, 15, new GuiColor(255, 255, 255, 0.8f)), triggerdropdown);
                containerGUI.addPlainButton("dropdown_button", new Rectangle(1772, 518, 119, 112, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), triggerdropdown);
                if (error == true)
                {
                    containerGUI.addPlainButton("confirm", new Rectangle(1488, 611, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("NO CATEGORY SELECTED", 15, new GuiColor(162, 51, 46, 0.8f)));
                    timer.Once(0.5f, () => popupCategories(player, activeSkin));

                }
                else
                {
                    containerGUI.addPlainButton("confirm", new Rectangle(1488, 611, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("CONFIRM", 20, new GuiColor(134, 190, 41, 0.8f)), confirm);

                }
                containerGUI.addPanel("popupcategories_header", new Rectangle(1488, 469, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("PICK A NEW CATEGORY", 20, new GuiColor(255, 255, 255, 0.8f)));
                containerGUI.addPlainButton("cancel", new Rectangle(1488, 682, 371, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
            
            containerGUI.display(player);
            } else if(dontClose==false)
            {
                destroyPopups(player);
            }
        }

        //general purpose dropdown
        public void dropdown(BasePlayer player, List<string> options, Rectangle rectangle, Action<string> callback, int page = 0, bool allowNew = false, Predicate<string> predicate = null)
        {
            if (allowNew) options.Add("(add new)");
            int maxItems = 5;
            List<List<string>> ListOfLists = SplitIntoChunks<string>(options, maxItems);
            GuiContainer container = new GuiContainer(this, "dropdown", "background");
            container.addPlainPanel("dropdown_background", rectangle, GuiContainer.Layer.menu, new GuiColor(0,0,0,0.6f), 0, 0, GuiContainer.Blur.medium);

            double cfX = rectangle.W / 300;
            double cfY = rectangle.H / 570;

            Action<BasePlayer, string[]> up = (bPlayer, input) =>
            {
                dropdown(player, options, rectangle, callback, page - 1, false, predicate);
            };
            Action<BasePlayer, string[]> down = (bPlayer, input) =>
            {
                dropdown(player, options, rectangle, callback, page + 1, false, predicate);
            };
            if(page > 0) container.addPlainButton("dropdown_up", new Rectangle(0, 1, 298, 36, 300, 570, true), new GuiColor(1, 1, 1, 0.4f), 0, 0, new GuiText("<b>∧</b>", (int)Math.Floor(22*cfY), new GuiColor("black")) , up, parent: "dropdown_background");
            if(page < ListOfLists.Count -1) container.addPlainButton("dropdown_up", new Rectangle(0, 533, 298, 37, 300, 570, true), new GuiColor(1, 1, 1, 0.4f), 0, 0, new GuiText("<b>∨</b>", (int)Math.Floor(22*cfY), new GuiColor("black")), down, parent: "dropdown_background");

            int count = 0;
            foreach(string option in ListOfLists[page]) 
            {
                Rectangle pos = new Rectangle(10, 40 + count * 100, 280, 90, 300, 570, true);;
                Rectangle absPos = new Rectangle(rectangle.X + (pos.X*cfX), rectangle.Y + (pos.Y*cfY), pos.W*cfX, pos.H*cfY, 1920, 1080, true);

                Action<BasePlayer, string[]> btnCallback = null;
                if(option == "(add new)")
                {
                    btnCallback = (bPlayer, input) => dropdownAddNew(player, absPos, callback, predicate);
                } else
                {
                    string selected = option;
                    btnCallback = (bPlayer, input) =>
                    {
                        callback(selected);
                    };
                }
                container.addPlainButton($"dropdown_option_{option}", pos, new GuiColor(0, 0, 0, 0.7f), 0, 0, new GuiText(option, color: new GuiColor(1, 1, 1, 0.5f)), btnCallback, parent: "dropdown_background");
                count++;
            }

            container.display(player);
        }

        public void dropdownAddNew(BasePlayer player, Rectangle rectangle, Action<string> callback, Predicate<string> predicate)
        {
            GuiContainer container = new GuiContainer(this, "dropdown_addNew", "dropdown");
            Action<BasePlayer, string[]> inputCallback = (bPlayer, input) => 
            {
                if (input.Length == 0) return;
                GuiTracker.getGuiTracker(player).destroyGui(PluginInstance, "dropdown_addNew");
                StringBuilder newName = new StringBuilder();
                int i = 1;
                foreach (string s in input)
                {
                    newName.Append(s);
                    if (i != input.Length) newName.Append(" ");
                    i++;
                }

                if (predicate != null)
                {
                    if (!predicate(newName.ToString()))
                    {
                        prompt(player, "Your input is invalid!", "INVALID INPUT");
                        dropdownAddNew(player, rectangle, callback, predicate);
                        return;
                    }
                }
                callback(newName.ToString());
                GuiTracker.getGuiTracker(player).destroyGui(PluginInstance, container);
            };

            container.addInput("dropdown_addNew_input", rectangle, inputCallback, GuiContainer.Layer.menu, null, new GuiColor("white"), 15, new GuiText("", color: new GuiColor("black")), 0, 0);
            container.display(player);
        }
        
        //suggest and review suggestions buttons
        public void buttonsLeft(BasePlayer player)
        {
            if (!config.allowSuggestions && !isAdmin(player)) return;
            GuiContainer containerGUI = new GuiContainer(this, "buttonsLeft", "background");
            containerGUI.addImage("add_image", new Rectangle(0, 5, 110, 110, 1920, 1080, true, Rectangle.Anchors.UpperLeft), "button_ADD", GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
            Action<BasePlayer, string[]> popupAddNew = (bPlayer, input) =>
            {
                suggestNewStepOne(player);
            };
            Action<BasePlayer, string[]> popupReviewRequests = (bPlayer, input) =>
            {
                if((PluginInstance.requestData?.requests?.Count ?? 0) == 0)
                {
                    prompt(bPlayer, "There are no pending suggestions!", "NO SUGGESTIONS");
                    return;
                }
                reviewRequests(bPlayer);
            };
            containerGUI.addPlainButton("add_button", new Rectangle(0, 5, 110, 110, 1920, 1080, true, Rectangle.Anchors.UpperLeft), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), popupAddNew);

            bool isStaff = isAdmin(player);
            int queueSum = requestData.requests.Count;
            if(isStaff == true)
            {
                containerGUI.addImage("check_image", new Rectangle(0, 111, 110, 110, 1920, 1080, true, Rectangle.Anchors.UpperLeft), "button_CHECK", GuiContainer.Layer.menu, null, FadeIn = 0.25f, FadeIn = 0.25f);
                containerGUI.addPlainButton("check_button", new Rectangle(0, 111, 110, 110, 1920, 1080, true, Rectangle.Anchors.UpperLeft), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), popupReviewRequests);
                containerGUI.addPanel("check_text", new Rectangle(72, 130, 39, 21, 1920, 1080, true, Rectangle.Anchors.UpperLeft), GuiContainer.Layer.menu, new GuiColor(10, 10, 10, 0.0f), 0, 0, new GuiText($"{queueSum}", 12, new GuiColor(255, 255, 255, 0.6f), TextAnchor.MiddleLeft));
            }
            containerGUI.display(player);
        }

        //tinder style suggestion review panel
        public void reviewRequests(BasePlayer player, Request request = null)
        {
            if (GuiTracker.getGuiTracker(player).getContainer(this, "popupReviewRequests") != null) return;

            if(request == null) request = requestData.getNextRequest();
            if (request == null) return;

#if DEBUG3
            Puts($"request by {request.userID}, for {request.skin.name}, cat: {request.skin.category}");
#endif

            string requestName = request.skin.name;
            string requestCategory = request.category;
            string requestAuthor = BasePlayer.FindByID(request.userID)?.displayName;
#if DEBUG3
            Puts($"requestAuthor: {requestAuthor ?? "null"}");
#endif
            string requestImage = request.skin.safename;

            destroyPopups(player);
            Action<BasePlayer> closeCallback = (bPlayer) =>
            {
                //make sure to return request if admin just tabs out
                if (request == null) return;
#if DEBUG
                player.ChatMessage($"returning {request.skin.name} to queue!");
#endif
                PluginInstance.requestData.returnRequest(request);
            };
            GuiContainer containerGUI = new GuiContainer(this, "popupReviewRequests", "background", closeCallback);
            Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
            {
                GuiTracker.getGuiTracker(player).destroyGui(PluginInstance, containerGUI);
            };
            Action<BasePlayer, string[]> reject = (bPlayer, input) =>
            {
                if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "dropdown") != null) return;
                Effect.server.Run("assets/prefabs/food/bota bag/effects/bota-bag-cork-squeak.prefab", player.transform.position);
                LogToFile("suggestions", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} [{player.userID}]{player.displayName} rejected {request.skinID}:{request.skin.name}", this);
                request = null;
                destroyPopups(player);
                reviewRequests(player);
                buttonsLeft(player);
            };
            Action<BasePlayer, string[]> approve = (bPlayer, input) =>
            {
                if (GuiTracker.getGuiTracker(player).getContainer(PluginInstance, "dropdown") != null) return;
                Effect.server.Run("assets/prefabs/deployable/research table/effects/research-success.prefab", player.transform.position);
                LogToFile("suggestions", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} [{player.userID}]{player.displayName} approved {request.skinID}:{request.skin.name}, it was added to {request.category}", this);
                request.approve(request.category);
                request = null;
                destroyPopups(player);
                reviewRequests(player);
                buttonsLeft(player);
            };
            Action<BasePlayer, string[]> changeCat = (bPlayer, input) =>
            {
                GuiTracker tracker = GuiTracker.getGuiTracker(player);
                GuiContainer dropdown = tracker.getContainer(PluginInstance, "dropdown");
                if(dropdown != null)
                {
                    tracker.destroyGui(PluginInstance, dropdown);
                    return;
                }
                

                List<string> options = new List<string>();
                if (request.skin?.skinnable != null)
                {
                    foreach (Category cat in PluginInstance.GetCategories(player, request.skin.skinnable))
                    {
                        options.Add(cat.name);
                    }
                }
                else options.Add(config.defaultCatName);

                Action<string> changeCatCallback = (option) =>
                {
                    Request backup = request;
                    request = null;
                    destroyPopups(player);
                    backup.category = option;
                    reviewRequests(player, backup);
                };

                Predicate<string> predicate = (input2) =>
                {
                    if (options.Contains(input2)) return false;
                    return true;
                };

                this.dropdown(player, options, new Rectangle(762, 772, 398, 300, 1920, 1080, true), changeCatCallback, 0, true, predicate);
            };

            containerGUI.addImage("popup_CheckRequests", new Rectangle(492, 198, 918, 721, 1920, 1080, true), "popup_CHECKREQUESTS", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
            containerGUI.addImage("requestImage", new Rectangle(762, 278, 398, 402, 1920, 1080, true), requestImage, GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
            containerGUI.addImage("requestButtons", new Rectangle(682, 438, 551, 101, 1920, 1080, true), "requestButtons", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);

            containerGUI.addPanel("requestName", new Rectangle(762, 680, 398, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{requestName}", 20, new GuiColor(255, 255, 255, 0.8f)));
            containerGUI.addImage("dropdownArrow", new Rectangle(1126, 740, 30, 30, 1920, 1080, true), "new_arrow_down", GuiContainer.Layer.menu, null, 0, 0);
            containerGUI.addPlainButton("requestCategory", new Rectangle(762, 740, 398, 30, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0.6f), 0, 0, new GuiText($"{requestCategory}", 15, new GuiColor(255, 255, 255, 0.5f)), changeCat);
            containerGUI.addPanel("requestAuthor", new Rectangle(762, 780, 398, 30, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{requestAuthor}", 15, new GuiColor(255, 255, 255, 0.5f)));
                
            containerGUI.addPanel("popup_header", new Rectangle(761, 213, 398, 65, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("REQUEST REVIEW", 25, new GuiColor(255, 255, 255, 0.8f)));
            containerGUI.addPlainButton("reject", new Rectangle(691, 445, 89, 89, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0,0,0,0), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("", 20, new GuiColor(0,0,0,0)), reject);
            containerGUI.addPlainButton("approve", new Rectangle(1135, 445, 89, 89, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0,0,0,0), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("", 20, new GuiColor(0,0,0,0)), approve);

            containerGUI.addPlainButton("cancel", new Rectangle(761, 822, 398, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
            containerGUI.display(player);
        }

        //suggestion
        public void suggestNewStepOne(BasePlayer player, bool error = false, bool dontClose = false)
        {
            if (GuiTracker.getGuiTracker(player).getContainer(this, "background") == null)
            {
                destroyPopups(player);
            } // bunsen added this to fix bug
            else if (GuiTracker.getGuiTracker(player).getContainer(this, "popupAddnew") == null || error == true || dontClose == true)
            {
 
                if (error != true || dontClose != true)
                {
                    destroyPopups(player);
                }
                GuiContainer containerGUI = new GuiContainer(this, "popupAddnew", "background");
                Action<BasePlayer, string[]> inputCallback = (bPlayer, input) =>
                {
                    if (input == null) return;
                    if (input.Length == 0) return;
                    if(PluginInstance.requestData.getPendingRequests(player.userID).Count >= config.maxPendingReq)
                    {
                        destroyPopups(player);
                        prompt(player, "You can't suggest any more skins!", "MAX SUGGESTIONS");
                        Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                        return;
                    }
                    ulong skinID = 0;
                    if (!ulong.TryParse(input[0], out skinID))
                    {
                        suggestNewStepOne(player, true);
                        return;
                    }
                    if (skinID == 0) return;
                    if(PluginInstance.skinsData.GetSkin(skinID) != null)
                    {
                        destroyPopups(player);
                        prompt(player, "That skin is already included!", "SKIN INCLUDED");
                        Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                        return;
                    }
                    Request request = new Request(bPlayer.userID, skinID);

                    if(IsApproved(skinID))
                    {
                        destroyPopups(player);
                        prompt(player, "You can't suggest an approved skin!", "INVALID SKIN");
                        Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                        return;
                    }

                    Action<Skin> callback = (skin) =>
                    {
                        GuiTracker tracker = GuiTracker.getGuiTracker(player);
                        GuiContainer gametipContainer = tracker.getContainer(this, "gametip");
                        if (gametipContainer != null) tracker.destroyGui(this, gametipContainer);
                        if (skin == null)
                        {
                            destroyPopups(player);
                            prompt(player, "No Skin with this ID was found!", "SKIN DOESN'T EXIST");
                            Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                            return;
                        }
                        //else if(isAdmin(player))
                        //{
                        //    reviewRequests(player, request);
                        //}
                        else suggestNewStepTwo(player, request);
                    };
                    request.init(callback);
                    prompt(player, "Retrieving skin data from steam!", "PLEASE WAIT");
                };
                Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
                {
                    destroyPopups(player);
                };
                Action<BasePlayer, string[]> yourRequests = (bPlayer, input) =>
                {
                    pendingRequests(player);
                };
                containerGUI.addImage("popup_Addnew", new Rectangle(501, 284, 918, 468, 1920, 1080, true), "popup_ADDNEW", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                containerGUI.addInput("newname", new Rectangle(572, 462, 379, 67, 1920, 1080, true), inputCallback, GuiContainer.Layer.menu, null, new GuiColor("white"), 15, new GuiText("", 20), 0, 0);
                containerGUI.addPanel("newnameheader", new Rectangle(572, 416, 379, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("STEP 1: ENTER THE STEAM ID OF SKIN", 10, new GuiColor(255, 255, 255, 0.8f)));
                if (error == true)
                {
                    Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                    containerGUI.addPanel("confirm", new Rectangle(688, 565, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("NOT A VALID SKIN ID", 15, new GuiColor(162, 51, 46, 0.8f)));
                    timer.Once(0.5f, () => suggestNewStepOne(player, false, true));

                }
                else
                {
                    containerGUI.addPanel("confirm", new Rectangle(688, 565, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("PRESS ENTER TO PROCEED", 20, new GuiColor(134, 190, 41, 0.8f)));

                }
                containerGUI.addPanel("header", new Rectangle(688, 315, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("SUGGEST A SKIN", 25, new GuiColor(255, 255, 255, 0.8f)));
                containerGUI.addPlainButton("viewRequestsButton", new Rectangle(740, 369, 433, 47, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("", 20, new GuiColor(0, 0, 0, 0)), yourRequests);
                containerGUI.addPlainButton("cancel", new Rectangle(688, 643, 525, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
                containerGUI.display(player);
            }
            else
            {
                destroyPopups(player);
            }
        }

        public void suggestNewStepTwo(BasePlayer player, Request request, bool dropDownActive = false, string activeSelection = "Click to Select a Category", bool error = false)
        {
            GuiContainer containerGUI = new GuiContainer(this, "popupAddnew", "background");

            Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
            {
                destroyPopups(player);
            };
            Action<BasePlayer, string[]> proceed = (bPlayer, input) =>
            {
                if (activeSelection == "Click to Select a Category")
                {
                    suggestNewStepTwo(player, request, dropDownActive, activeSelection, error = true);
                    GuiTracker.getGuiTracker(player).destroyGui(this, "dropdown");
                }
                else {
                    request.category = activeSelection;
                    //if(isAdmin(player))
                    //{
                    //    request.approve();
                    //    prompt(player, "Skin has been added!", "SKIN ADDED");
                    //    PluginInstance.LogToFile("suggestions", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} [{player.userID}]{player.displayName}(Admin) added {request.skinID}:{request.skin.name} to Category {request.category}", PluginInstance);
                    //}
                    //else
                    //{
                        PluginInstance.requestData.addRequest(request);
                        prompt(player, "You may view your pending requests at any time.", "SKIN SUGGESTED");
                    //}
                    Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", player.transform.position);
                    destroyPopups(player);
                    buttonsLeft(player);
                }

            };
            Action<string> callback = (option) =>
            {
                GuiTracker.getGuiTracker(player).destroyGui(this, "dropdown");
                suggestNewStepTwo(player, request, dropDownActive = false, option);
            };

            Action<BasePlayer, string[]> triggerdropdown = (bPlayer, input) =>
            {
                if (dropDownActive == false)
                {
                    suggestNewStepTwo(player, request, dropDownActive=true, activeSelection);
                    //dropdown(player, request.skin, new Rectangle(716, 464, 446, 429, 1920, 1080, true), callback, "popupAddnew", OriginX: 772, OriginYOld: 488);
                    List<string> options = new List<string>();
                    if (request.skin?.skinnable != null)
                    {
                        foreach (Category cat in GetCategories(player, request.skin.skinnable))
                        {
                            options.Add(cat.name);
                        }
                    }
                    else options.Add(config.defaultCatName);
                    Predicate<string> predicate = (input2) =>
                    {
                        if (options.Contains(input2)) return false;
                        return true;
                    };
                    dropdown(player, options, new Rectangle(716, 464, 446, 429, 1920, 1080, true), callback, 0, true, predicate);
                }
                else
                {
                    GuiTracker.getGuiTracker(player).destroyGui(this, "dropdown");
                    dropDownActive = false;
                    suggestNewStepTwo(player, request, dropDownActive = false, activeSelection);
                }
            };
            containerGUI.addImage("popup_Addnew", new Rectangle(501, 284, 918, 468, 1920, 1080, true), "popup_ADDNEWCATEGORY", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);


            if (dropDownActive == false)
                    {
                        containerGUI.addImage("arrow_image", new Rectangle(1040, 385, 119, 112, 1920, 1080, true), "arrow_down", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
                     }
                 else
                     {
                        containerGUI.addImage("arrow_image", new Rectangle(1040, 385, 119, 112, 1920, 1080, true), "arrow_up", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);

                       };
            containerGUI.addPlainButton("arrow_button", new Rectangle(755, 407, 316, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0.5f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText(activeSelection, 15, new GuiColor(255, 255, 255, 0.8f)), triggerdropdown);
            containerGUI.addPlainButton("dropdown_button", new Rectangle(1040, 385, 119, 112, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)),triggerdropdown);
            

            containerGUI.addPanel("newnameheader", new Rectangle(755, 371, 362, 26, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("STEP 2: CHOOSE A CATEGORY", 10, new GuiColor(255, 255, 255, 0.8f)));
            if (error == true)
            {
                containerGUI.addPlainButton("confirm", new Rectangle(688, 478, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("NO CATEGORY SELECTED", 15, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(0.5f, () => suggestNewStepTwo(player, request));

            }
            else
            {
                containerGUI.addPlainButton("confirm", new Rectangle(688, 478, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("CONFIRM", 20, new GuiColor(134, 190, 41, 0.8f)), proceed);

            }
            containerGUI.addPanel("header", new Rectangle(755, 313, 373, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("SUGGEST A SKIN", 25, new GuiColor(255, 255, 255, 0.8f)));
             containerGUI.addPlainButton("cancel", new Rectangle(688, 556, 525, 60, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
             containerGUI.display(player);
            }

        public void pendingRequests(BasePlayer player, List<Request> requests = null, int page = 0)
        {
            destroyPopups(player);
            Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
            {
                destroyPopups(player);
            };

            if(requests == null) requests = requestData.getPendingRequests(player.userID);
            if (requests.Count == 0)
            {
                prompt(player, "You don't have any pending requests!", "NO PENDING REQUESTS");
                return;
            }
            int maxElements = 10;
            List<List<Request>> ListOfLists = SplitIntoChunks<Request>(requests, maxElements);

            Action<BasePlayer, string[]> lastPage = (bPlayer, input) =>
            {
                if (page <= 0) return;
                pendingRequests(player, requests, page - 1);
            };
            Action<BasePlayer, string[]> nextPage = (bPlayer, input) =>
            {
                if (page + 1 >= ListOfLists.Count) return;
                pendingRequests(player, requests, page + 1);
            };

            GuiContainer containerGUI = new GuiContainer(this, "popupPendingRequests", "background");

            containerGUI.addImage("popup_pendingRequests", new Rectangle(385, 138, 1143, 813, 1920, 1080, true), "popup_HISTORY", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
            containerGUI.addPanel("header", new Rectangle(695, 162, 524, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("PENDING REQUESTS", 25, new GuiColor(255, 255, 255, 0.8f)));

            containerGUI.addPlainButton("cancel", new Rectangle(695, 859, 524, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.00f, FadeOut = 0.00f, new GuiText("CLOSE", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
            containerGUI.addPlainButton("lastPage", new Rectangle(604, 860, 61, 61, 1920, 1080, true), GuiContainer.Layer.menu, text: new GuiText("<<", 20, new GuiColor("white")), callback: lastPage);
            containerGUI.addPlainButton("nextPage", new Rectangle(1250, 860, 61, 61, 1920, 1080, true), GuiContainer.Layer.menu, text: new GuiText(">>", 20, new GuiColor("white")), callback: nextPage);

            int columns = 2;
            int rows = 5;
            Rectangle origin = new Rectangle(441, 236, 509, 106, 1920, 1080, true);
            double gapX = 14;
            double gapY = 19;

            int count = 0;
            for(int i = 0; i < columns; i++)
            {
                if (count >= ListOfLists[page].Count) break;
                for (int j = 0; j < rows; j++)
                {
                    if (count >= ListOfLists[page].Count) break;
                    Request req = ListOfLists[page][count];
                    double X = origin.X + i * (origin.W + gapX);
                    double Y = origin.Y + j * (origin.H + gapY);
                    containerGUI.addPlainPanel($"{req.skinID}_parent", new Rectangle(X, Y, origin.W, origin.H, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0);
                    containerGUI.addImage($"{req.skinID}_img", new Rectangle(0, 0, 108, 104, origin.W, origin.H, true), req.skin.safename, $"{req.skinID}_parent");
                    containerGUI.addText($"{req.skinID}_name", new Rectangle(108, 0, 401, 53, origin.W, origin.H, true), new GuiText($"Skin: {req.skin.title}", 20, new GuiColor("white")), 0, 0, $"{req.skinID}_parent");
                    containerGUI.addText($"{req.skinID}_cat", new Rectangle(108, 53, 401, 53, origin.W, origin.H, true), new GuiText($"Category: {req.category}", 20, new GuiColor("white")), 0, 0, $"{req.skinID}_parent");
                    count++;
                }
            }

            containerGUI.display(player);


        }

        public void addNewCategoryMiddle(BasePlayer player, Request request)
        {
            GuiContainer containerGUI = new GuiContainer(this, "popupCategories", "background");
            Action<BasePlayer, string[]> inputCallback = (Bplayer, input) =>
            {
                StringBuilder newName = new StringBuilder();
                int i = 1;
                foreach (string s in input)
                {
                    newName.Append(s);
                    if (i != input.Length) newName.Append(" ");
                    i++;
                }

                request.category = newName.ToString();
                PluginInstance.requestData.addRequest(request);
                destroyPopups(player);
                prompt(player, "You may view your pending requests at any time.", "SKIN SUGGESTED");
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", player.transform.position);
                buttonsLeft(player);
            };
            Action<BasePlayer, string[]> cancel = (bPlayer, input) =>
            {
                destroyPopups(player);
            };
            containerGUI.addImage("popup_Addnew", new Rectangle(501, 284, 918, 468, 1920, 1080, true), "popup_ADDNEWCATEGORY", GuiContainer.Layer.menu, null, FadeIn = 0, FadeIn = 0);
            containerGUI.addPanel("newnameheader", new Rectangle(755, 371, 362, 26, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("STEP 2: CHOOSE A CATEGORY", 10, new GuiColor(255, 255, 255, 0.8f)));

            containerGUI.addInput("newname", new Rectangle(755, 410, 373, 59, 1920, 1080, true), inputCallback, GuiContainer.Layer.menu, null, new GuiColor("white"), 15, new GuiText("", 20), 0, 0);
            containerGUI.addPanel("confirm", new Rectangle(689, 482, 524, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(67, 84, 37, 0.8f), 0, 0, new GuiText("PRESS ENTER TO CONFIRM", 20, new GuiColor(134, 190, 41, 0.8f)));
            containerGUI.addPanel("header", new Rectangle(755, 313, 373, 59, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("ADD A NEW CATEGORY", 20, new GuiColor(255, 255, 255, 0.8f)));
            containerGUI.addPlainButton("cancel", new Rectangle(688, 559, 524, 61, 1920, 1080, true), GuiContainer.Layer.menu, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CANCEL", 20, new GuiColor(162, 51, 46, 0.8f)), cancel);
            containerGUI.display(player);
        }

        //general purpose prompt
        public void prompt(BasePlayer player, string message, string header)
        {
            Action<BasePlayer, string[]> closeCallback = (bPlayer, input) =>
            {
                GuiTracker.getGuiTracker(player).destroyGui(PluginInstance, "gametip");
            };
            GuiContainer containerGUI = new GuiContainer(this, "gametip", "background");
            containerGUI.addImage("gametip_prompt", new Rectangle(501, 377, 918, 243, 1920, 1080, true), "popup_PROMPT", GuiContainer.Layer.overlay, null, FadeIn = 0, FadeIn = 0);
            containerGUI.addPanel("gametip_msg", new Rectangle(755, 469, 394, 56, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText(message, 10, new GuiColor(255, 255, 255, 0.8f)));
            containerGUI.addPanel("gametip_header", new Rectangle(800, 404, 318, 56, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText(header, 25, new GuiColor(255, 255, 255, 0.8f)));
            containerGUI.addPlainButton("gametip_close", new Rectangle(802, 536, 318, 56, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("CLOSE", 20, new GuiColor(162, 51, 46, 0.8f)), closeCallback);
            containerGUI.display(player);
        }

        //hooks
        public void closeUI(virtualContainer container)
        {
#if DEBUG
            container.player.ChatMessage("closing UI");
#endif
            GuiTracker.getGuiTracker(container.player).destroyGui(this, "background");
        }

        private void onItemInserted(virtualContainer container, Item item)
        {
            container.item = item;
#if DEBUG
            PrintToChat($"OnItemInserted: container:{container.uid}, owner:{container?.player?.displayName}, item:{item?.amount} x {item?.info?.displayName?.english}");
#endif
            Skinnable skinnable = skinsData.GetSkinnable(item.info.shortname);
            if(skinnable == null)
            {
#if DEBUG
                container.player.ChatMessage($"skinnable for {item.info.shortname} not found");
#endif
                return;
            }
            sendCategories(container.player, item, GetCategories(container.player, skinnable));
        }

        private void onItemRemoved(virtualContainer container, Item item)
        {           
#if DEBUG
            PrintToChat($"OnItemRemoved: container:{container.uid}, owner:{container?.player?.displayName}, item:{item?.amount} x {item?.info?.displayName?.english}");
#endif
            GuiTracker.getGuiTracker(container.player).destroyGui(this, "categories");
        }

        #endregion

        #region commands

        private void approvedCommand(ConsoleSystem.Arg arg)
        {
            ulong? skinID = null; 
            if(arg.Args.Length > 0)
            {
                skinID = ulong.Parse(arg.Args[0]);
            }

            if(skinID != null)
            {
                if (ApprovedSkins.Contains(skinID.Value))
                {
                    Puts($"skin: {skinID} is approved");
                }
                else
                {
                    Puts($"skin: {skinID} is not approved");
                }
            }
        }

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
            
            if(args.Length < 1)
            {
                virtualContainer container = virtualContainer.find(player);
                if (container != null) Puts($"Skin-it: {player.displayName} already has a vContainer... this shouldn't happen");
                container = player.gameObject.AddComponent<virtualContainer>();
                container.init(player);
                timer.Once(0.5f, () => container.send());
            }
            else
            {
                switch (args[0])
                {
                    case "add":
                        string answer = addCommand(args.Skip(1).ToArray());
                        if (answer != null) player.ChatMessage(answer);
                        break;
                }
            }
        }

        private void addCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null) return;
            if(!arg.IsAdmin)
            {
                BasePlayer player = arg.Player();
                if (!permission.UserHasPermission(player.UserIDString, "skinit.use"))
                {
                    PrintToChat(player, lang.GetMessage("noPermission", this, player.UserIDString));
                    return;
                }
            }
            string answer = addCommand(arg.Args);
            if (answer != null) SendReply(arg, answer);
        }

        private string addCommand(string[] args)
        {
            if (args.Length < 1) return null;
            ulong temp;
            string category = null;
            if (!ulong.TryParse(args[0], out temp)) category = args[0];
            List<ulong> IDs = new List<ulong>();
            for (int i = (category == null) ? 0 : 1; i < args.Length; i++)
            {
                IDs.Add(ulong.Parse(args[i]));
            }
            ServerMgr.Instance.StartCoroutine(AddSkins(IDs, (category == null)?config.defaultCatName:args[0]));
            return $"adding {IDs.Count} {((IDs.Count == 1)?"skin":"skins")}";
        }
        #endregion

        #region helpers

        public Item applySkin(virtualContainer container, Item item, ulong skinID)
        {
            item.skin = skinID;
            item.MarkDirty();
            BaseEntity heldEntity = item.GetHeldEntity();
            if (heldEntity != null)
            {
                heldEntity.skinID = skinID;
                heldEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }
            Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", container.player.transform.position);
            return item;
            //Item newItem = ItemManager.Create(item.info, item.amount, skinID);
            //List<Item> contentBackup = new List<Item>();
            //if(item.contents != null)
            //{
            //    foreach (Item i in item.contents.itemList)
            //    {
            //        contentBackup.Add(i);
            //    }
            //    foreach (Item i in contentBackup)
            //    {
            //        newItem.contents.AddItem(i.info, i.amount);
            //    }
            //}

            //if (item.hasCondition)
            //{
            //    newItem._maxCondition = item._maxCondition;
            //    newItem._condition = item._condition;
            //}

            //BaseProjectile oldGun = item.GetHeldEntity() as BaseProjectile;
            //BaseProjectile newGun = newItem.GetHeldEntity() as BaseProjectile;
            //if (newGun != null && oldGun != null)
            //{
            //    newGun.primaryMagazine.ammoType = oldGun.primaryMagazine.ammoType;
            //    newGun.primaryMagazine.contents = oldGun.primaryMagazine.contents;
            //}
            //item.Remove();
            //newItem.position = slot;
            //newItem.parent = container.itemContainer;

            //container.itemContainer.itemList.Add(newItem);
            //newItem.MarkDirty();
            //Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", container.player.transform.position);
            //return newItem;
        }

        private IEnumerator AddSkins(List<ulong> IDs, string category, bool cfg = true)
        {
            foreach(ulong ID in IDs)
            {
                if(IsApproved(ID))
                {
                    Puts($"purging approved skin from config! ({ID})");
                    RemoveSkinFromConfig(ID);
                    continue;
                }
                skinWebRequest(ID, (s) => addSkin(s, category, cfg), (id) => RemoveSkinFromConfig(id));
                yield return null;
            }
        }

        private void addSkin(Skin skin, string category, bool cfg = true)
        {
            if(skin == null)
            {
                return;
            }

            if (IsApproved(skin.id))
            {
                Puts($"trying to add approved skin! ({skin.id})");
                return;
            }

            skin.category = category ?? config.defaultCatName;
            category = skin.category;

            Skinnable item = skinsData.GetSkinnable(skin.shortname);
            if (item == null)
            {
                item = new Skinnable(skin.shortname);
                skinsData.items.Add(item);
            }
            Category cat = skinsData.GetCategory(item, skin.category);
            if (cat == null)
            {
                cat = new Category(skin.category, skin.shortname);
                if(cat.name == config.defaultCatName)
                {
                    List<Category> newList = new List<Category>();
                    newList.Add(cat);
                    newList.AddRange(item.categories);
                    item.categories = newList;
                }
                else
                {
                    item.categories.Add(cat);
                }
            }
            if (skinsData.GetSkin(cat, skin.id) == null)
            {
                cat.skins.Add(skin);
                guiCreator.registerImage(this, skin.safename, skin.url, imgSizeX: config.imgRes, imgSizeY: config.imgRes);
            }
            saveSkinsData();

            if (cfg)
            {
                if (!config.skins.ContainsKey(skin.shortname))
                {
                    config.skins.Add(skin.shortname, new Dictionary<string, List<ulong>>());
                }
                if (!config.skins[skin.shortname].ContainsKey(category))
                {
                    if(category == config.defaultCatName)
                    {
                        Dictionary<string, List<ulong>> newDict = new Dictionary<string, List<ulong>>();
                        newDict.Add(category, new List<ulong>());
                        foreach(string cat_ in config.skins[skin.shortname].Keys)
                        {
                            newDict.Add(cat_, config.skins[skin.shortname][cat_]);
                        }
                        config.skins[skin.shortname] = newDict;
                    }
                    else
                    {
                        config.skins[skin.shortname].Add(category, new List<ulong>());
                    }
                }
                if (!config.skins[skin.shortname][category].Contains(skin.id)) config.skins[skin.shortname][category].Add(skin.id);
                SaveConfig();
            }

            //discord message
            _DiscordCoreAPI.SendMessageToChannel(config.discordChannel, $"Skin: \"{skin.name}\" has been added to category {skin.category} in skinbox!\n{skin.url}");
        }

        private void removeSkin(Skin skin)
        {
            config.skins[skin.shortname][skin.category].Remove(skin.id);
            if (config.skins[skin.shortname][skin.category].Count == 0) config.skins[skin.shortname].Remove(skin.category);
            if (config.skins[skin.shortname].Count == 0) config.skins.Remove(skin.shortname);
            SaveConfig();
            skin.categoryObject.skins.Remove(skin);
            if (skin.categoryObject.skins.Count == 0) skin.skinnable.categories.Remove(skin.categoryObject);
            if (skin.skinnable.categories.Count == 0) skinsData.items.Remove(skin.skinnable);
            saveSkinsData();
        }

        private void RemoveSkinFromConfig(ulong id)
        {
            foreach(var skinnable in config.skins)
            {
                foreach(var category in skinnable.Value)
                {
                    foreach(var skin in category.Value)
                    {
                        if (skin == id)
                        {
                            category.Value.Remove(id);
                            SaveConfig();
                            return;
                        }
                    }
                }
            }
        }

        private void changeSkinCategory(Skin skin, string newCategory)
        {
            removeSkin(skin);
            addSkin(skin, newCategory);
        }

        private bool buySkin(virtualContainer container, Item item, Skin skin)
        {
            Category category = skin.categoryObject;
            if (!canSkin(container.player, item, category)) return false;
            takePoints(container.player, getCost(container.player, item, category));
            Item newItem = applySkin(container, item, skin.id);
            onItemInserted(container, newItem);
            return true;
        }

        private List<Category> GetCategories(BasePlayer player, Skinnable item)
        {
            if (item == null) return null;
            List<Category> output = new List<skinit.Category>();
            foreach(Category cat in item.categories)
            {
                if(config.hideCatsWithoutPerm)
                {
                    if (!hasPermission(player, cat)) continue;
                }
                output.Add(cat);
            }
            return output;
        }

        private bool isAdmin(BasePlayer player)
        {
            if (player.IsAdmin) return true;
            if (player.IPlayer.HasPermission("skinit.admin")) return true;
            return false;
        }

        #region permissions

        private bool canSkin(BasePlayer player, Item item, Category category)
        {
            int cost;
            if (!hasPermission(player, item, category, out cost)) return false;
            if (getPoints(player) < cost) return false;
            return true;
        }

        private bool hasPermission(BasePlayer player, Item item, Category category)
        {
            int temp;
            return hasPermission(player, item, category, out temp);
        }

        private int getCost(BasePlayer player, Item item, Category category)
        {
            if (player.IPlayer.HasPermission("skinit.free")) return 0;
            int cost;
            hasPermission(player, item, category, out cost);
            return cost;
        }

        private bool hasPermission(BasePlayer player, Item item, Category category, out int cost)
        {
            cost = 0;
            switch (item.info.category)
            {
                case ItemCategory.Attire:
                    if (!player.IPlayer.HasPermission("skinit.attire")) return false;
                    cost = config.costAttire;
                    break;
                case ItemCategory.Construction:
                case ItemCategory.Items:
                    if (!player.IPlayer.HasPermission("skinit.deployable")) return false;
                    cost = config.costDeployable;
                    break;
                case ItemCategory.Tool:
                    if (!player.IPlayer.HasPermission("skinit.tool")) return false;
                    cost = config.costTool;
                    break;
                case ItemCategory.Weapon:
                    if (!player.IPlayer.HasPermission("skinit.weapon")) return false;
                    cost = config.costWeapon;
                    break;
            }
            if (!hasPermission(player, category)) return false;
            return true;
        }

        private bool hasPermission(BasePlayer player, Category category)
        {
            if(category.perm)
            {
                if (!player.IPlayer.HasPermission($"skinit.category.{category.safename}")) return false;
            }
            return true;
        }

        private bool toggleCategoryPerm(Category category)
        {
            if (category.perm)
            {
                category.perm = false;
                return false;
            }
            else
            {
                category.perm = true;
                permission.RegisterPermission($"skinit.category.{category.safename}", this);
                return true;
            }
        }

        #endregion

        #region Points

        private int getPoints(BasePlayer player)
        {
            if (config.useServerRewards)
            {
                object answer = ServerRewards?.Call<int>("CheckPoints", player.userID);
                if (answer != null) return (int)answer;
            }
            else if (config.useEconomics)
            {
                object answer = Economics?.Call<int>("Balance", player.userID);
                if (answer != null) return (int)answer;
            }
            return 0;
        }

        private bool takePoints(BasePlayer player, int amount)
        {
            if (config.useServerRewards)
            {
                object answer = ServerRewards?.Call<int>("TakePoints", player.userID, amount);
                if (answer == null) return false;
                else return true;
            }
            else if (config.useEconomics)
            {
                object answer = Economics?.Call<int>("Withdraw", player.userID, (double)amount);
                if (answer == null) return false;
            }
            return false;
        }

        #endregion

        #endregion

        #region steamworks api

        public class tag_
        {
            public string tag;
        }

        public class publishedFile
        {
            public string publishedfileid;
            public int result;
            public string creator;
            public ulong creator_app_id;
            public ulong consumer_app_id;
            public string filename;
            public ulong file_size;
            public string preview_url;
            public string hcontent_preview;
            public string title;
            public string description;
            public ulong time_created;
            public ulong time_updated;
            public int visibility;
            public int banned;
            public string ban_reason;
            public int subscriptions;
            public int favourited;
            public int lifetime_subscriptions;
            public int lifetime_favourited;
            public uint views;
            public List<tag_> tags;
        }

        public class steamAnswer
        {
            public int result;
            public int resultcount;
            public List<publishedFile> publishedfiledetails;

        }

        public class webResponse
        {
            public steamAnswer response;
        }

        private void skinWebRequest(ulong ID, Action<Skin> callback, Action<ulong> errorCallback = null)
        {
            string body = $"itemcount=1&publishedfileids%5B0%5D={ID}";
            webrequest.Enqueue("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body, (code, response) =>
            {
#if DEBUG
                Puts($"Response code: {code}");
#endif
                if (code != 200 || response == null)
                {
                    Puts($"Coudn't get skin info for ID {ID}!");
                    return;
                }
                webResponse answer = JsonConvert.DeserializeObject<webResponse>(response);
                if (answer?.response?.publishedfiledetails == null)
                {
                    Puts("answer is null");
                    callback(null);
                    return;
                }
                if(answer.response.publishedfiledetails[0]?.title == null)
                {
                    Puts($"Skin ID {ID} doesn't exist!");
                    errorCallback(ID);
                    return;
                }
                //Puts($"got skin info: {(answer.response.publishedfiledetails[0].title)} for ID {ID}!");
                if (answer.response.publishedfiledetails.Count < 1)
                {
                    Puts("0 publishedfiledetails in response!");
                    errorCallback(ID);
                    return;
                }
                publishedFile pf = answer.response.publishedfiledetails[0];
                string shortname = null;
                foreach (tag_ t in pf.tags)
                {
                    if (shortnames.ContainsKey(t.tag))
                    {
                        shortname = shortnames[t.tag];
                        break;
                    }
                }
                if (shortname == null)
                {
                    Puts("no shortname found in publishedfiledetails!");
                    errorCallback(ID);
                    return;
                }
                Skin s = new Skin(pf.title, config.defaultCatName, shortname, ulong.Parse(pf.publishedfileid), pf.preview_url);
                callback(s);
            }, this, RequestMethod.POST);
        }

        #endregion

        #region data management

        #region skinsData
        private class SkinsData
        {
            public List<Skinnable> items = new List<skinit.Skinnable>();

            public SkinsData()
            {
            }

            private bool registryInProgress = false;

            public IEnumerator RegisterImages()
            {
                if (registryInProgress)
                {
                    yield break;
                }

                foreach (Skinnable item in items)
                {
#if DEBUG2
                    PluginInstance.Puts($"re-registering categories for {item.shortname}");
#endif
                    foreach(Category cat in item.categories)
                    {
#if DEBUG2
                        PluginInstance.Puts($"re-registering skins in {cat.name}");
#endif
                        foreach (Skin skin in cat.skins)
                        {
#if DEBUG2
                            PluginInstance.Puts($"re-registering {skin.safename}");
#endif
                            registryInProgress = true;
                            Action callback = () =>
                            {
                                registryInProgress = false;
                            };
                            PluginInstance.guiCreator.registerImage(PluginInstance, skin.safename, skin.url, callback, imgSizeX: config.imgRes, imgSizeY: config.imgRes);
                        }
                    }
                }
            }

            public Skinnable GetSkinnable(string shortname)
            {
                foreach(Skinnable item in items)
                {
                    if (item.shortname == shortname) return item;
                }
                return null;
            }

            public Category GetCategory(Skinnable item, string name)
            {
                foreach(Category category in item.categories)
                {
                    if (category.name == name) return category;
                }
                return null;
            }

            public Skin GetSkin(Skinnable item, ulong id)
            {
                foreach(Category category in item.categories)
                {
                    Skin skin = GetSkin(category, id);
                    if (skin != null) return skin;
                }
                return null;
            }

            public Skin GetSkin(Category category, ulong id)
            {
                foreach(Skin skin in category.skins)
                {
                    if (skin.id == id) return skin;
                }
                return null;
            }
            public Skin GetSkin(ulong id)
            {
                foreach(Skinnable item in items)
                {
                    Skin skin = GetSkin(item, id);
                    if (skin != null) return skin;
                }
                return null;
            }
        }

        void saveSkinsData()
        {
            try
            {
                SkinsFile.WriteObject(skinsData);
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

        void loadSkinsData()
        {
            try
            {
                skinsData = SkinsFile.ReadObject<SkinsData>();
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

#endregion

        #region requestsData
        private class RequestData
        {
            public Queue<Request> requests = new Queue<skinit.Request>();

            public RequestData()
            {
            }

            public bool addRequest(Request request)
            {
                if (PluginInstance.skinsData.GetSkin(request.skinID) != null) return false;
                if (getRequest(request.skinID) != null) return false;
                if (getPendingRequests(request.userID).Count >= config.maxPendingReq) return false;
                BasePlayer player = BasePlayer.FindByID(request.userID);
                PluginInstance.LogToFile("suggestions", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} [{player.userID}]{player.displayName} suggested {request.skinID}:{request.skin.name} for Category {request.category}", PluginInstance);
                requests.Enqueue(request);
                PluginInstance.saveRequestsData();
                return true;
            }

            public Request getRequest(ulong skinID)
            {
                foreach (Request req in requests)
                {
                    if (req.skinID == skinID) return req;
                }
                return null;
            }

            public void returnRequest(Request request)
            {
                Queue<Request> newQueue = new Queue<Request>();
                newQueue.Enqueue(request);
                foreach(Request req in requests)
                {
                    newQueue.Enqueue(req);
                }
                requests = newQueue;
                PluginInstance.saveRequestsData();
            }

            public Request getNextRequest()
            {
                Request output = null;
                requests.TryDequeue(out output);
                PluginInstance.saveRequestsData();
                return output;
            }

            public List<Request> getPendingRequests(ulong userID)
            {
                List<Request> output = new List<skinit.Request>();
                foreach (Request req in requests)
                {
                    if (req.userID == userID) output.Add(req);
                }
                return output;
            }
        }

        void saveRequestsData()
        {
            try
            {
                RequestsFile.WriteObject(requestData);
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

        void loadRequestsData()
        {
            try
            {
                requestData = RequestsFile.ReadObject<RequestData>();
            }
            catch (Exception E)
            {
                Puts(E.ToString());
            }
        }

#endregion

        #endregion

        #region Config
        private static ConfigData config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Discord Announcement channel")]
            public string discordChannel;

            [JsonProperty(PropertyName = "Skin image resolution")]
            public int imgRes;

            [JsonProperty(PropertyName = "Allow approved skins")]
            public bool allowApprovedSkins;

            [JsonProperty(PropertyName = "Chat Command")]
            public string command;

            [JsonProperty(PropertyName = "Default Category Name")]
            public string defaultCatName;

            [JsonProperty(PropertyName = "Allow Suggestions")]
            public bool allowSuggestions;

            [JsonProperty(PropertyName = "Use Server Rewards")]
            public bool useServerRewards;

            [JsonProperty(PropertyName = "Use Economics")]
            public bool useEconomics;

            [JsonProperty(PropertyName = "Hide Categories that the User doesn't have Permission for")]
            public bool hideCatsWithoutPerm;

            [JsonProperty(PropertyName = "Maximum pending requests per player")]
            public int maxPendingReq;

            [JsonProperty(PropertyName = "Attire Cost")]
            public int costAttire;

            [JsonProperty(PropertyName = "Deployable Cost")]
            public int costDeployable;

            [JsonProperty(PropertyName = "Tool Cost")]
            public int costTool;

            [JsonProperty(PropertyName = "Weapon Cost")]
            public int costWeapon;

            [JsonProperty(PropertyName = "Skins")]
            public Dictionary<string, Dictionary<string, List<ulong>>> skins;

        }

        private ConfigData getDefaultConfig()
        {
            ConfigData output = new ConfigData
            {
                discordChannel = "discord-bot-testing",
                imgRes = 200,
                allowApprovedSkins = false,
                command = "skinit",
                defaultCatName = "default",
                allowSuggestions = true,
                useServerRewards = true,
                useEconomics = false,
                hideCatsWithoutPerm = false,
                maxPendingReq = 7,
                costAttire = 5,
                costDeployable = 10,
                costTool = 15,
                costWeapon = 20,
            };
            output.skins = new Dictionary<string, Dictionary<string, List<ulong>>>
            {
                {"rock", new Dictionary<string, List<ulong>>
                    {
                        { output.defaultCatName, new List<ulong>
                            {
                                1530140666,
                                1435229543,
                                1636101879
                            }
                        }
                    }
                }
            };
            return output;
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

        protected override void LoadDefaultConfig() => config = getDefaultConfig();
#endregion

        #region Localization
        Dictionary<string, string> messages = new Dictionary<string, string>()
        {
            {"noPermission", "You don't have permission to use this command!"}
        };
        #endregion

        #region approved skins

        private List<ulong> ApprovedSkins = new List<ulong>();

        private IEnumerator InitApprovedSkins()
        {
            yield return new WaitWhile(() => Steamworks.SteamInventory.Definitions == null);

            foreach (InventoryDef item in Steamworks.SteamInventory.Definitions)
            {
                if (item == null) continue;

                ulong workshopid;
                if (!ulong.TryParse(item.GetProperty("workshopid"), out workshopid)) continue;

                ApprovedSkins.Add(workshopid);
            }

#if DEBUG2
            Puts($"Approved skins initialised. count: {ApprovedSkins.Count}");
#endif
        }

        private bool IsApproved(ulong skinId)
        {
            if (config.allowApprovedSkins) return false;
            return ApprovedSkins.Contains(skinId);
        }

        #endregion

        #region shortname LUT

        private static Dictionary<string, string> shortnames = new Dictionary<string, string>
            {
                {"Acoustic Guitar", "fun.guitar"},
                {"AK47", "rifle.ak"},
                {"Armored Door", "door.hinged.toptier"},
                {"Armored Double Door", "door.double.hinged.toptier"},
                {"Balaclava", "mask.balaclava"},
                {"Bandana", "mask.bandana"},
                {"Bearskin Rug", "rug.bear" },
                {"Beenie Hat", "hat.beenie"},
                {"Bolt Rifle", "rifle.bolt"},
                {"Bone Club", "bone.club"},
                {"Bone Knife", "knife.bone"},
                {"Boonie Hat", "hat.boonie"},
                {"Bucket Helmet", "bucket.helmet"},
                {"Burlap Headwrap", "burlap.headwrap"},
                {"Burlap Pants", "burlap.trousers"},
                {"Burlap Shirt", "burlap.shirt"},
                {"Burlap Shoes", "burlap.shoes"},
                {"Cap", "hat.cap"},
                {"Chair", "chair" },
                {"Coffee Can Helmet", "coffeecan.helmet"},
                {"Collared Shirt", "shirt.collared"},
                {"Combat Knife", "knife.combat"},
                {"Concrete Barricade", "barricade.concrete"},
                {"Crossbow", "crossbow"},
                {"Custom SMG", "smg.2"},
                {"Deer Skull Mask", "deer.skull.mask"},
                {"Double Barrel Shotgun", "shotgun.double"},
                {"Eoka Pistol", "pistol.eoka"},
                {"F1 Grenade", "grenade.f1"},
                {"Fridge", "fridge" },
                {"Furnace", "furnace" },
                {"Garage Door", "wall.frame.garagedoor"},
                {"Hammer", "hammer"},
                {"Hatchet", "hatchet"},
                {"Hide Halterneck", "attire.hide.helterneck"},
                {"Hide Pants", "attire.hide.pants"},
                {"Hide Poncho", "attire.hide.poncho"},
                {"Hide Shirt", "attire.hide.vest"},
                {"Hide Shoes", "attire.hide.boots"},
                {"Hide Skirt", "attire.hide.skirt"},
                {"Hoodie", "hoodie"},
                {"Large Wood Box", "box.wooden.large"},
                {"Leather Gloves", "burlap.gloves"},
                {"Locker", "locker" },
                {"Long TShirt", "tshirt.long"},
                {"Longsword", "longsword"},
                {"LR300", "rifle.lr300"},
                {"M249", "lmg.m249" },
                {"M39", "rifle.m39" },
                {"Metal Chest Plate", "metal.plate.torso"},
                {"Metal Facemask", "metal.facemask"},
                {"Miner Hat", "hat.miner"},
                {"Mp5", "smg.mp5"},
                {"Pants", "pants"},
                {"Pick Axe", "pickaxe"},
                {"Pump Shotgun", "shotgun.pump"},
                {"Python", "pistol.python"},
                {"Reactive Target", "target.reactive"},
                {"Revolver", "pistol.revolver"},
                {"Riot Helmet", "riot.helmet"},
                {"Roadsign Pants", "roadsign.kilt"},
                {"Roadsign Vest", "roadsign.jacket"},
                {"Rock", "rock"},
                {"Rocket Launcher", "rocket.launcher"},
                {"Rug", "rug" },
                {"Salvaged Hammer", "hammer.salvaged"},
                {"Salvaged Icepick", "icepick.salvaged"},
                {"Sandbag Barricade", "barricade.sandbags"},
                {"Satchel Charge", "explosive.satchel"},
                {"Semi-Automatic Pistol", "pistol.semiauto"},
                {"Semi-Automatic Rifle", "rifle.semiauto"},
                {"Sheet Metal Door", "door.hinged.metal"},
                {"Sheet Metal Double Door", "door.double.hinged.metal"},
                {"Shorts", "pants.shorts"},
                {"Sleeping Bag", "sleepingbag"},
                { "Spinning Wheel", "spinner.wheel"},
                {"Snow Jacket", "jacket.snow"},
                {"Stone Hatchet", "stonehatchet"},
                {"Stone Pick Axe", "stone.pickaxe"},
                {"Sword", "salvaged.sword"},
                {"Table", "table" },
                {"Tank Top", "shirt.tanktop"},
                {"Thompson", "smg.thompson"},
                {"TShirt", "tshirt"},
                {"Vagabond Jacket", "jacket"},
                {"Vending Machine", "vending.machine"},
                {"Water Purifier", "water.purifier"},
                {"Waterpipe Shotgun", "shotgun.waterpipe"},
                {"Wood Storage Box", "box.wooden"},
                {"Wooden Door", "door.hinged.wood"},
                {"Wooden Double Door", "door.double.hinged.wood"},
                {"Work Boots", "shoes.boots"},
                {"Boots Skin", "shoes.boots" }
            };

        #endregion

        #region discord

        [PluginReference]
        private Plugin DiscordCore;

        private static DiscordCoreAPI _DiscordCoreAPI;

        void InitDiscord()
        {
            if (_DiscordCoreAPI?.initialized ?? false) return;
            if (DiscordCore == null)
            {
                Puts("Discord Core is not loaded! get it here https://umod.org/plugins/discord-core");
                return;
            }
            _DiscordCoreAPI = new DiscordCoreAPI();
            if (_DiscordCoreAPI.IsReady())
            {
                _DiscordCoreAPI.initialized = true;
            }
        }

        #region hooks

        private void OnDiscordCoreReady()
        {
            InitDiscord();
        }

        private void OnDiscordCoreJoin(IPlayer player)
        {

        }

        private void OnDiscordCoreLeave(IPlayer player)
        {

        }

        private void OnDiscordChat(IPlayer player, string message)
        {

        }

        private void OnGetChannelMessages(List<Message> messages, string responseKey)
        {
            _DiscordCoreAPI.ExecuteCallback(responseKey, messages);
        }

        #endregion

        #region commands

        #endregion

        public class DiscordCoreAPI
        {
            public bool initialized { get; set; } = false;

            private Dictionary<string, Action<List<Message>>> Callbacks = new Dictionary<string, Action<List<Message>>>();

            public void RegisterCallback(string responseKey, Action<List<Message>> callback)
            {
                if (!Callbacks.ContainsKey(responseKey)) Callbacks.Add(responseKey, callback);
            }

            public void ExecuteCallback(string responseKey, List<Message> messages)
            {
                if (Callbacks.ContainsKey(responseKey))
                {
                    try
                    {
                        Callbacks[responseKey]?.Invoke(messages);
                    }
                    catch (Exception e)
                    {
                        PluginInstance.Puts($"DiscordCoreAPI: Failed to execute callback: {responseKey}\n{e.Message}");
                    }
                    Callbacks.Remove(responseKey);
                }
            }

            public bool IsReady()
            {
                object response = PluginInstance.DiscordCore.Call(nameof(IsReady));
                if (response == null) return false;
                return (bool)response;
            }

            public void UpdatePresence(Presence presence)
            {
                PluginInstance.DiscordCore.Call(nameof(UpdatePresence), presence);
            }

            public void RegisterCommand(string command, Plugin plugin, Func<IPlayer, string, string, string[], object> method, string helpText, string permission = null, bool allowInChannel = false)
            {
                PluginInstance.DiscordCore.Call(nameof(RegisterCommand), command, plugin, method, helpText, permission, allowInChannel);
            }

            public GuildMember GetGuildMember(string steamId)
            {
                object response = PluginInstance.DiscordCore.Call(nameof(GetGuildMember), steamId);
                if (response == null) return null;
                GuildMember guildMember = response as GuildMember;
                return guildMember;
            }

            #region messages

            public void SendMessageToChannel(string channelNameOrId, string message)
            {
                PluginInstance.DiscordCore.Call(nameof(SendMessageToChannel), channelNameOrId, message);
            }

            public void SendMessageToChannel(string channelNameOrId, Message message)
            {
                PluginInstance.DiscordCore.Call(nameof(SendMessageToChannel), channelNameOrId, message);
            }

            public void SendMessageToChannel(string channelNameOrId, Embed message)
            {
                PluginInstance.DiscordCore.Call(nameof(SendMessageToChannel), channelNameOrId, message);
            }

            public void SendMessageToUser(string id, string message)
            {
                PluginInstance.DiscordCore.Call(nameof(SendMessageToUser), id, message);
            }

            public void SendMessageToUser(IPlayer player, string message)
            {
                PluginInstance.DiscordCore.Call(nameof(SendMessageToUser), player, message);
            }

            public void DeleteMessage(Message message)
            {
                PluginInstance.DiscordCore.Call(nameof(DeleteMessage), message);
            }

            #endregion

            #region channels

            public List<Channel> GetAllChannels()
            {
                object response = PluginInstance.DiscordCore.Call(nameof(GetAllChannels));
                if (response != null)
                {
                    return response as List<Channel>;
                }
                return null;
            }

            public Channel GetChannel(string nameOrId)
            {
                object response = PluginInstance.DiscordCore.Call(nameof(GetChannel), nameOrId);
                if (response != null)
                {
                    return response as Channel;
                }
                return null;
            }

            public void GetChannelMessages(string nameOrId, string responseKey)
            {
                PluginInstance.DiscordCore.Call(nameof(GetChannelMessages), nameOrId, responseKey);
            }

            public void SubscribeChannel(string channelNameOrId, Plugin plugin, Func<Message, object> method)
            {
                PluginInstance.DiscordCore.Call(nameof(SubscribeChannel), channelNameOrId, plugin, method);
            }

            public void UnsubscribeChannel(string channelNameOrId, Plugin plugin)
            {
                PluginInstance.DiscordCore.Call(nameof(UnsubscribeChannel), channelNameOrId, plugin);
            }

            #endregion

            #region Roles

            public Role GetRole(string nameOrId)
            {
                object response = PluginInstance.DiscordCore.Call(nameof(GetRole), nameOrId);
                if (response == null) return null;
                Role role = response as Role;
                return role;
            }

            public bool? UserHasRole(string userId, string nameOrId)
            {
                object response = PluginInstance.DiscordCore.Call(nameof(UserHasRole), userId, nameOrId);
                if (response == null) return null;
                bool truth = (bool)response;
                return truth;
            }

            public void AddRoleToUser(string userId, string nameOrId)
            {
                PluginInstance.DiscordCore.Call(nameof(AddRoleToUser), userId, nameOrId);
            }

            public void RemoveRoleFromUser(string userId, string nameOrId)
            {
                PluginInstance.DiscordCore.Call(nameof(RemoveRoleFromUser), userId, nameOrId);
            }

            public void CreateGuildRole(Role role)
            {
                PluginInstance.DiscordCore.Call(nameof(CreateGuildRole), role);
            }

            public void DeleteGuildRole(Role role)
            {
                PluginInstance.DiscordCore.Call(nameof(CreateGuildRole), role);
            }

            public void DeleteGuildRole(string roleId)
            {
                PluginInstance.DiscordCore.Call(nameof(CreateGuildRole), roleId);
            }

            #endregion
        }


        #endregion
    }
}