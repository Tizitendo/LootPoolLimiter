using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json.Utilities;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;
using static RoR2.PickupPickerController;
using Random = UnityEngine.Random;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;
using System.IO;
using System.Reflection;
using Path = System.IO.Path;

namespace LootPoolLimiter
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency("com.rune580.riskofoptions")]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class LootPoolLimiter : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Onyx";
        public const string PluginName = "LootPoolLimiter";
        public const string PluginVersion = "1.0.0";

        public static String[] affectedPools = { "dtChest1" , "dtChest2", "dtSonorousEcho", "dtSmallChestDamage",
            "dtSmallChestHealing", "dtSmallChestUtility", "dtCategoryChest2Damage", "dtCategoryChest2Healing",
            "dtCategoryChest2Utility", "dtCasinoChest", "dtShrineChance", "dtChanceDoll", "dtLockbox", "dtSacrificeArtifact",
            "dtVoidTriple", "dtTier1Item", "dtTier2Item", "dtTier3Item", "GeodeRewardDropTable", "dtShrineHalcyoniteTier1",
            "dtShrineHalcyoniteTier2", "dtShrineHalcyoniteTier3"};

        public static ConfigEntry<String> ConfigWhites { get; set; }
        public static ConfigEntry<String> ConfigGreens { get; set; }
        public static ConfigEntry<String> ConfigReds { get; set; }
        public static ConfigEntry<float> forceCategories { get; set; }
        public static ConfigEntry<bool> affectScrappers { get; set; }
        public static ConfigEntry<bool> affectCradles { get; set; }
        public static ConfigEntry<bool> affectVoidKey { get; set; }

        public static BasicPickupDropTable dtDuplicatorTier1;
        public static BasicPickupDropTable dtDuplicatorTier2;
        public static BasicPickupDropTable dtDuplicatorTier3;

        public static int numWhites;
        public static int numGreens;
        public static int numReds;

        public static List<PickupIndex> blockedWhites;
        public static List<PickupIndex> blockedGreens;
        public static List<PickupIndex> blockedReds;
        Dictionary<ItemDef, ItemDef> voidPairs;
        public static int SotsItemCount;
        public static float[] categoryWeightsWhite = new float[3];
        public static float[] categoryWeightsGreen = new float[3];
        public static float[] categoryWeightsRed = new float[3];
        public static ItemTag[] categoryTags = { ItemTag.Damage, ItemTag.Utility, ItemTag.Healing };

        public void Awake()
        {
            Log.Init(Logger);
            InitConfig();
            RoR2.Run.onRunStartGlobal += start_blacklist;
            On.RoR2.BasicPickupDropTable.GenerateWeightedSelection += filter_basic_loot;
            On.RoR2.PickupPickerController.GenerateOptionsFromDropTablePlusForcedStorm += fix_halc_loot;
            On.RoR2.PickupTransmutationManager.RebuildAvailablePickupGroups += filter_scrapper;
            On.RoR2.ShopTerminalBehavior.Start += fix_soup_always_affected;
        }

        public void fix_soup_always_affected(On.RoR2.ShopTerminalBehavior.orig_Start orig, ShopTerminalBehavior self)
        {
            if (!affectScrappers.Value)
            {
                if (self.name.Contains("LunarCauldron, WhiteToGreen"))
                {
                    self.dropTable = dtDuplicatorTier2;
                }
                if (self.name.Contains("LunarCauldron, GreenToRed"))
                {
                    self.dropTable = dtDuplicatorTier3;
                }
                if (self.name.Contains("LunarCauldron, RedToWhite"))
                {
                    self.dropTable = dtDuplicatorTier1;
                }
            }

            orig(self);
        }

        void load_config()
        {
            int parse_itemnum(String configString)
            {
                String[] splitString = configString.Split('-');
                int max, min;
                if (splitString.Length > 1)
                {
                    if (splitString[0].Length > 0)
                    {
                        int.TryParse(splitString[0], out min);
                        int.TryParse(splitString[1], out max);
                    }
                    else
                    {
                        min = -1;
                        max = -1;
                    }
                }
                else
                {
                    int.TryParse(configString, out min);
                    max = min;
                }
                return Random.Range(min, max + 1);
            }
            Config.Reload();
            numWhites = parse_itemnum(ConfigWhites.Value);
            numGreens = parse_itemnum(ConfigGreens.Value);
            numReds = parse_itemnum(ConfigReds.Value);
        }

        private void get_weights(float[] categoryWeights, List<PickupIndex> availableDropList, int numAllowed)
        {
            foreach (PickupIndex item in availableDropList)
            {
                for (int i = 0; i < categoryTags.Length; i++)
                {
                    if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(item).itemIndex).ContainsTag(categoryTags[i]))
                    {
                        categoryWeights[i]++;
                    }
                }
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                categoryWeights[i] = (categoryWeights[i] / availableDropList.Count) * (availableDropList.Count - numAllowed);
            }
        }

        private bool countupTag(ItemDef item)
        {
            float[] categoryWeights;
            switch (item.tier)
            {
                case ItemTier.Tier1:
                    categoryWeights = categoryWeightsWhite;
                    break;
                case ItemTier.Tier2:
                    categoryWeights = categoryWeightsGreen;
                    break;
                case ItemTier.Tier3:
                    categoryWeights = categoryWeightsRed;
                    break;
                default:
                    return false;
            }
            for (int i = 0; i < categoryTags.Length; i++)
            {
                if (item.ContainsTag(categoryTags[i]) && categoryWeights[i] < 0.5 * (1 + forceCategories.Value / 10))
                {
                    return false;
                }
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                if (item.ContainsTag(categoryTags[i]))
                {
                    categoryWeights[i]--;
                }
            }
            return true;
        }

        private void start_blacklist(Run run)
        {
            load_config();
            get_weights(categoryWeightsWhite, run.availableTier1DropList, numWhites);
            get_weights(categoryWeightsGreen, run.availableTier2DropList, numGreens);
            get_weights(categoryWeightsRed, run.availableTier3DropList, numReds);
            SotsItemCount = 0;
            init_void_relationships();
            blockedWhites = create_blacklist(new List<PickupIndex>(run.availableTier1DropList), numWhites);
            blockedGreens = create_blacklist(new List<PickupIndex>(run.availableTier2DropList), numGreens);
            blockedReds = create_blacklist(new List<PickupIndex>(run.availableTier3DropList), numReds);
        }

        private void init_void_relationships()
        {
            voidPairs = new Dictionary<ItemDef, ItemDef>();
            foreach (ItemDef.Pair relationship in ItemCatalog.GetItemPairsForRelationship(Addressables.LoadAssetAsync<ItemRelationshipType>("RoR2/DLC1/Common/ContagiousItem.asset").WaitForCompletion()))
            {
                voidPairs.Add(relationship.itemDef1, relationship.itemDef2);
            }
        }

        private List<PickupIndex> create_blacklist(List<PickupIndex> itemList, int blacklistCount)
        {
            List<PickupIndex> blockedItems = new List<PickupIndex>();
            if (blacklistCount < 0)
            {
                return blockedItems;
            }
            for (int i = itemList.Count - blacklistCount - 1; i >= 0; i--)
            {
                if (itemList.Count <= 1)
                {
                    break;
                }
                int random = Random.Range(0, itemList.Count);
                ItemDef item = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(itemList[random]).itemIndex);
                if (countupTag(item))
                {
                    blockedItems.Add(itemList[random]);
                    if (voidPairs.ContainsKey(item))
                    {
                        blockedItems.Add(PickupCatalog.FindPickupIndex(voidPairs[item].itemIndex));
                    }
                }
                else
                {
                    i++;
                }
                itemList.RemoveAt(random);
            }

            foreach (PickupIndex item in itemList)
            {
                if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(item).itemIndex).ContainsTag(ItemTag.HalcyoniteShrine))
                {
                    SotsItemCount = Math.Min(SotsItemCount + 1, 2);
                }
            }
            return blockedItems;
        }

        private void filter_basic_loot(On.RoR2.BasicPickupDropTable.orig_GenerateWeightedSelection orig, BasicPickupDropTable self, Run run)
        {
            orig(self, run);
            //Log.Info(self.name);

            //get droptables to replace cauldrons with that of printers, before was using the same as multishops
            if (self.name.Contains("dtDuplicatorTier1"))
            {
                dtDuplicatorTier1 = self;
            }
            else if (self.name.Contains("dtDuplicatorTier2"))
            {
                dtDuplicatorTier2 = self;
            }
            else if (self.name.Contains("dtDuplicatorTier3"))
            {
                dtDuplicatorTier3 = self;
            }

            if (!affectedPools.Contains(self.name) &&
                !(affectScrappers.Value && self.name.Contains("dtDuplicator")) &&
                !(affectCradles.Value && self.name.Contains("dtVoidChest")) &&
                !(affectVoidKey.Value && self.name.Contains("dtVoidLockbox")))
            {
                return;
            }
            Dictionary<ItemTier, float> prevTierAmount = get_tier_amounts(self);

            for (int num = self.selector.Count - 1; num >= 0; num--)
            {
                if (blockedWhites.Contains(self.selector.choices[num].value) ||
                    blockedGreens.Contains(self.selector.choices[num].value))
                {
                    self.selector.RemoveChoice(num);
                }
            }
            balance_item_weight(self, prevTierAmount);
            PickupTransmutationManager.RebuildPickupGroups();
        }

        void filter_scrapper(On.RoR2.PickupTransmutationManager.orig_RebuildAvailablePickupGroups orig, Run run)
        {
            orig(run);
            for (int i = 0; i < PickupTransmutationManager.availablePickupGroups.Length; i++)
            {
                PickupIndex[] itemList = PickupTransmutationManager.pickupGroups[i];
                List<PickupIndex> newItemList = new List<PickupIndex>();
                foreach (PickupIndex item in itemList)
                {
                    if (!blockedWhites.Contains(item) && !blockedGreens.Contains(item) && !blockedReds.Contains(item))
                    {
                        newItemList.Add(item);
                    }
                }
                itemList = newItemList.ToArray();

                PickupTransmutationManager.availablePickupGroups[i] = itemList;
                for (int j = 0; j < itemList.Length; j++)
                {
                    PickupTransmutationManager.availablePickupGroupMap[itemList[j].value] = itemList;
                }
            }
        }

        PickupPickerController.Option[] fix_halc_loot(On.RoR2.PickupPickerController.orig_GenerateOptionsFromDropTablePlusForcedStorm orig, int numOptions, PickupDropTable dropTable, PickupDropTable stormDropTable, Xoroshiro128Plus rng)
        {
            PickupPickerController.Option[] shrineDrops = orig(numOptions, dropTable, stormDropTable, rng);
            PickupIndex[] stormdrops = stormDropTable.GenerateUniqueDrops(2, rng);
            PickupIndex[] normaldrops = dropTable.GenerateUniqueDrops(numOptions, rng);
            int sotsItems = 0;
            for (int i = 0; i < numOptions; i++)
            {
                shrineDrops[i] = new Option
                {
                    available = true,
                    pickupIndex = normaldrops[i]
                };
                if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(normaldrops[i]).itemIndex).ContainsTag(ItemTag.HalcyoniteShrine))
                {
                    sotsItems++;
                    if (normaldrops[i] == stormdrops[1])
                    {
                        stormdrops[1] = stormdrops[0];
                    }
                }
                if (i + SotsItemCount - sotsItems >= numOptions)
                {
                    shrineDrops[i].pickupIndex = stormdrops[sotsItems];
                }
            }
            Shuffle(new System.Random(), shrineDrops);
            return shrineDrops;
        }

        Dictionary<ItemTier, float> get_tier_amounts(BasicPickupDropTable self)
        {
            Dictionary<ItemTier, float> tierAmount = new Dictionary<ItemTier, float>();

            foreach (WeightedSelection<PickupIndex>.ChoiceInfo choice in self.selector.choices)
            {
                if (PickupCatalog.GetPickupDef(choice.value).itemIndex != ItemIndex.None)
                {
                    if (tierAmount.ContainsKey(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier))
                    {
                        tierAmount[ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier] += 1;
                    }
                    else
                    {
                        tierAmount.Add(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier, 1);
                    }
                }
            }
            return tierAmount;
        }

        void balance_item_weight(BasicPickupDropTable self, Dictionary<ItemTier, float> prevTierAmount)
        {
            Dictionary<ItemTier, float> tierAmount = get_tier_amounts(self);
            List<ItemTier> tierkeys = new List<ItemTier>();
            foreach (ItemTier tier in tierAmount.Keys)
            {
                tierkeys.Add(tier);
            }

            foreach (ItemTier tier in tierkeys)
            {
                tierAmount[tier] = prevTierAmount[tier] / tierAmount[tier];
            }

            for (int i = 0; i < self.selector.Count; i++)
            {
                if (PickupCatalog.GetPickupDef(self.selector.choices[i].value).itemIndex != ItemIndex.None)
                {
                    self.selector.ModifyChoiceWeight(i, self.selector.choices[i].weight * tierAmount[ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(self.selector.choices[i].value).itemIndex).tier]);
                }
            }
        }

        private void InitConfig()
        {
            ConfigWhites = Config.Bind<String>(
            "General",
            "White Items",
                    "15-25",
            "How many white items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
            );
            ConfigGreens = Config.Bind<String>(
            "General",
            "Green Items",
                    "10-15",
            "How many green items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
            );
            ConfigReds = Config.Bind<String>(
            "General",
            "Red Items",
                    "-1",
            "How many red items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
            );
            forceCategories = Config.Bind<float>(
            "General",
            "Category ratio variance",
                    10,
            "Determines how much the ratios can differ from the base game. \nEnsures that you can't get a build with only healing etc.\n Higher values equals more randomness.\n (min:0, max:100)"
            );
            affectScrappers = Config.Bind<bool>(
            "General",
            "Affect scrappers",
                    false,
            "Limits the loot pool of scrappers as well"
            );
            affectCradles = Config.Bind<bool>(
            "General",
            "Affect cradles",
                    false,
            "Only allow voided variants of whitelisted items from cradles"
            );
            affectVoidKey = Config.Bind<bool>(
            "General",
            "Affect voidKey",
                    false,
            "Only allow voided variants of whitelisted items from void keyboxes"
            );

            ModSettingsManager.SetModDescription("Randomly remove a set amount of items from the itempool");
            ModSettingsManager.AddOption(new StringInputFieldOption(ConfigWhites));
            ModSettingsManager.AddOption(new StringInputFieldOption(ConfigGreens));
            ModSettingsManager.AddOption(new StringInputFieldOption(ConfigReds));
            ModSettingsManager.AddOption(new StepSliderOption(forceCategories, new StepSliderConfig() { min = 0, max = 100, increment = 1f }));
            ModSettingsManager.AddOption(new CheckBoxOption(affectScrappers));
            ModSettingsManager.AddOption(new CheckBoxOption(affectCradles));
            ModSettingsManager.AddOption(new CheckBoxOption(affectVoidKey));
            SetSpriteDefaultIcon();
        }

        void SetSpriteDefaultIcon()
        {
            try
            {
                string fullName = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)).FullName;
                Texture2D texture2D = new Texture2D(256, 256);
                if (texture2D.LoadImage(File.ReadAllBytes(Path.Combine(fullName, "icon.png"))))
                {
                    ModSettingsManager.SetModIcon(Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f)));
                }
                else
                {
                    Log.Error("Failed to load icon.png");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load icon.png\n" + ex);
            }
        }
    }
}
