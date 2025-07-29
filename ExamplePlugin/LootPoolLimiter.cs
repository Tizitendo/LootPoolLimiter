using BepInEx;
using Newtonsoft.Json.Utilities;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public const string PluginVersion = "1.1.0";

        public static String[] affectedPools = { "dtChest1" , "dtChest2", "dtSonorousEcho", "dtSmallChestDamage",
            "dtSmallChestHealing", "dtSmallChestUtility", "dtCategoryChest2Damage", "dtCategoryChest2Healing",
            "dtCategoryChest2Utility", "dtCasinoChest", "dtShrineChance", "dtChanceDoll", "dtLockbox", "dtSacrificeArtifact",
            "dtVoidTriple", "dtTier1Item", "dtTier2Item", "dtTier3Item", "GeodeRewardDropTable", "dtShrineHalcyoniteTier1",
            "dtShrineHalcyoniteTier2", "dtShrineHalcyoniteTier3"};


        public static BasicPickupDropTable dtDuplicatorTier1;
        public static BasicPickupDropTable dtDuplicatorTier2;
        public static BasicPickupDropTable dtDuplicatorTier3;
        public static BasicPickupDropTable dtChest1;
        //public static WeightedSelection<PickupIndex> smallChestSelection;

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
        public static float[] speedWeights = new float[3];
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
        }

        private void start_blacklist(Run run)
        {
            load_config();
            //get_weights(categoryWeightsWhite, run.availableTier1DropList, numWhites);
            //get_weights(categoryWeightsGreen, run.availableTier2DropList, numGreens);
            //get_weights(categoryWeightsRed, run.availableTier3DropList, numReds);
            get_category_weights(ItemTier.Tier1, run);
            get_category_weights(ItemTier.Tier2, run);
            get_category_weights(ItemTier.Tier3, run);
            SotsItemCount = 0;
            init_void_relationships();
            blockedWhites = create_blacklist(new List<PickupIndex>(run.availableTier1DropList), numWhites);
            blockedGreens = create_blacklist(new List<PickupIndex>(run.availableTier2DropList), numGreens);
            blockedReds = create_blacklist(new List<PickupIndex>(run.availableTier3DropList), numReds);
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

        private List<PickupIndex> create_blacklist(List<PickupIndex> itemList, int allowedCount)
        {
            if (allowedCount < 0)
            {
                return new List<PickupIndex>();
            }

            List<PickupIndex> blockedItems = new List<PickupIndex>(itemList);
            foreach (ItemDef item in voidPairs.Values)
            {
                blockedItems.Add(PickupCatalog.FindPickupIndex(item.itemIndex));
            }
            List<PickupIndex> blockableItems = new List<PickupIndex>(blockedItems);
            for (int i = 0; i < allowedCount; i++)
            {
                int random = Random.Range(0, blockableItems.Count);
                ItemDef item = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(blockableItems[random]).itemIndex);
                if (countupTag(item))
                {
                    //Log.Info(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(blockableItems[random]).itemIndex).nameToken);
                    blockedItems.Remove(blockableItems[random]);
                    if (voidPairs.ContainsKey(item))
                    {
                        blockedItems.Remove(PickupCatalog.FindPickupIndex(voidPairs[item].itemIndex));
                    }
                    if (item.ContainsTag(ItemTag.HalcyoniteShrine))
                    {
                        SotsItemCount = Math.Min(SotsItemCount + 1, 2);
                    }
                }
                else
                {
                    //Log.Info("no");
                    //Log.Info(ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(blockableItems[random]).itemIndex).nameToken);
                    i--;
                }
                blockableItems.RemoveAt(random);
                if (blockableItems.Count <= 0)
                {
                    break;
                }
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
                    speedWeights[(int)tier]++;
                }
                else
                {
                    for (int i = 0; i < categoryTags.Length; i++)
                    {
                        if (ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(item).itemIndex).ContainsTag(categoryTags[i]))
                        {
                            categoryWeights[i]++;
                        }
                    }
                }
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                categoryWeights[i] = (categoryWeights[i] / availableDropList.Count) * numAllowed;
            }
            speedWeights[(int)tier] = (speedWeights[(int)tier] / availableDropList.Count) * numAllowed;
        }

        private bool countupTag(ItemDef item)
        {
            float[] categoryWeights;
            int speedWeightsTier;
            float min = 0.33f;
            if (LootPoolLimiterConfig.speedCategory.Value)
            {
                min = 0.25f;
            }
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

            // apply weight for speed category
            if (LootPoolLimiterConfig.SpeedIncluded.Value.Split(", ").Contains(PickupCatalog.FindPickupIndex(item.itemIndex).pickupDef.internalName.Substring(10)) && LootPoolLimiterConfig.speedCategory.Value)
            {
                if (speedWeights[(int)item.tier] < min)
                {
                    return false;
                }
                speedWeights[(int)item.tier]--;
                return true;
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                if (item.ContainsTag(categoryTags[i]) && categoryWeights[i] < min)
                {
                    return false;
                }
            }

            for (int i = 0; i < categoryTags.Length; i++)
            {
                if (item.ContainsTag(categoryTags[i]))
                {
                    categoryWeights[i] -= 1 - LootPoolLimiterConfig.forceCategories.Value / 100;
                }
            }
            return true;
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
            } else if(self.name.Contains("dtChest1"))
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
