using System;
using System.Runtime.CompilerServices;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using MelonLoader;
using Menace.ModpackLoader;
using Menace.SDK;
using UnityEngine;

namespace EnhancedFlanking;

public class EnhancedFlanking : IModpackPlugin
{
    private static EnhancedFlanking _instance;
    private MelonLogger.Instance _log;
    private HarmonyLib.Harmony _harmony;
    private const string MOD_SETTINGS_GROUP = "EnhancedFlanking";
    private const string _logPrefix = "";

    // Debugging logs
    private const string DEBUG_LOGGING_KEY = "DebugLogging";
    private static readonly bool DEFAULT_IS_DEBUG_LOGGING = false;

    // accuracy
    private const string FLANKING_BONUS_PERCENT_KEY = "FlankingBonusPercent";
    private const float MIN_ACC_BONUS_PERCENT = 0f;
    private const float MAX_ACC_BONUS_PERCENT = 100f;
    private const float DEFAULT_ACC_FLANKING_BONUS_PERCENT = 20f;

    // damage
    private const string FLANKING_DAMAGE_BONUS_PERCENT_KEY = "FlankingDamageBonusPercent";
    private const float MIN_FLANKING_DAMAGE_BONUS_PERCENT = 0f;
    private const float MAX_FLANKING_DAMAGE_BONUS_PERCENT = 100f;
    private const float DEFAULT_FLANKING_DAMAGE_BONUS_PERCENT = 20f;

    /// <summary>
    /// Logs a message to the mod's logger (MelonLoader's logging system)
    /// </summary>
    /// <param name="message">The message to log</param>
    public static void Log(string message)
    {
        _instance?._log.Msg($"{_logPrefix} {message}");
    }

    /// <summary>
    /// Logs a message to the mod's logger (MelonLoader's logging system) only if the
    /// debug flag IS_DEBUG_LOGGING is set to true.
    /// </summary>
    /// <param name="message">The message to log if debug logging is enabled.</param>
    public static void DebugLog(string message)
    {
        if (ModSettings.Get<bool>(MOD_SETTINGS_GROUP, DEBUG_LOGGING_KEY))
        {
            Log(message);
        }
    }

    /// <summary>
    /// Initializes the mod, setting up the logger, Harmony instance,
    /// and applying necessary patches.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="harmony"></param>
    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        _instance = this;
        _log = logger;
        _harmony = harmony;

        // configure the settings
        ConfigureModSettings(MOD_SETTINGS_GROUP);

        DebugLog("Applying patch...");
        var patchesApplied = new PatchSet(_harmony, "EnhancedFlanking")
            .Prefix("Skill", "GetHitchance", OnGetHitChance_Prefix)
            .Prefix(
                "WeaponTemplate",
                "ApplyToEntityProperties",
                OnWeaponTemplateApplyToProperties_Prefix
            )
            .Postfix("Skill", "OnUse", OnUseSkill_Postfix)
            .Apply();

