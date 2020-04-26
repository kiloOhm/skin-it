﻿// Requires: GUICreator

#define DEBUG
//#define DEBUG2
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Oxide.Plugins.GUICreator;

namespace Oxide.Plugins
{
    [Info("skinit", "Ohm & Bunsen", "0.2.0")]
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


        private const int slot = 19;
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
                Request instance = this;
                PluginInstance.skinWebRequest(skinID, (skin) => 
                {
                    instance.skin = skin;
                    PluginInstance.saveRequestsData();
                });
            }

            public void approve(string category = null)
            {
                PluginInstance.addSkin(skin, category);
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
            public string safename => Regex.Replace(name, " ", "_");
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

                player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "generic");

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
            #region config processing

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
                addSkins(toBeAdded[cat], cat, false);
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

            #endregion


            //references
            guiCreator = (GUICreator)Manager.GetPlugin("GUICreator");
            if (config.useServerRewards && ServerRewards == null) Puts("ServerRewards not loaded! get it at https://umod.org/plugins/server-rewards");
            if ((config.useServerRewards || config.useEconomics) && Economics == null) Puts("Economics not loaded! get it at https://umod.org/plugins/economics");
            if (guiCreator == null)
            {
                Puts("GUICreator missing! This shouldn't happen");
                return;
            }

            //commands
            cmd.AddChatCommand(config.command, this, nameof(skinitCommand));
            cmd.AddChatCommand("test", this, nameof(testCommand));
            cmd.AddConsoleCommand("skinit.test", this, nameof(testConsoleCommand));
            cmd.AddConsoleCommand("skinit.add", this, nameof(addCommand));

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
            guiCreator.registerImage(this, "smile", "https://b2.pngbarn.com/png/341/447/785/emoji-with-mask-corona-coronavirus-convid-yellow-facial-expression-emoticon-nose-smile-head-png-clip-art-thumbnail.png");
            guiCreator.registerImage(this, "sad", "https://s3.amazonaws.com/pix.iemoji.com/images/emoji/apple/ios-12/256/loudly-crying-face.png");

            //lang
            lang.RegisterMessages(messages, this);

            //hooks
            Unsubscribe(nameof(CanAcceptItem));
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

        public void sendUI(virtualContainer container)
        {
            StringBuilder sb = new StringBuilder();
            if (container.player.IPlayer.HasPermission("skinit.attire")) sb.Append("attire ");
            if (container.player.IPlayer.HasPermission("skinit.deployable")) sb.Append("deployables ");
            if (container.player.IPlayer.HasPermission("skinit.tool")) sb.Append("tools ");
            if (container.player.IPlayer.HasPermission("skinit.weapon")) sb.Append("weapons ");
            string skinPermissions = sb.ToString().ToUpper();
            skinPermissions.Trim();
            skinPermissions = Regex.Replace(skinPermissions, " ", ", ");
#if DEBUG
            container.player.ChatMessage("sending UI");
#endif
            GuiContainer containerGUI = new GuiContainer(this, "background");
            containerGUI.addImage("GUI_1_1", new Rectangle(0, 0, 392, 30, 1921, 1080, true), "GUI_1_1", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_2", new Rectangle(392, 0, 271, 837, 1921, 1081, true), "GUI_1_2", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_3", new Rectangle(663, 0, 562, 576, 1921, 1081, true), "GUI_1_3", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_4", new Rectangle(1225, 0, 695, 643, 1921, 1081, true), "GUI_1_4", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_5", new Rectangle(0, 30, 134, 807, 1921, 1081, true), "GUI_1_5", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_6", new Rectangle(1225, 643, 133, 89, 1921, 1081, true), "GUI_1_8", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_7", new Rectangle(1439, 643, 481, 89, 1921, 1081, true), "GUI_1_10", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_8", new Rectangle(1225, 732, 695, 322, 1921, 1081, true), "GUI_1_11", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_9", new Rectangle(134, 814, 258, 23, 1921, 1081, true), "GUI_1_12", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_10", new Rectangle(0, 837, 74, 243, 1921, 1081, true), "GUI_1_13", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_11", new Rectangle(631, 837, 32, 71, 1921, 1081, true), "GUI_1_15", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_12", new Rectangle(74, 908, 589, 172, 1921, 1081, true), "GUI_1_16", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_13", new Rectangle(663, 1054, 1257, 26, 1921, 1081, true), "GUI_1_17", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_Fill_1", new Rectangle(1204, 146, 39, 430, 1921, 1081, true), "GUI_1_Fill_1", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_Fill_2", new Rectangle(0, 0, 134, 92, 1921, 1081, true), "GUI_1_Fill_2", GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            // containerGUI.addImage("GUI_1_14", new Rectangle(134, 30, 258, 784, 1921, 1081, true), "GUI_1_6", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_15", new Rectangle(663, 576, 562, 478, 1921, 1081, true), "GUI_1_7", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_16", new Rectangle(1358, 643, 81, 89, 1921, 1081, true), "GUI_1_9", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            containerGUI.addImage("GUI_1_17", new Rectangle(74, 837, 557, 71, 1921, 1081, true), "GUI_1_14", GuiContainer.Layer.under, null, FadeIn, FadeOut);
            containerGUI.addImage("Text_1", new Rectangle(1334, 925, 460, 121, 1920, 1080, true), "Text_1", GuiContainer.Layer.overlay, null, FadeIn, FadeOut);
            containerGUI.addPanel("Text_Permissions_1", new Rectangle(80, 937, 471, 43, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("According to your permissions, you may skin...", 15, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_Permissions_2", new Rectangle(80, 975, 471, 43, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{skinPermissions}", 18, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_2", new Rectangle(1454, 629, 321, 115, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("ITEM TO BE SKINNED", 23, new GuiColor(255, 255, 255, 0.3f)));
            containerGUI.display(container.player);
            panelOneBackground(container.player);
            skinitButton(container);

        }

        public enum buttonStates {idle, noSelected, noPermission, cantAfford, success};
        public void skinitButton(virtualContainer container, Skin activeSkin = null, Item item = null, buttonStates flag = buttonStates.idle)
        {
            BasePlayer player = container.player;
            Action<BasePlayer, string[]> Skinit = (bPlayer, input) =>
            {
                if(activeSkin == null || item == null)
                {
                    skinitButton(container, flag : buttonStates.noSelected);
                }
                if(buySkin(container, item, activeSkin))
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

            if (flag == buttonStates.success) 
            {
                containerGUI.addPlainButton("checkout_success", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("SUCCESS", 30, new GuiColor(134, 190, 41, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    skinitButton(container);
                });
            }
            else if (flag == buttonStates.noPermission) 
            {
                containerGUI.addPlainButton("checkout_failure", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("YOU DON'T HAVE PERMISSION", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    skinitButton(container, activeSkin, item);
                });
            }
            else if (flag == buttonStates.cantAfford)
            {
                containerGUI.addPlainButton("checkout_failure", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("YOU CAN'T AFFORD THIS SKIN", 20, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    skinitButton(container, activeSkin, item);
                });
            }
            else if (flag == buttonStates.noSelected)
            {
                containerGUI.addPlainButton("checkout_attempt", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("NO SKIN SELECTED!", 30, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    skinitButton(container);
                });
            }
            else // The initial state when panelOne is launched;
            {
                containerGUI.addPlainButton("checkout", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("SKIN-IT!", 30, new GuiColor(134, 190, 41, 0.8f)), Skinit);
            }
            containerGUI.display(player);
        }
    
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

        public void panelOneBackground(BasePlayer player) // also background for preview panel
        {
            GuiContainer containerGUI = new GuiContainer(this, "panelOneBackground", "background");
            containerGUI.addImage("availableSkinsPanel", new Rectangle(452, 32, 1021, 451, 1920, 1080, true), "availableSkinsPanel", GuiContainer.Layer.overlay, null, FadeIn, FadeOut);
            containerGUI.addImage("previewPanel", new Rectangle(1492, 32, 389, 451, 1920, 1080, true), "previewPanel", GuiContainer.Layer.overlay, null, FadeIn, FadeOut);
            containerGUI.addPanel("previewPanelText", new Rectangle(1501, 0, 371, 74, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("PREVIEW", 30, new GuiColor(255, 255, 255, 0.5f)));
            containerGUI.addPanel("availableSkinsPanelText", new Rectangle(696, 0, 534, 74, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("AVAILABLE SKINS", 30, new GuiColor(255, 255, 255, 0.5f)));
            containerGUI.display(player);
        }

        public void panelOne(BasePlayer player, Item item, List<List<Skin>> skinListOfLists, int activeSkin = 0, int page=0)
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
            foreach(Skin s in skinList)
            {
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    panelOne(player, item, skinListOfLists, activeSkin = index, page);
                    previewPanel(bPlayer, item, skinList[index]);
                    skinitButton(virtualContainer.find(player), skinList[index], item);
                };
                if (i<picturesEachRow)
                {
                    xSpacing = OriginX + (widthEach * i);
                    OriginY = OriginY1;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);


                }
                else if (i>=picturesEachRow && i<(picturesEachRow*2))
                {
                    xSpacing = (OriginX + (widthEach * i))-(widthEach*picturesEachRow);
                    OriginY = OriginY2;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                else if (i<totalPictures)
                {
                    xSpacing = (OriginX + (widthEach * i)) - ((widthEach * picturesEachRow)*2);
                    OriginY = OriginY3;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), skinList[i].safename, GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                i++;
            }
            
            Action<BasePlayer, string[]> GoRight = (bPlayer, input) =>
            {
                if (page == skinListOfLists.Count-1)
                {
                    page = 0;
                    panelOne(player, item, skinListOfLists, activeSkin = 0, page);

                } else
                {
                    page += 1;
                    panelOne(player, item, skinListOfLists, activeSkin = 0, page);
                }
                };
            Action<BasePlayer, string[]> GoLeft = (bPlayer, input) =>
            {
                if (page == 0)
                {
                    page = skinListOfLists.Count-1;
                    panelOne(player, item, skinListOfLists, activeSkin = 0, page);
                } else
                {
                    page -= 1;
                    panelOne(player, item, skinListOfLists, activeSkin = 0, page);
                }
                };
            if (skinListOfLists.Count > 1)
            {
                containerGUI.addPlainButton("goRight", new Rectangle(1437, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(">>", 25, new GuiColor(255, 255, 255, 0.8f)), GoRight);
                containerGUI.addPlainButton("goLeft", new Rectangle(431, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText("<<", 25, new GuiColor(255, 255, 255, 0.8f)), GoLeft);
            }

            containerGUI.display(player);

        }

        public void previewPanel(BasePlayer player, Item item, Skin skin)
        {
            GuiContainer containerGUI = new GuiContainer(this, "previewPanel", "panelOne");
            containerGUI.addImage("previewPicture", new Rectangle(1520, 66, 332, 333, 1920, 1080, true), skin.safename, GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
            containerGUI.addPanel("previewPictureText", new Rectangle(1501, 399, 371, 74, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{skin.name}", 20, new GuiColor(255, 255, 255, 0.5f)));
            containerGUI.addPanel("Text_CostToSkin", new Rectangle(1349, 753, 426, 35, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"COST TO SKIN: {getCost(player, item, skin.categoryObject)}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            containerGUI.display(player);
        }
        
        public void sendCategories(BasePlayer player, Item item, List<Category> categoriesList, int activeCategory = 0)
        {
            double OriginY = 494;
            double Height = 46;
            double maximumWidth = 1429;
            double widthEach = maximumWidth / categoriesList.Count;
            double OriginX = 452;
            int fontSize = 15;

            GuiContainer containerGUI = new GuiContainer(this, "categories", "background");
            containerGUI.addPanel("Text_AccountBalance", new Rectangle(1349, 790, 426, 35, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"ACCOUNT BALANCE: {getPoints(player)}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            int i = 0;
            foreach(Category Cat in categoriesList)
            {
                double xSpacing = OriginX + (widthEach * i);
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    sendCategories(bPlayer, item, categoriesList, index);
                };
                if (i == activeCategory)
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText(Cat.name.ToUpper(), fontSize, new GuiColor(134, 190, 41, 0.8f)), callback);
                }
                else
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(Cat.name.ToUpper(), fontSize, new GuiColor(255, 255, 255, 0.8f)), callback);
                }
                i++;
            }
            containerGUI.display(player);
            sendSkins(player, item, categoriesList[activeCategory]);

        }

        public void sendSkins(BasePlayer player, Item item, Category category)
        {
            List<List<Skin>> ListOfLists = SplitIntoChunks<Skin>(category.skins, 30);
            panelOne(player, item, ListOfLists);
        }

        public void closeUI(virtualContainer container)
        {
#if DEBUG
            container.player.ChatMessage("closing UI");
#endif
            GuiTracker.getGuiTracker(container.player).destroyGui(this, "background");
        }

        private void onItemInserted(virtualContainer container, Item item)
        {
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
            addSkins(IDs, (category == null)?"main":args[0]);
             return $"adding {IDs.Count} {((IDs.Count == 1)?"skin":"skins")}";
        }

        private void testConsoleCommand(ConsoleSystem.Arg arg)
        {
            changeSkinCategory(skinsData.GetSkin(ulong.Parse(arg.Args[0])), arg.Args[1]);
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("testing");
            Puts("testing");
#endif
            if(args[0] == "approve")
            {
                requestData.getNextRequest().approve("test");
                return;
            }
            if(args[0] == "return")
            {
                Request req = requestData.getNextRequest();
                timer.Once(10, () =>
                {
                    req.returnToQueue();
                });
                return;
            }
            if (requestData.addRequest(new Request(player.userID, ulong.Parse(args[0])))) player.ChatMessage("done");
        }
        #endregion

        #region helpers

        public Item applySkin(virtualContainer container, Item item, ulong skinID)
        {
            Item newItem = ItemManager.Create(item.info, item.amount, skinID);
            List<Item> contentBackup = new List<Item>();
            if(item.contents != null)
            {
                foreach (Item i in item.contents.itemList)
                {
                    contentBackup.Add(i);
                }
                foreach (Item i in contentBackup)
                {
                    newItem.contents.AddItem(i.info, i.amount);
                }
            }

            if (item.hasCondition)
            {
                newItem._maxCondition = item._maxCondition;
                newItem._condition = item._condition;
            }

            BaseProjectile oldGun = item.GetHeldEntity() as BaseProjectile;
            BaseProjectile newGun = newItem.GetHeldEntity() as BaseProjectile;
            if (newGun != null && oldGun != null)
            {
                newGun.primaryMagazine.ammoType = oldGun.primaryMagazine.ammoType;
                newGun.primaryMagazine.contents = oldGun.primaryMagazine.contents;
            }
            item.Remove();
            newItem.position = slot;
            newItem.parent = container.itemContainer;

            container.itemContainer.itemList.Add(newItem);
            newItem.MarkDirty();
            return newItem;
        }

        private void addSkins(List<ulong> IDs, string category, bool cfg = true)
        {
            foreach(ulong ID in IDs)
            {
                skinWebRequest(ID, (s) => addSkin(s, category, cfg));
            }
        }

        private void addSkin(Skin skin, string category, bool cfg = true)
        {
            skin.category = category ?? "main";

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
                item.categories.Add(cat);
            }
            if (skinsData.GetSkin(cat, skin.id) == null)
            {
                cat.skins.Add(skin);
                guiCreator.registerImage(this, skin.safename, skin.url);
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
                    config.skins[skin.shortname].Add(category, new List<ulong>());
                }
                if (!config.skins[skin.shortname][category].Contains(skin.id)) config.skins[skin.shortname][category].Add(skin.id);
                SaveConfig();
            }
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
            int cost = 0;
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

        private void skinWebRequest(ulong ID, Action<Skin> callback)
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
                    return;
                }
                if(answer.response.publishedfiledetails[0]?.title == null)
                {
                    Puts($"Skin ID {ID} doesn't exist!");
                    return;
                }
                Puts($"got skin info: {(answer.response.publishedfiledetails[0].title)} for ID {ID}!");
                if (answer.response.publishedfiledetails.Count < 1)
                {
                    Puts("0 publishedfiledetails in response!");
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
                    return;
                }
                Skin s = new Skin(pf.title, null, shortname, ulong.Parse(pf.publishedfileid), pf.preview_url);
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
                if (getPendingRequests(request.userID).Count >= config.maxPeningReq) return false;
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
                Request output = requests.Dequeue();
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
            [JsonProperty(PropertyName = "Chat Command")]
            public string command;

            [JsonProperty(PropertyName = "Use Server Rewards")]
            public bool useServerRewards;

            [JsonProperty(PropertyName = "Use Economics")]
            public bool useEconomics;

            [JsonProperty(PropertyName = "Hide Categories that the User doesn't have Permission for")]
            public bool hideCatsWithoutPerm;

            [JsonProperty(PropertyName = "Maximum pending requests per player")]
            public int maxPeningReq;

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
            return new ConfigData
            {
                command = "skinit",
                useServerRewards = true,
                useEconomics = false,
                hideCatsWithoutPerm = false,
                maxPeningReq = 7,
                costAttire = 5,
                costDeployable = 10,
                costTool = 15,
                costWeapon = 20,
                skins = new Dictionary<string, Dictionary<string, List<ulong>>>
                {
                    {"rock", new Dictionary<string, List<ulong>>
                        {
                            { "main", new List<ulong>
                                {
                                    2061119719,
                                    2062928637,
                                    2030659199
                                }
                            }
                        }
                    }
                }
            };
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
            {"posOutput", "Player Coordinates: X:{0}, Y:{1}, Z:{2}"},
            {"noPermission", "You don't have permission to use this command!"}
        };
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
            {"Beenie Hat", "hat.beenie"},
            {"Bolt Rifle", "rifle.bolt"},
            {"Bone Club", "bone.club"},
            {"Bone Knife", "knife.bone"},
            {"Boonie Hat", "hat.boonie"},
            {"Bucket Helmet", "bucket.helmet"},
            {"Burlap Headwrap", "burlap.headwrap"},
            {"Burlap Pants", "burlap.headwrap"},
            {"Burlap Shirt", "burlap.shirt"},
            {"Burlap Shoes", "burlap.shoes"},
            {"Cap", "hat.cap"},
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
            {"Garage Door", "wall.frame.garagedoor"},
            {"Hammer", "hammer"},
            {"Hatchet", "hatchet"},
            {"Hide Halterneck", "attire.hide.helterneck"},
            {"Hide Pants", "attire.hide.pants"},
            {"Hide Poncho", "attire.hide.poncho"},
            {"Hide Shirt", "attire.hide.poncho"},
            {"Hide Shoes", "attire.hide.poncho"},
            {"Hide Skirt", "attire.hide.skirt"},
            {"Hoodie", "hoodie"},
            {"Large Wood Box", "box.wooden.large"},
            {"Leather Gloves", "burlap.gloves"},
            {"Long TShirt", "tshirt.long"},
            {"Longsword", "longsword"},
            {"LR300", "rifle.lr300"},
            {"M249", "lmg.m249" },
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
            {"Snow Jacket", "jacket.snow"},
            {"Stone Hatchet", "stonehatchet"},
            {"Stone Pick Axe", "stone.pickaxe"},
            {"Sword", "salvaged.sword"},
            {"Tank Top", "shirt.tanktop"},
            {"Thompson", "smg.thompson"},
            {"TShirt", "tshirt"},
            {"Vagabond Jacket", "jacket"},
            {"Water Purifier", "water.purifier"},
            {"Waterpipe Shotgun", "shotgun.waterpipe"},
            {"Wood Storage Box", "box.wooden"},
            {"Wooden Door", "door.hinged.wood"},
            {"Wooden Double Door", "door.double.hinged.wood"},
            {"Work Boots", "shoes.boots"}
        };

        #endregion
    }
}