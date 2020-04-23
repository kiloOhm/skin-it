// Requires: GUICreator

#define DEBUG
//#define DEBUG2
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
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
        StoredData storedData;

        private const int slot = 19;
        #endregion

        #region classes

        [JsonObject(MemberSerialization.OptIn)]
        public class skin
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
            //process config

            foreach(string shortname in config.skins.Keys)
            {
                foreach(string category in config.skins[shortname].Keys)
                {
                    List<ulong> toBeAdded = new List<ulong>();
                    foreach(ulong id in config.skins[shortname][category])
                    {
                        if (!storedData.containsSkin(id)) toBeAdded.Add(id);
                    }
                    addSkins(toBeAdded, category, false);
                }
            }

            List<skin> toBeRemoved = new List<skin>();
            foreach(skin s in storedData.skins)
            {
                if(!config.skins.ContainsKey(s.shortname))
                {
                    toBeRemoved.Add(s);
                    continue;
                }
                if(!config.skins[s.shortname].ContainsKey(s.category))
                {
                    toBeRemoved.Add(s);
                    continue;
                }
                if(!config.skins[s.shortname][s.category].Contains(s.id))
                {
                    toBeRemoved.Add(s);
                    continue;
                }
            }
            foreach(skin s in toBeRemoved)
            {
                storedData.skins.Remove(s);
            }
            saveData();
            

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

            //lang
            lang.RegisterMessages(messages, this);

            //hooks
            Unsubscribe(nameof(CanAcceptItem));
        }

        private void OnSkinDataUpdated()
        {
            foreach(skin s in storedData.skins)
            {
                if(!ImageLibrary.Call<bool>("HasImage", s.safename, (ulong)0))
                {
                    ImageLibrary.Call<bool>("AddImage", s.url, s.safename, (ulong)0);
                }
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

            containerGUI.addPlainButton("checkout", new Rectangle(1349, 831, 425, 84, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText("SKIN-IT!", 30, new GuiColor(134, 190, 41, 0.8f)));
#if DEBUG
            //keeping this here for debugging purposes.
            // containerGUI.addPlainButton("close", new Rectangle(1827, 30, 64, 64, 1920, 1080, true), GuiContainer.Layer.overlay, new GuiColor(1, 0, 0, 0.5f), FadeIn, FadeOut, new GuiText(""));
#endif
            containerGUI.display(container.player);
        }

        public void categories(virtualContainer container, List<string> categoriesList = null, int activeCategory = 1)
        {
            GuiContainer containerGUI = new GuiContainer(this, "categories", "background");
            categoriesList.Add("Category 1");
            categoriesList.Add("Category 2");
            categoriesList.Add("Category 3");
            categoriesList.Add("Category 4");
            categoriesList.Add("Category 5");
            categoriesList.Add("Category 6");
            categoriesList.Add("Category 7");
            categoriesList.Add("Category 8");

            double maximumWidth = 1392;
            double widthEach = maximumWidth / categoriesList.Count;
            double initialX = 466;



            for (int i = 1; i > categoriesList.Count + 1; i++)
            {

                float xSpacing = (float)initialX * i;
                if (i == activeCategory)
                {
                    containerGUI.addPlainButton($"category{i}", new Rectangle(xSpacing, 502, 174, 34, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText($"{categoriesList[i - 1]}", 10, new GuiColor(134, 190, 41, 0.8f)));
                }
                else
                {
                    containerGUI.addPlainButton($"category{i}", new Rectangle(xSpacing, 502, 174, 34, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText($"{categoriesList[i - 1]}", 10, new GuiColor(255, 255, 255, 0.8f)));
                }
            }
            containerGUI.display(container.player);
        }

        public void categories(BasePlayer player, List<string> categoriesList, int activeCategory = 0)
        {
            double OriginY = 500;
            double Height = 45;
            double maximumWidth = 1920;
            double widthEach = maximumWidth / categoriesList.Count;
            double OriginX = 0;

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
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(67, 84, 37, 0.8f), FadeIn, FadeOut, new GuiText(s, 10, new GuiColor(134, 190, 41, 0.8f)), callback);
                }
                else
                {
                    containerGUI.addPlainButton($"category_{i}", new Rectangle(xSpacing, OriginY, widthEach, Height, 1920, 1080, true), GuiContainer.Layer.overall, new GuiColor(0, 0, 0, 0), FadeIn, FadeOut, new GuiText(s, 10, new GuiColor(255, 255, 255, 0.8f)), callback);
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
                        if (args.Length < 3) return; 
                        List<ulong> IDs = new List<ulong>();
                        for(int i = 2; i < args.Length; i++)
                        {
                            IDs.Add(ulong.Parse(args[i]));
                        }
                        addSkins(IDs, args[1]);
                        break;
                }
            }
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("testing");
#endif
            applySkin(player, player.GetActiveItem(), ulong.Parse(args[0]));
        }
        #endregion

        #region helpers

        public void applySkin(BasePlayer player, Item item, ulong skinID)
        {
            Item newItem = ItemManager.Create(item.info, item.amount, skinID);
            List<Item> contentBackup = new List<Item>();
            foreach(Item i in item.contents.itemList)
            {
                contentBackup.Add(i);
            }
            foreach(Item i in contentBackup)
            {
                newItem.contents.AddItem(i.info, i.amount);
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
            player.GiveItem(newItem);
        }

        private void addSkins(List<ulong> IDs, string category, bool cfg = true)
        {
            Action<List<skin>> callback = (skins) =>
            {
                foreach(skin s in skins)
                {
                    s.category = category;
                    if (!storedData.skins.Contains(s)) storedData.skins.Add(s);

                    if(cfg)
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
                    }
                }
                saveData();
                if(cfg) SaveConfig();
                OnSkinDataUpdated();
            };
            skinWebRequest(IDs, callback);
        }

        private void skinWebRequest(List<ulong> IDs, Action<List<skin>> callback)
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
                List<skin> output = new List<skin>();
                foreach(publishedFile pf in answer.response.publishedfiledetails)
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
                    skin s = new skin { name = pf.title, id = ulong.Parse(pf.publishedfileid), url = pf.preview_url , shortname = shortname};
                    output.Add(s);
                }
                callback(output);
            }, this, RequestMethod.POST);
        }

        public void sendSkinImg(BasePlayer player, string url, string name, Rectangle rectangle, string parent = null)
        {
            player.ChatMessage($"sending img {name}");
            Action callback = () =>
            {
                GuiContainer container = new GuiContainer(pluginInstance, $"skinimg_{name}", parent);
                container.addRawImage($"img_{name}", rectangle, ImageLibrary.Call<string>("GetImage", name), "Hud");
                container.display(player);
            };
            if(ImageLibrary.Call<bool>("HasImage", name, (ulong)0)) 
            {
                callback();
            }
            else ImageLibrary.Call<bool>("AddImage", url, name, (ulong)0, callback);

        }

        #endregion

        #region data management
        private class StoredData
        {
            public List<skin> skins = new List<skin>();

            public StoredData()
            {
            }

            public bool containsSkin(ulong id)
            {
                foreach(skin s in skins)
                {
                    if (s.id == id) return true;
                }
                return false;
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