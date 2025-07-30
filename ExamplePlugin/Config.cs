using BepInEx.Configuration;
using System;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;
using System.IO;
using System.Reflection;
using Path = System.IO.Path;

namespace LootPoolLimiter;

public static class LootPoolLimiterConfig
{
    public static ConfigEntry<String> ConfigWhites { get; set; }
    public static ConfigEntry<String> ConfigGreens { get; set; }
    public static ConfigEntry<String> ConfigReds { get; set; }
    public static ConfigEntry<bool> speedCategory { get; set; }
    public static ConfigEntry<String> SpeedIncluded { get; set; }
    public static ConfigEntry<float> categoryVariance { get; set; }
    public static ConfigEntry<float> blacklistWeight { get; set; }
    public static ConfigEntry<bool> affectPrinters { get; set; }
    public static ConfigEntry<bool> affectCradles { get; set; }
    public static ConfigEntry<bool> affectVoidKey { get; set; }


    public static void InitConfig(ConfigFile config)
    {
        ConfigWhites = config.Bind<String>(
        "General",
        "White Items",
                "15-25",
        "How many white items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
        );
        ConfigGreens = config.Bind<String>(
        "General",
        "Green Items",
                "10-15",
        "How many green items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
        );
        ConfigReds = config.Bind<String>(
        "General",
        "Red Items",
                "-1",
        "How many red items are in the loot pool. write 2 numbers seperated by \"-\" to limit the pool a random amount in that range (5-10). -1 for no limit"
        );
        blacklistWeight = config.Bind<float>(
        "General",
        "blacklistWeight",
                0f,
        "The & chance of getting a blacklisted item. If you don't want to remove items, but want to make them less common instead. (0 to completely remove blacklisted items, 100 disables the mod"
        );
        affectPrinters = config.Bind<bool>(
        "General",
        "Affect printers",
                false,
        "Limits the loot pool of printers and cauldrons as well"
        );
        affectCradles = config.Bind<bool>(
        "General",
        "Affect cradles",
                false,
        "Only allow voided variants of whitelisted items from cradles"
        );
        affectVoidKey = config.Bind<bool>(
        "General",
        "Affect voidKey",
                false,
        "Only allow voided variants of whitelisted items from void keyboxes"
        );

        speedCategory = config.Bind<bool>(
       "Categories",
       "Speed Category",
               false,
       "Add a subcategory for speed items, which in combination with low Category ratio variance will ensure that there are always at least some available"
       );
        SpeedIncluded = config.Bind<String>(
        "Categories",
        "Speed Category Contents",
        "SpeedBoostPickup, SprintBonus, AttackSpeedAndMoveSpeed, Hoof, Feather, MoveSpeedOnKill, SprintOutOfCombat, JumpBoost, BoostAllStats",
        "Items that should belong in the speed category, seperated by \", \" (SpeedBoostPickup, SprintBonus)"
        );
        categoryVariance = config.Bind<float>(
        "Categories",
        "Category ratio variance",
                10,
        "Determines how much the ratios can differ from the base game. \nEnsures that you can't get a build with only healing etc.\n Higher values equals more randomness.\n (min:0, max:100)"
        );

        ModSettingsManager.SetModDescription("Randomly remove a set amount of items from the itempool");
        ModSettingsManager.AddOption(new StringInputFieldOption(ConfigWhites));
        ModSettingsManager.AddOption(new StringInputFieldOption(ConfigGreens));
        ModSettingsManager.AddOption(new StringInputFieldOption(ConfigReds));
        ModSettingsManager.AddOption(new StepSliderOption(blacklistWeight, new StepSliderConfig() { min = 0, max = 100, increment = 1f }));

        ModSettingsManager.AddOption(new CheckBoxOption(speedCategory));
        ModSettingsManager.AddOption(new StringInputFieldOption(SpeedIncluded));
        ModSettingsManager.AddOption(new StepSliderOption(categoryVariance, new StepSliderConfig() { min = 0, max = 100, increment = 1f }));
        ModSettingsManager.AddOption(new CheckBoxOption(affectPrinters));
        ModSettingsManager.AddOption(new CheckBoxOption(affectCradles));
        ModSettingsManager.AddOption(new CheckBoxOption(affectVoidKey));
        SetSpriteDefaultIcon();
    }

    static void SetSpriteDefaultIcon()
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