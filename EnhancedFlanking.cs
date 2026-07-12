using System;
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
    public const string MOD_SETTINGS_GROUP = "EnhancedFlanking";
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

    // HUD
    private const string HUD_ICON_VISIBILITY_KEY = "HUDIconVisibility";
    private static readonly bool DEFAULT_IS_HUD_ICON_VISIBLE = true;
    public const string HUD_ICON_POSITION_X_KEY = "HUDIconPositionX";
    private const int MIN_HUD_ICON_POSITION_X = -10;
    private const int MAX_HUD_ICON_POSITION_X = 10;
    private const int DEFAULT_HUD_ICON_POSITION_X = 0;

    public const string HUD_ICON_POSITION_Y_KEY = "HUDIconPositionY";
    private const int MIN_HUD_ICON_POSITION_Y = -10;
    private const int MAX_HUD_ICON_POSITION_Y = 10;
    private const int DEFAULT_HUD_ICON_POSITION_Y = -2;

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
            .Postfix("Skill", "OnUse", OnUseSkill_Postfix)
            .Postfix("SkillAction", "HandleMouseMoveOnTile", OnHandleMouseMoveOnTile_Postfix)
            .Postfix("SkillAction", "HandleLeftClickOnTile", OnHandleLeftClickOnTile_Postfix)
            .Postfix("SkillAction", "Cancel", OnCancelSkillAction_Postfix)
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
    /// Called after a skill action is canceled, ensuring that any active flank indicator
    /// is removed from the screen
    /// </summary>
    /// <param name="_activeActor">The actor who canceled the skill action.</param>
    public static void OnCancelSkillAction_Postfix(Actor _activeActor)
    {
        FlankPreviewHUDTracker.ClearIcon();
    }

    /// <summary>
    /// Called after a left-click action on a tile, ensuring that any active flank indicator
    /// is removed from the screen.
    /// </summary>
    /// <param name="_tile">The tile that was clicked.</param>
    /// <param name="_activeActor">The actor who performed the click action.</param>
    public static void OnHandleLeftClickOnTile_Postfix(Tile _tile, Actor _activeActor)
    {
        FlankPreviewHUDTracker.ClearIcon();
    }

    /// <summary>
    /// Called after the mouse moves over a tile, updating the flank indicator based on
    /// the current flanking state.
    /// </summary>
    /// <param name="_mouseWorldPos"></param>
    /// <param name="_currentTile"></param>
    /// <param name="_oldTile"></param>
    /// <param name="_activeActor"></param>
    public static void OnHandleMouseMoveOnTile_Postfix(
        Vector3 _mouseWorldPos,
        Tile _currentTile,
        Tile _oldTile,
        Actor _activeActor
    )
    {
        // early return when showing the icon is disabled by user settings
        if (!CheckHudIconSettingEnabled())
        {
            FlankPreviewHUDTracker.ClearIcon();
            return;
        }
        // Show icon if flanking
        if (
            CheckIfValidFlanking(
                FlankStateTracker.CurrentlyEvaluatingSkill,
                _activeActor.GetTile(),
                _currentTile
            )
        )
        {
            FlankPreviewHUDTracker.SetIcon(_currentTile);
        }
        else
        {
            FlankPreviewHUDTracker.ClearIcon();
        }
    }

    /// <summary>
    /// Handles logic to be executed after a skill is used, clearing the flank
    /// evaluation state.
    ///
    /// TODO: Evaluate if this logic can be merged with the SkillAction.HandleLeftClickOnTile
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
        DebugLog($"Cleared flank state for skill {__instance} {__instance.UsageId}");
    }

    /// <summary>
    /// Determines whether the given combination of requirements satisfies the flanking conditions.
    /// </summary>
    /// <param name="_skill">The currently selected skill to use</param>
    /// <param name="_fromTile">The tile from which the skill is being used</param>
    /// <param name="_targetTile">The tile being targeted by the skill</param>
    /// <returns>True if the flanking conditions are satisfied, false otherwise</returns>
    private static bool CheckIfValidFlanking(Skill _skill, Tile _fromTile, Tile _targetTile)
    {
        Entity target = _targetTile.GetEntity();

        // Must be a valid target entity
        if (target == null)
        {
            DebugLog("Failed to resolve target entity");
            return false;
        }

        // Must not be self
        if (_fromTile == _targetTile)
        {
            DebugLog("Cannot flank self");
            return false;
        }

        // Must have a valid skill
        if (_skill == null)
        {
            DebugLog("Skill is null");
            return false;
        }

        // Must be a ranged attack
        if (_skill.GetItem() == null)
        {
            DebugLog("Not a weapon attack (Item is null)");
            return false;
        }

        // And must be infantry
        if (!target.IsInfantry())
        {
            DebugLog($"Target is not infantry");
            return false;
        }

        // Must not have any cover applied, otherwise we are not fully flanking
        float coverMult = _skill.GetCoverMult(
            _fromTile,
            _targetTile,
            target,
            _targetTile.GetEntity().GetCurrentProperties(),
            false
        );
        // Any cover multiplier other than 1 indicates that the target has cover
        // Note that cover multiplier calculation takes into consideration the
        // direction of the attack.
        if (coverMult != 1f)
        {
            DebugLog($"Not flanking completely");
            return false;
        }

        // If all checks pass, we are fully flanking
        DebugLog($"[FLANKING] Fully flanking target entity {target}");
        return true;
    }

    /// <summary>
    /// Handles logic to be executed after the hit chance for a skill has been
    /// calculated, determining and recording in internal state whether the skill is flanking
    /// the target. Applies flanking bonuses when the skill is determined to be flanking.
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
        // set the current skill being evaluated
        FlankStateTracker.CurrentlyEvaluatingSkill = __instance;
        bool result = CheckIfValidFlanking(__instance, _from, _targetTile);
        // Clear flanking icon on failure and reset the currently evaluating skill
        if (!result)
        {
            FlankPreviewHUDTracker.ClearIcon();
            FlankStateTracker.CurrentlyEvaluatingSkill = null;
            return;
        }

        // Update the internal flank state for the skill evaluation
        FlankStateTracker.ActiveFlanks.GetOrCreateValue(__instance);
        float bonusAccPercent = GetFlankingAccBonusPercent();
        float bonusAccMult = 1f + bonusAccPercent / 100f;
        var newFinalAccuracyValue = _properties.GetAccuracy() * bonusAccMult;

        DebugLog(
            $"[FLANKING] BonusAcc={bonusAccPercent}% {_properties.GetAccuracy()} * {bonusAccMult} = {newFinalAccuracyValue}"
        );

        float bonusDmgPercent = GetFlankingDamageBonusPercent();
        var bonusDmgMult = 1f + bonusDmgPercent / 100f;
        var newFinalDamageValue = _properties.GetDamage() * bonusDmgMult;
        DebugLog(
            $"[FLANKING] BonusDmg={bonusDmgPercent}% {_properties.GetDamage()} * {bonusDmgMult} = {newFinalDamageValue}"
        );

        _properties.Accuracy = newFinalAccuracyValue;
        _properties.Damage = newFinalDamageValue;
    }

    /// <summary>
    /// Configures the mod settings for the Enhanced Flanking Mod.
    /// </summary>
    /// <param name="modSettingsGroup">
    /// The group name under which the mod settings should be registered.
    /// </param>
    private static void ConfigureModSettings(string modSettingsGroup)
    {
        ModSettings.Register(
            modSettingsGroup,
            settings =>
            {
                // Settings for accuracy when flanking
                settings.AddHeader("Bonuses");
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

                settings.AddHeader("HUD Icon");
                // Settings for the HUD icon visibility
                settings.AddToggle(
                    HUD_ICON_VISIBILITY_KEY,
                    "Show HUD Icon",
                    DEFAULT_IS_HUD_ICON_VISIBLE
                );

                // Settings for the icon's position
                settings.AddNumber(
                    HUD_ICON_POSITION_X_KEY,
                    "HUD Icon X Position",
                    MIN_HUD_ICON_POSITION_X,
                    MAX_HUD_ICON_POSITION_X,
                    DEFAULT_HUD_ICON_POSITION_X
                );
                settings.AddNumber(
                    HUD_ICON_POSITION_Y_KEY,
                    "HUD Icon Y Position",
                    MIN_HUD_ICON_POSITION_Y,
                    MAX_HUD_ICON_POSITION_Y,
                    DEFAULT_HUD_ICON_POSITION_Y
                );
            }
        );
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
    /// Retrieves the configured HUD icon visibility setting from the mod settings.
    /// </summary>
    /// <returns>True if the HUD icon should be visible, false otherwise.</returns>
    private static bool CheckHudIconSettingEnabled()
    {
        bool value = ModSettings.Get<bool>(MOD_SETTINGS_GROUP, HUD_ICON_VISIBILITY_KEY);
        return value;
    }

    /// <summary>
    /// Called when the Enhanced Flanking plugin is unloaded, performing any necessary
    /// cleanup such as unsubscribing from Harmony patches.
    /// </summary>
    public void OnUnload()
    {
        DebugLog("EnhancedFlanking unloading...");
        // Remove any existing icons from HUD
        FlankPreviewHUDTracker.ClearIcon();
        // Always cleanly unsubscribe when the plugin unloads
        _harmony.UnpatchSelf();
    }
}