        DebugLog($"Patches applied: {patchesApplied}");
    }

    /// <summary>
    /// Called when a scene has finished loading. This method can be used
    /// to perform any scene-specific initialization or setup.
    /// </summary>
    /// <param name="buildIndex">The build index of the loaded scene.</param>
    /// <param name="sceneName">The name of the loaded scene.</param>
    public void OnSceneLoaded(int buildIndex, string sceneName) { }

    /// <summary>
    /// Handles logic to be executed after a skill is used, clearing the flank evaluation state.
    /// </summary>
    /// <param name="__instance">The skill instance that was used.</param>
    /// <param name="_user">The actor who used the skill.</param>
    /// <param name="_targetTile">The tile targeted by the skill.</param>
    /// <param name="_usageParams">Additional usage parameters for the skill</param>
    public static void OnUseSkill_Postfix(
        Skill __instance,
        Actor _user,
        Tile _targetTile,
        UsageParameter _usageParams
    )
    {
        // Clear the thread assignment so it doesn't leak into subsequent non-skill tooltips
        FlankStateTracker.CurrentlyEvaluatingSkill = null;
        DebugLog($"Finished OnAfterUseSkill for __instance={__instance}");
    }

    /// <summary>
    /// Handles logic to be executed after the hit chance for a skill has been
    /// calculated, determining and recording whether the skill is flanking
    /// the target. This allows subsequent systems to query the flanking
    /// state for the skill evaluation.
    /// </summary>
    /// <param name="__instance">The skill instance for which the hit chance was calculated.</param>
    /// <param name="_from">The tile from which the skill is being used.</param>
    /// <param name="_targetTile">The tile targeted by the skill.</param>
    /// <param name="_properties">The properties of the entity using the skill.</param>
    /// <param name="_defenderProperties">The properties of the entity being targeted by the skill.</param>
    /// <param name="_overrideTargetEntity">An optional entity that overrides the target for the skill evaluation.</param>
    /// <param name="_includeDropoff">Indicates whether hit chance dropoff should be included in the calculation.</param>
    /// <param name="_forImmediateUse">Indicates whether the skill is being evaluated for immediate use.</param>
    /// <param name="__result">The calculated hit chance for the skill.</param>
    private static void OnGetHitChance_Prefix(
        Skill __instance,
        Tile _from,
        Tile _targetTile,
        EntityProperties _properties,
        EntityProperties _defenderProperties,
        Entity _overrideTargetEntity,
        bool _includeDropoff,
        bool _forImmediateUse
    )
    {
        // Get or create the unique thread/GC-safe context for this specific skill instance
        var context = FlankStateTracker.ActiveFlanks.GetOrCreateValue(__instance);

        Entity target = _targetTile.GetEntity();

        if (target == null)
        {
            DebugLog("Failed to resolve target entity");
            context.IsFlanking = false; // Explicitly clear it if conditions fail
            return;
        }

        // Must be a ranged attack
        if (__instance.GetItem() == null)
        {
            DebugLog("Not a weapon attack");
            context.IsFlanking = false; // Explicitly clear it if conditions fail
            return;
        }

        // Calculate flanking state and save it directly into our persistent context object
        FlankStateTracker.CurrentlyEvaluatingSkill = __instance;

        // And must be infantry
        if (!target.IsInfantry())
        {
            DebugLog($"Target is not infantry");
            context.IsFlanking = false; // Explicitly clear it if conditions fail
            return;
        }

        // Must not have any cover applied, otherwise we are not fully flanking
        float coverMult = __instance.GetCoverMult(
            _from,
            _targetTile,
            target,
            _defenderProperties,
            false
        );
        if (coverMult != 1f)
        {
            // Any amount of cover means we are not fully flanking
            DebugLog($"Not flanking completely");
            context.IsFlanking = false; // Explicitly clear it if conditions fail
            return;
        }

        context.IsFlanking = true;

        float bonusPercent = GetFlankingAccBonusPercent();
        var newFinalAccuracyValue = _properties.GetAccuracy() * (1f + bonusPercent / 100f);

        DebugLog(
            $"[Flanking] Bonus={bonusPercent}% New final value: {newFinalAccuracyValue} (was: {_properties.GetAccuracy()})"
        );

        _properties.Accuracy = newFinalAccuracyValue;
    }

    /// <summary>
    /// Applies flanking-related modifications to a weapon template's properties
    /// based on the current skill evaluation context. If the skill that triggered
    /// this evaluation is actively flanking, the weapon's damage multiplier
    /// will be adjusted accordingly.
    ///
    /// Because the engine decouples Skills from WeaponTemplates, they execute sequentially on the same thread.
    /// 1. GetHitchance ran first, verified the geometric flank, and cached the active Skill pointer to 'CurrentlyEvaluatingSkill'.
    /// 2. This line pulls that cached Skill pointer and checks our master ledger (ConditionalWeakTable).
    /// 3. If a match is found and 'IsFlanking' is true, we know this specific damage payload belongs to the flanking shot.
    /// </summary>
    /// <param name="__instance">The weapon template instance</param>
    /// <param name="_properties">The target's properties</param>
    public static void OnWeaponTemplateApplyToProperties_Prefix(
        WeaponTemplate __instance,
        EntityProperties _properties
    )
    {
        // Grab the skill that put us into this calculation loop
        Skill activeSkill = FlankStateTracker.CurrentlyEvaluatingSkill;

        // Because the engine decouples Skills from WeaponTemplates, they execute sequentially on the same thread.
        // 1. GetHitchance ran first, verified the geometric flank, and cached the active Skill pointer to 'CurrentlyEvaluatingSkill'.
        // 2. This line pulls that cached Skill pointer and checks our master ledger (ConditionalWeakTable).
        // 3. If a match is found and 'IsFlanking' is true, we know this specific damage payload belongs to the flanking shot.
        if (
            activeSkill != null
            && FlankStateTracker.ActiveFlanks.TryGetValue(activeSkill, out var context)
        )
        {
            if (context.IsFlanking)
            {
                float dmgBonusPercent = GetFlankingDamageBonusPercent();
                float dmgMultiplier = 1f + (dmgBonusPercent / 100f);
                float newDamage = _properties.Damage * dmgMultiplier;
                DebugLog(
                    $"Buffed damage by {dmgBonusPercent}% ({dmgMultiplier}x) - from: {_properties.Damage} to: {newDamage}"
                );
                _properties.Damage = newDamage;
            }
        }
    }

    /// <summary>
    /// Returns the configured flanking accuracy bonus percentage from the mod settings.
    /// </summary>
    /// <returns>The flanking accuracy bonus percentage to apply.</returns>
    private static float GetFlankingAccBonusPercent()
    {
        float value = ModSettings.Get<float>(MOD_SETTINGS_GROUP, FLANKING_BONUS_PERCENT_KEY);

        return Math.Clamp(value, MIN_ACC_BONUS_PERCENT, MAX_ACC_BONUS_PERCENT);
    }

    /// <summary>
    /// Retrieves the configured flanking damage bonus percentage from the mod settings.
    /// Ensures the value is within the allowed range, otherwise returns the default value.
    /// </summary>
    /// <returns>The flanking damage bonus percentage to apply.</returns>
    private static float GetFlankingDamageBonusPercent()
    {
        float value = ModSettings.Get<float>(MOD_SETTINGS_GROUP, FLANKING_DAMAGE_BONUS_PERCENT_KEY);
        if (value < MIN_FLANKING_DAMAGE_BONUS_PERCENT || value > MAX_FLANKING_DAMAGE_BONUS_PERCENT)
        {
            value = DEFAULT_FLANKING_DAMAGE_BONUS_PERCENT;
        }

        return value;
    }

    /// <summary>
    /// Configures the mod settings for the Enhanced Flanking plugin, registering the
    /// accuracy and damage bonus percentage settings so they can be adjusted in the
    /// mod settings UI.
    /// </summary>
    /// <param name="modSettingsGroup">The group name under which the mod settings should be registered.</param>
    private static void ConfigureModSettings(string modSettingsGroup)
    {
        ModSettings.Register(
            modSettingsGroup,
            settings =>
            {
                // Settings for accuracy when flanking
                settings.AddHeader("Accuracy Bonus");
                settings.AddSlider(
                    FLANKING_BONUS_PERCENT_KEY,
                    "Accuracy Bonus (%)",
                    MIN_ACC_BONUS_PERCENT,
                    MAX_ACC_BONUS_PERCENT,
                    DEFAULT_ACC_FLANKING_BONUS_PERCENT
                );

                // Settings for damage when flanking
                settings.AddSlider(
                    FLANKING_DAMAGE_BONUS_PERCENT_KEY,
                    "Damage Bonus (%)",
                    MIN_FLANKING_DAMAGE_BONUS_PERCENT,
                    MAX_FLANKING_DAMAGE_BONUS_PERCENT,
                    DEFAULT_FLANKING_DAMAGE_BONUS_PERCENT
                );

                // Settings for debug logging
                settings.AddToggle(
                    DEBUG_LOGGING_KEY,
                    "Enable Debug Logging",
                    DEFAULT_IS_DEBUG_LOGGING
                );
            }
        );
    }

    /// <summary>
    /// Called when the Enhanced Flanking plugin is unloaded, performing any necessary
    /// cleanup such as unsubscribing from Harmony patches.
    /// </summary>
    public void OnUnload()
    {
        DebugLog("EnhancedFlanking unloadeding...");
        // Always cleanly unsubscribe when the plugin unloads
        _harmony.UnpatchSelf();
    }
}

/// <summary>
/// Tracks the flanking state for skills during combat math evaluation, allowing
/// the Enhanced Flanking plugin to determine whether a skill is currently
/// benefiting from a flanking bonus. Maintains a mapping of active flanks
/// per skill and provides a thread-static reference to the skill currently
/// being evaluated.
/// </summary>
public static class FlankStateTracker
{
    public static readonly ConditionalWeakTable<Skill, FlankContext> ActiveFlanks = new();

    // The bridge: keeps track of which skill is currently evaluating combat math
    [System.ThreadStatic]
    public static Skill CurrentlyEvaluatingSkill;

    public class FlankContext
    {
        public bool IsFlanking;
    }
}
