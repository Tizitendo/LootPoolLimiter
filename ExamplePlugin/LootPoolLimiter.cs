using BepInEx;
using Newtonsoft.Json.Utilities;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static RoR2.PickupPickerController;
using Random = UnityEngine.Random;

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
        public const string PluginVersion = "1.1.1";

        public static String[] affectedPools = { "dtChest1" , "dtChest2", "dtSonorousEcho", "dtSmallChestDamage",
            "dtSmallChestHealing", "dtSmallChestUtility", "dtCategoryChest2Damage", "dtCategoryChest2Healing",
            "dtCategoryChest2Utility", "dtCasinoChest", "dtShrineChance", "dtChanceDoll", "dtLockbox", "dtSacrificeArtifact",
            "dtVoidTriple", "dtTier1Item", "dtTier2Item", "dtTier3Item", "GeodeRewardDropTable", "dtShrineHalcyoniteTier1",
            "dtShrineHalcyoniteTier2", "dtShrineHalcyoniteTier3"};


        public static BasicPickupDropTable dtDuplicatorTier1;
        public static BasicPickupDropTable dtDuplicatorTier2;
        public static BasicPickupDropTable dtDuplicatorTier3;
        public static BasicPickupDropTable dtChest1;

        public static int numWhites;
        public static int numGreens;
        public static int numReds;

        public static List<PickupIndex> blockedWhites;
        public static List<PickupIndex> blockedGreens;
        public static List<PickupIndex> blockedReds;
        Dictionary<ItemDef, ItemDef> voidPairs;
        public static int SotsItemCount;
        public static float[] categoryWeightsWhite = new float[4];
        public static float[] categoryWeightsGreen = new float[4];
        public static float[] categoryWeightsRed = new float[4];
        //public static float[] speedWeights = new float[3];
        public static ItemTag[] categoryTags = { ItemTag.Damage, ItemTag.Utility, ItemTag.Healing };

        public void Awake()
        {
            Log.Init(Logger);
            LootPoolLimiterConfig.InitConfig(Config);
            RoR2.Run.onRunStartGlobal += start_blacklist;

            On.RoR2.BasicPickupDropTable.GenerateWeightedSelection += filter_basic_loot;
            On.RoR2.PickupPickerController.GenerateOptionsFromDropTablePlusForcedStorm += fix_halc_loot;
            On.RoR2.PickupTransmutationManager.RebuildAvailablePickupGroups += filter_printers;
            On.RoR2.ShopTerminalBehavior.Start += fix_soup_always_affected;
            On.RoR2.ChestBehavior.BaseItemDrop += fix_scavbag_droptable;
            On.RoR2.PickupPickerController.GenerateOptionsFromDropTable += test;
            On.RoR2.PickupPickerController.GenerateOptionsFromArray += idk;
            //On.RoR2.PickupPickerController.GenerateOptionsFromDropTablePlusForcedStorm += yo;
        }

        //private Option[] yo(On.RoR2.PickupPickerController.orig_GenerateOptionsFromDropTablePlusForcedStorm orig, int numOptions, PickupDropTable dropTable, PickupDropTable stormDropTable, Xoroshiro128Plus rng)
        //{
        //    Log.Info("test3");
        //    Log.Info(dropTable);
        //    Log.Info(stormDropTable);
        //    return orig(numOptions, dropTable, stormDropTable, rng);
        //}

        private Option[] test(On.RoR2.PickupPickerController.orig_GenerateOptionsFromDropTable orig, int numOptions, PickupDropTable dropTable, Xoroshiro128Plus rng)
        {
            Log.Info("test1");
            Log.Info(dropTable);
            return orig(numOptions, dropTable, rng);
        }

        private Option[] idk(On.RoR2.PickupPickerController.orig_GenerateOptionsFromArray orig, PickupIndex[] drops)
        {
            Log.Info("test2");
            Log.Info(drops);
            return orig(drops);
        }

        private void fix_scavbag_droptable(On.RoR2.ChestBehavior.orig_BaseItemDrop orig, ChestBehavior self)
        {
            if (self.name.Contains("ScavBackpack"))
            {
                self.dropPickup = self.dropTable.GenerateDrop(self.rng);
            }
            orig(self);
        }

        private void start_blacklist(Run run)
        {
            load_config();
            get_category_weights(ItemTier.Tier1, run);
            get_category_weights(ItemTier.Tier2, run);
            get_category_weights(ItemTier.Tier3, run);
            SotsItemCount = 0;
            init_void_relationships();
            blockedWhites = create_blacklist(run.availableTier1DropList, numWhites, categoryWeightsWhite);
            blockedGreens = create_blacklist(run.availableTier2DropList, numGreens, categoryWeightsGreen);
            blockedReds = create_blacklist(run.availableTier3DropList, numReds, categoryWeightsRed);
            PickupDropTable.RegenerateAll(run);
        }

        private void init_void_relationships()
        {
            voidPairs = new Dictionary<ItemDef, ItemDef>();
            foreach (ItemDef.Pair relationship in ItemCatalog.GetItemPairsForRelationship(Addressables.LoadAssetAsync<ItemRelationshipType>("RoR2/DLC1/Common/ContagiousItem.asset").WaitForCompletion()))
            {
                voidPairs.Add(relationship.itemDef1, relationship.itemDef2);
            }
        }

        private List<PickupIndex> create_blacklist(List<PickupIndex> itemList, int allowedCount, float[] categoryWeights)
        {
            if (allowedCount <= 0)
            {
                return new List<PickupIndex>();
            }

            List<PickupIndex> blockedItems = new List<PickupIndex>(itemList);
            foreach (ItemDef item in voidPairs.Values)
            {
                blockedItems.Add(PickupCatalog.FindPickupIndex(item.itemIndex));
            }

            List<PickupIndex>[] categoryItems = { new List<PickupIndex>(), new List<PickupIndex>(), new List<PickupIndex>(), new List<PickupIndex>() };
            foreach (PickupIndex pickupindex in itemList)
            {
                for (int i = 0; i < categoryTags.Length; i++)
                {
                    if (LootPoolLimiterConfig.SpeedIncluded.Value.Split(", ").Contains(PickupCatalog.GetPickupDef(pickupindex).internalName.Substring(10)) && LootPoolLimiterConfig.speedCategory.Value)
                    {
                        categoryItems[3].Add(pickupindex);
                        break;
                    }
                    if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(pickupindex).itemIndex).ContainsTag(categoryTags[i]))
                    {
                        categoryItems[i].Add(pickupindex);
                    }
                }
            }

            blacklistItem(Random.Range(0, 4));
            for (int i = 1; i < allowedCount && i < itemList.Count; i++)
            {
                int category = Array.IndexOf(categoryWeights, categoryWeights.Max());
                if (categoryItems[category].Count <= 0)
                {
                    i--;
                    categoryWeights[category] -= 100;
                    continue;
                }
                blacklistItem(category);
            }

            void blacklistItem(int category)
            {
                if(categoryItems[category].Count <= 0) { return; }
                int random = Random.Range(0, categoryItems[category].Count);
                ItemDef item = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(categoryItems[category][random]).itemIndex);
                if (voidPairs.ContainsKey(item))
                {
                    blockedItems.Remove(PickupCatalog.FindPickupIndex(voidPairs[item].itemIndex));
                }
                blockedItems.Remove(categoryItems[category][random]);

                if (item.ContainsTag(ItemTag.HalcyoniteShrine))
                {
                    SotsItemCount = Math.Min(SotsItemCount + 1, 2);
                }
                categoryItems[category].RemoveAt(random);
                categoryWeights[category] -= Random.Range(1 - LootPoolLimiterConfig.categoryVariance.Value / 100, 1f);
            }
            return blockedItems;
        }

        private void get_category_weights(ItemTier tier, Run run)
        {
            int numAllowed;
            List<PickupIndex> availableDropList;
            float[] categoryWeights;
            switch (tier)
            {
                case ItemTier.Tier1:
                    availableDropList = run.availableTier1DropList;
                    numAllowed = numWhites;
                    categoryWeights = categoryWeightsWhite;
                    break;
                case ItemTier.Tier2:
                    availableDropList = run.availableTier2DropList;
                    numAllowed = numGreens;
                    categoryWeights = categoryWeightsGreen;
                    break;
                case ItemTier.Tier3:
                    availableDropList = run.availableTier3DropList;
                    numAllowed = numReds;
                    categoryWeights = categoryWeightsRed;
                    break;
                default:
                    return;
            }
            foreach (PickupIndex item in availableDropList)
            {
                //Log.Info(item.pickupDef.internalName.Substring(10));
                //get weights for speed category
                if (LootPoolLimiterConfig.SpeedIncluded.Value.Split(", ").Contains(item.pickupDef.internalName.Substring(10)) && LootPoolLimiterConfig.speedCategory.Value)
                {
                    //speedWeights[(int)tier]++;
                    categoryWeights[3]++;
                }
                else
                {
                    for (int i = Random.Range(3, 6); i >= 0; i--)
                    {
                        if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(item).itemIndex).ContainsTag(categoryTags[i % 3]))
                        {
                            categoryWeights[i % 3]++;
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                categoryWeights[i] = (categoryWeights[i] / availableDropList.Count) * numAllowed;
                Log.Info(categoryWeights[i]);
            }
            categoryWeights[3] = (categoryWeights[3] / availableDropList.Count) * numAllowed;
        }

        public void fix_soup_always_affected(On.RoR2.ShopTerminalBehavior.orig_Start orig, ShopTerminalBehavior self)
        {
            if (!LootPoolLimiterConfig.affectPrinters.Value)
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
            numWhites = parse_itemnum(LootPoolLimiterConfig.ConfigWhites.Value);
            numGreens = parse_itemnum(LootPoolLimiterConfig.ConfigGreens.Value);
            numReds = parse_itemnum(LootPoolLimiterConfig.ConfigReds.Value);
        }

        private void filter_basic_loot(On.RoR2.BasicPickupDropTable.orig_GenerateWeightedSelection orig, BasicPickupDropTable self, Run run)
        {
            orig(self, run);

            if(blockedWhites == null || blockedGreens == null|| blockedReds == null)
            {
                return;
            }
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
            } else if (self.name.Contains("dtChest1"))
            {
                dtChest1 = self;
            }

            if (!affectedPools.Contains(self.name) &&
                !(LootPoolLimiterConfig.affectPrinters.Value && self.name.Contains("dtDuplicator")) &&
                !(LootPoolLimiterConfig.affectCradles.Value && self.name.Contains("dtVoidChest")) &&
                !(LootPoolLimiterConfig.affectVoidKey.Value && self.name.Contains("dtVoidLockbox")))
            {
                return;
            }
            Dictionary<ItemTier, float> prevTierAmount = get_tier_weights(self);

            int numAllowed = self.selector.Count;
            for (int num = self.selector.Count - 1; num >= 0; num--)
            {
                if (blockedWhites.Contains(self.selector.choices[num].value) ||
                    blockedGreens.Contains(self.selector.choices[num].value) ||
                    blockedReds.Contains(self.selector.choices[num].value))
                {
                    numAllowed--;
                }
            }

            for (int num = self.selector.Count - 1; num >= 0; num--)
            {
                if (LootPoolLimiterConfig.blacklistWeight.Value == 0)
                {
                    if (blockedWhites.Contains(self.selector.choices[num].value) ||
                    blockedGreens.Contains(self.selector.choices[num].value) ||
                    blockedReds.Contains(self.selector.choices[num].value))
                    {
                        self.selector.ModifyChoiceWeight(num, 0);
                    }
                }
                else
                {
                    if (!blockedWhites.Contains(self.selector.choices[num].value) &&
                    !blockedGreens.Contains(self.selector.choices[num].value) &&
                    !blockedReds.Contains(self.selector.choices[num].value))
                    {
                        self.selector.ModifyChoiceWeight(num, self.selector.choices[num].weight * (((self.selector.Count - numAllowed) / numAllowed) / (LootPoolLimiterConfig.blacklistWeight.Value / 100) - ((self.selector.Count - numAllowed) / numAllowed) + 1));
                    }
                }
            }
            balance_item_weight(self, prevTierAmount);

            PickupTransmutationManager.RebuildPickupGroups();        
        }

        void filter_printers(On.RoR2.PickupTransmutationManager.orig_RebuildAvailablePickupGroups orig, Run run)
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
            if (stormDropTable == dropTable)
            {
                dropTable = dtChest1;
            }
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

        Dictionary<ItemTier, float> get_tier_weights(BasicPickupDropTable self)
        {
            Dictionary<ItemTier, float> tierAmount = new Dictionary<ItemTier, float>();

            foreach (WeightedSelection<PickupIndex>.ChoiceInfo choice in self.selector.choices)
            {
                if (PickupCatalog.GetPickupDef(choice.value).itemIndex != ItemIndex.None)
                {
                    if (tierAmount.ContainsKey(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier))
                    {
                        tierAmount[ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier] += choice.weight;
                    }
                    else
                    {
                        tierAmount.Add(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(choice.value).itemIndex).tier, choice.weight);
                    }
                }
            }
            return tierAmount;
        }

        void balance_item_weight(BasicPickupDropTable self, Dictionary<ItemTier, float> prevTierAmount)
        {
            Dictionary<ItemTier, float> tierAmount = get_tier_weights(self);
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
    }
}
