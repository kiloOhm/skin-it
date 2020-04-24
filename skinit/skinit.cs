// Requires: GUICreator

#define DEBUG
//#define DEBUG2
using Facepunch.Extend;
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
    [Info("skinit", "Ohm & Bunsen", "0.1.0")]
    [Description("GUI based Item skinning")]
    class skinit : RustPlugin
    {
        #region references
        [PluginReference]
        private Plugin ImageLibrary;

        private GUICreator guiCreator;

        #endregion

        #region global
        private static skinit pluginInstance = null;

        public skinit()
        {
            pluginInstance = this;
        }

        DynamicConfigFile File;
        Data data;

        private const int slot = 19;
        #endregion

        #region classes

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
            [JsonProperty(PropertyName = "Item Shortname")]
            public string shortname;
            [JsonProperty(PropertyName = "Skins")]
            public List<Skin> skins = new List<skinit.Skin>();

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

            public Skin(string name, string category, string shortname, ulong id, string url)
            {
                this.name = name;
                this.category = category;
                this.shortname = shortname;
                this.id = id;
                this.url = url;
            }
        }

        public class virtualContainer:MonoBehaviour
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

                pluginInstance.sendUI(this);
                pluginInstance.Subscribe(nameof(CanAcceptItem));
            }

            public void close()
            {
#if DEBUG2
                player.ChatMessage("closing virtual container");
#endif
                if (item != null) player.GiveItem(item);
                pluginInstance.closeUI(this);
                Destroy(this);
            }
        }

        #endregion

        #region oxide hooks
        void Init()
        {
            permission.RegisterPermission("skinit.use", this);
            File = Interface.Oxide.DataFileSystem.GetFile("skinit/skins");
            loadData();
        }

        void OnServerInitialized()
        {
            #region config processing

            //add new skins
            Dictionary<string, List<ulong>> toBeAdded = new Dictionary<string, List<ulong>>();
            foreach (string shortname in config.skins.Keys)
            {
                Skinnable item = data.GetSkinnable(shortname);
                if(item == null)
                {
                    item = new Skinnable(shortname);
                    data.items.Add(item);
                }
                foreach(string category in config.skins[shortname].Keys)
                {
                    Category cat = data.GetCategory(item, category);
                    if(cat == null)
                    {
                        cat = new Category(category, item.shortname);
                        item.categories.Add(cat);
                    }
                    foreach(ulong id in config.skins[shortname][category])
                    {
                        if (data.GetSkin(cat, id) == null)
                        {
                            if (!toBeAdded.ContainsKey(cat.name)) toBeAdded.Add(cat.name, new List<ulong>());
                            toBeAdded[cat.name].Add(id);
                        }
                    }

                }
            }
            foreach(string cat in toBeAdded.Keys)
            {
                addSkins(toBeAdded[cat], cat, false);
            }

            //delete removed skins
            data.items.RemoveAll(item => !config.skins.ContainsKey(item.shortname));
            foreach(Skinnable item in data.items)
            {
                item.categories.RemoveAll(cat => !config.skins[item.shortname].ContainsKey(cat.name));
                foreach(Category cat in item.categories)
                {
                    cat.skins.RemoveAll(skin => !config.skins[item.shortname][cat.name].Contains(skin.id));
                }
            }
            saveData();

            #endregion


            //references
            guiCreator = (GUICreator)Manager.GetPlugin("GUICreator");
            if (ImageLibrary == null)
            {
                Puts("ImageLibrary is not loaded! get it here https://umod.org/plugins/image-library");
                return;
            }

            //commands
            cmd.AddChatCommand("skinit", this, nameof(skinitCommand));
            cmd.AddChatCommand("test", this, nameof(testCommand));
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
            if(item?.parent?.uid == vContainer.uid)
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
#if DEBUG
            container.player.ChatMessage("sending UI");
#endif
            GuiContainer containerGUI = new GuiContainer(this, "background");
            int cost = 35;
            int balance = 1550;
            string skinPermissions = "attire, deployables, tools, weapons";
            skinPermissions = skinPermissions.ToUpper();
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
            containerGUI.addPanel("Text_CostToSkin", new Rectangle(1349, 753, 426, 35, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"COST TO SKIN: {cost}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_AccountBalance", new Rectangle(1349, 790, 426, 35, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"ACCOUNT BALANCE: {balance}", 19, new GuiColor(255, 255, 255, 0.4f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_Permissions_1", new Rectangle(80, 937, 471, 43, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("According to your permissions, you may skin...", 15, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_Permissions_2", new Rectangle(80, 975, 471, 43, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{skinPermissions}", 20, new GuiColor(255, 255, 255, 0.3f), TextAnchor.MiddleLeft));
            containerGUI.addPanel("Text_2", new Rectangle(1454, 629, 321, 115, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText("ITEM TO BE SKINNED", 23, new GuiColor(255, 255, 255, 0.3f)));
#if DEBUG
            //keeping this here for debugging purposes.
            // containerGUI.addPlainButton("close", new Rectangle(1827, 30, 64, 64, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(1, 0, 0, 0.5f), FadeIn, FadeOut, new GuiText(""));
#endif
            containerGUI.display(container.player);

            List<string> testList = new List<string> { "Western", "Space", "Medical", "Chemists", "Christmas", "Naughty" };
            List<string> picturesList = new List<string> {"smile", "sad", "smile", "sad", "smile", "smile", "sad", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "sad", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile", "smile" };
            categories(container.player, testList);
            List<List<string>> ListOfLists = SplitIntoChunks<string>(picturesList, 30);
            panelOneBackground(container.player);
            panelOne(container.player, ListOfLists);
            skinitButton(container.player);

        }

        public void skinitButton(BasePlayer player, int activeSkin = 0, bool skinSelected = false, bool successSkin = false, bool attempt = false)
        {
            Action<BasePlayer, string[]> Skinit = (bPlayer, input) =>
            {
                skinitButton(player, 0, skinSelected = false, successSkin = false, attempt = true);

            };
            GuiContainer containerGUI = new GuiContainer(this, "skinitButton", "background");

            if (successSkin == true) {} // If the username entered matches a username in Userlogin();
            else if (attempt == true) // If the username entered DOES NOT match a username in Userlogin(), but there has been an attempt;
            {
                containerGUI.addPlainButton("checkout", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(65, 33, 32, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("NO SKIN SELECTED!", 30, new GuiColor(162, 51, 46, 0.8f)));
                timer.Once(1f, () => // After a second launch panelOne again with default parameters
                {
                    skinitButton(player, 0, skinSelected = false, successSkin = false, attempt = false);
                });
            }
            else // The initial state when panelOne is launched;
            {
        containerGUI.addPlainButton("checkout", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(67, 84, 37, 0.8f), FadeIn = 0.05f, FadeOut = 0.05f, new GuiText("SKIN-IT!", 30, new GuiColor(134, 190, 41, 0.8f)),Skinit);
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
        public void panelOne(BasePlayer player, List<List<string>> picturesListOfLists, int activePicture = 0, int page=0)
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

            List<string> picturesList = picturesListOfLists[page];
            GuiContainer containerGUI = new GuiContainer(this, "panelOne", "background");
            int i = 0;
            foreach(string s in picturesList)
            {
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    panelOne(player, picturesListOfLists, activePicture = index, page);
                    previewPanel(player, picturesListOfLists, activePicture = index, page);
                };
                if (i<picturesEachRow)
                {
                    xSpacing = OriginX + (widthEach * i);
                    OriginY = OriginY1;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), picturesList[i], GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);


                }
                else if (i>=picturesEachRow && i<(picturesEachRow*2))
                {
                    xSpacing = (OriginX + (widthEach * i))-(widthEach*picturesEachRow);
                    OriginY = OriginY2;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), picturesList[i], GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                else if (i<totalPictures)
                {
                    xSpacing = (OriginX + (widthEach * i)) - ((widthEach * picturesEachRow)*2);
                    OriginY = OriginY3;
                    containerGUI.addImage($"picture{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), picturesList[i], GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
                    containerGUI.addPlainButton($"button{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn = 0, FadeIn = 0, new GuiText("", 15, new GuiColor(255, 255, 255, 0.8f)), callback);

                }
                i++;
            }
            
            Action<BasePlayer, string[]> GoRight = (bPlayer, input) =>
            {
                if (page == picturesListOfLists.Count-1)
                {
                    page = 0;
                    panelOne(player, picturesListOfLists, activePicture = 0, page);
                    previewPanel(player, picturesListOfLists, activePicture = 0, page);

                } else
                {
                    page += 1;
                    panelOne(player, picturesListOfLists, activePicture = 0, page);
                    previewPanel(player, picturesListOfLists, activePicture = 0, page);
                }
                };
            Action<BasePlayer, string[]> GoLeft = (bPlayer, input) =>
            {
                if (page == 0)
                {
                    page = picturesListOfLists.Count-1;
                    panelOne(player, picturesListOfLists, activePicture = 0, page);
                    previewPanel(player, picturesListOfLists, activePicture = 0, page);
                } else
                {
                    page -= 1;
                    panelOne(player, picturesListOfLists, activePicture = 0, page);
                    previewPanel(player, picturesListOfLists, activePicture = 0, page);
                }
                };
            if (picturesListOfLists.Count > 1)
            {
                containerGUI.addPlainButton("goRight", new Rectangle(1437, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(">>", 25, new GuiColor(255, 255, 255, 0.8f)), GoRight);
                containerGUI.addPlainButton("goLeft", new Rectangle(431, 230, 56, 56, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText("<<", 25, new GuiColor(255, 255, 255, 0.8f)), GoLeft);
            }

            containerGUI.display(player);

        }

        public void previewPanel(BasePlayer player, List<List<string>> picturesListOfLists, int activePicture = 0,int page = 0)
        {
            GuiContainer containerGUI = new GuiContainer(this, "previewPanel", "background");
            List<string> picturesList = picturesListOfLists[page];
            containerGUI.addImage("previewPicture", new Rectangle(1520, 66, 332, 333, 1920, 1080, true), picturesList[activePicture], GuiContainer.Layer.overlay, null, FadeIn = 0.25f, FadeIn = 0.25f);
            containerGUI.addPanel("previewPictureText", new Rectangle(1501, 399, 371, 74, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), 0, 0, new GuiText($"{picturesList[activePicture]}", 20, new GuiColor(255, 255, 255, 0.5f)));
            containerGUI.display(player);
        }
        // BEGIN CATEGORIES
        public void categories(BasePlayer player, List<string> categoriesList, int activeCategory = 0)
        {
            double OriginY = 494;
            double Height = 46;
            double maximumWidth = 1429;
            double widthEach = maximumWidth / categoriesList.Count;
            double OriginX = 452;
            int fontSize = 15;

            GuiContainer containerGUI = new GuiContainer(this, "categories", "background");
            int i = 0;
            foreach(string s in categoriesList)
            {
                double xSpacing = OriginX + (widthEach * i);
                int index = i;
                Action<BasePlayer, string[]> callback = (bPlayer, input) =>
                {
                    categories(bPlayer, categoriesList, index);
                };
                if (i == activeCategory)
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText(s.ToUpper(), fontSize, new GuiColor(134, 190, 41, 0.8f)), callback);
                }
                else
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(s.ToUpper(), fontSize, new GuiColor(255, 255, 255, 0.8f)), callback);
                }
                i++;
            }
            containerGUI.display(player);

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
            //container.item = item;
            //List<Skin> availableSkins = getSkins(item.info.shortname);

            //GuiContainer guiContainer = new GuiContainer(this, "skinsTest", "background");
            //int i = 0;
            //foreach(Skin s in availableSkins)
            //{
            //    Rectangle pos = new Rectangle(534 + (i * 110), 221, 100, 100, 1920, 1080, true);
            //    guiContainer.addImage($"img_{s.safename}", pos, s.safename, GuiContainer.Layer.menu, null, FadeIn, FadeOut);
            //    Skin selected = s;
            //    Action<BasePlayer, string[]> callback = (bPlayer, input) =>
            //    {
            //        bPlayer.ChatMessage($"skinning {selected.name}");
            //        Item newItem = applySkin(container, item, selected.id);
            //        onItemInserted(container, newItem);
            //    };
            //    guiContainer.addPlainButton($"btn_{s.safename}", new Rectangle(), null, FadeIn, FadeOut, callback: callback, parent: $"img_{s.safename}");
            //    i++;
            //}
            //guiContainer.display(container.player);
        }

        private void onItemRemoved(virtualContainer container, Item item)
        {
#if DEBUG
            PrintToChat($"OnItemRemoved: container:{container.uid}, owner:{container?.player?.displayName}, item:{item?.amount} x {item?.info?.displayName?.english}");
#endif
            GuiTracker.getGuiTracker(container.player).destroyGui(this, "skinsTest");
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
             return $"added {IDs.Count} skins";
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("testing");
#endif
            List<string> testList = new List<string> { "entry1", "entry2", "entry3" };
            categories(player, testList);
        }
        #endregion

        #region helpers

        public Item applySkin( virtualContainer container, Item item, ulong skinID)
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
            Action<List<Skin>> callback = (skins) =>
            {
                foreach(Skin s in skins)
                {
                    s.category = category;

                    Skinnable item = data.GetSkinnable(s.shortname);
                    if(item == null)
                    {
                        item = new Skinnable(s.shortname);
                        data.items.Add(item);
                    }
                    Category cat = data.GetCategory(item, s.category);
                    if(cat == null)
                    {
                        cat = new Category(s.category, s.shortname);
                        item.categories.Add(cat);
                    }
                    if (data.GetSkin(cat, s.id) == null)
                    {
                        cat.skins.Add(s);
                        guiCreator.registerImage(this, s.safename, s.url);
                    }
                    saveData();

                    if (cfg)
                    {
                        if (!config.skins.ContainsKey(s.shortname))
                        {
                            config.skins.Add(s.shortname, new Dictionary<string, List<ulong>>());
                        }
                        if(!config.skins[s.shortname].ContainsKey(category))
                        {
                            config.skins[s.shortname].Add(category, new List<ulong>());
                        }
                        config.skins[s.shortname][category].Add(s.id);
                        SaveConfig();
                    }
                }
            };
            skinWebRequest(IDs, callback);
        }

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

        private void skinWebRequest(List<ulong> IDs, Action<List<Skin>> callback)
        {
            if (IDs.Count < 1) return;

            StringBuilder bodySB = new StringBuilder();
            int i = 0;
            foreach (ulong id in IDs)
            {
                bodySB.Append($"&publishedfileids%5B{i}%5D={id}");
                i++;
            }
            string body = $"itemcount={IDs.Count}{bodySB}";
            webrequest.Enqueue("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body, (code, response) =>
            {
                if (code != 200 || response == null)
                {
                    Puts($"Coudn't get skin info!");
                    return;
                }
                webResponse answer = JsonConvert.DeserializeObject<webResponse>(response);
#if DEBUG
                Puts($"getting skin info: {(answer?.response?.publishedfiledetails[0]?.title) ?? "null"}");
#endif
                if (answer?.response?.publishedfiledetails == null) return;
                List<Skin> output = new List<Skin>();
                foreach (publishedFile pf in answer.response.publishedfiledetails)
                {

                    string shortname = null;
                    foreach (tag_ t in pf.tags)
                    {
                        if (shortnames.ContainsKey(t.tag))
                        {
                            shortname = shortnames[t.tag];
                            break;
                        }
                    }
                    if (shortname == null) continue;
                    Skin s = new Skin(pf.title, null, shortname, ulong.Parse(pf.publishedfileid), pf.preview_url);
                    output.Add(s);
                }
                callback(output);
            }, this, RequestMethod.POST);
        }

        #endregion

        #region data management
        private class Data
        {
            public List<Skinnable> items = new List<skinit.Skinnable>();

            public Data()
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

        void saveData()
        {
            try
            {
                File.WriteObject(data);
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
                data = File.ReadObject<Data>();
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
            public Dictionary<string, Dictionary<string, List<ulong>>> skins;

        }

        private ConfigData getDefaultConfig()
        {
            return new ConfigData
            {
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
            {"Work Boots", "shoes.boots"}
        };

        #endregion
    }
}