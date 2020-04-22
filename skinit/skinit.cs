// Requires: GUICreator

#define DEBUG
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

        private const int slot = 20;
        #endregion

        #region classes

        public class skin
        {
            public string name;
            public string safename => Regex.Replace(name, " ", "_");
            public string shortname;
            public ulong id;
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
            List<ulong> toBeAdded = new List<ulong>();
            foreach(string key in config.skins.Keys)
            {
                foreach(ulong id in config.skins[key])
                {
                    if (!storedData.containsSkin(id)) toBeAdded.Add(id);
                }
            }
            addSkins(toBeAdded, false);

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

            //lang
            lang.RegisterMessages(messages, this);
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
                        List<ulong> IDs = new List<ulong>();
                        for(int i = 1; i < args.Length; i++)
                        {
                            IDs.Add(ulong.Parse(args[i]));
                        }
                        addSkins(IDs);
                        break;
                }
            }
        }

        private void testCommand(BasePlayer player, string command, string[] args)
        {
#if DEBUG
            player.ChatMessage("testing");
#endif

            List<ulong> IDs = new List<ulong>();
            foreach(string arg in args)
            {
                IDs.Add(ulong.Parse(arg));
            }

            Action<List<skin>> callback = (skins) =>
            {
                int i = 0;
                foreach(skin s in skins)
                {
                    Rectangle rect = new Rectangle(200+(i*110), 490, 100, 100, 1920, 1080, true);
                    sendSkinImg(player, s.url, s.safename, rect);
                    i++;
                }
            };
            skinWebRequest(IDs, callback);
        }
        #endregion

        #region helpers

        private void addSkins(List<ulong> IDs, bool cfg = true)
        {
            Action<List<skin>> callback = (skins) =>
            {
                foreach(skin s in skins)
                {
                    if (!storedData.skins.Contains(s)) storedData.skins.Add(s);

                    if(cfg)
                    {
                        if (!config.skins.ContainsKey(s.shortname))
                        {
                            config.skins.Add(s.shortname, new List<ulong>());
                        }
                        config.skins[s.shortname].Add(s.id);
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
            public Dictionary<string, List<ulong>> skins;

        }

        private ConfigData getDefaultConfig()
        {
            return new ConfigData
            {
                skins = new Dictionary<string, List<ulong>>
                {
                    {"rock", new List<ulong>
                    {
                        2061119719,
                        2062928637,
                        2030659199
                    }}
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