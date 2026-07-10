using System;
using System.IO;
using System.Reflection;
using Il2CppMenace.States;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Tactical;
using Menace.SDK;
using UnityEngine;
using Path = System.IO.Path;

namespace EnhancedFlanking;

/// <summary>
/// A self-contained, static utility class that manages the lifecycle of the
/// flanking preview HUD icon within the tactical scene.
///
/// TODO: Currently loads the flanking icon sprite from data upon first access,
/// but I know modkit already loads assets, I just can't figure out how to get my
/// asset from the modkit asset management system.
/// </summary>
public static class FlankPreviewHUDTracker
{
    private static SimpleWorldSpaceIcon _activeIcon;
    private static Tile _activeTile;

    private static Sprite _cachedFlankingSprite;

    // Dynamically retrieve the texture from your actual mod directory folder
    private static Sprite FlankingIconSprite
    {
        get
        {
            if (_cachedFlankingSprite != null)
                return _cachedFlankingSprite;

            try
            {
                // Grabs the exact directory folder where your mod assembly .dll lives
                string modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // Go up one level, and go to the assets folder
                modFolder = Path.Combine(modFolder, ".."); // Go up one level
                modFolder = Path.GetFullPath(modFolder); // Resolve the absolute path after going up one level

                string assetPath = Path.Combine(modFolder, "assets", "flanking.png");

                if (File.Exists(assetPath))
                {
                    byte[] fileData = File.ReadAllBytes(assetPath);
                    Texture2D texture = new(2, 2);

                    if (ImageConversion.LoadImage(texture, fileData))
                    {
                        Rect rect = new(0.0f, 0.0f, texture.width, texture.height);
                        Vector2 pivot = new(0.5f, 0.5f);
                        _cachedFlankingSprite = Sprite.Create(texture, rect, pivot);
                        return _cachedFlankingSprite;
                    }
                }
                EnhancedFlanking.Log(
                    $"[HUD ERROR] Asset file missing or unreadable at target path: {assetPath}"
                );
            }
            catch (Exception ex)
            {
                EnhancedFlanking.Log(
                    $"[HUD ERROR] Critical exception loading mod texture asset: {ex.Message}"
                );
            }

            return null;
        }
    }

    /// <summary>
    /// Retrieves the current tactical state from the game.
    /// </summary>
    /// <returns>The current tactical state instance, or null if unavailable.</returns>
    private static TacticalState GetState() => TacticalState.Get();

    /// <summary>
    /// Retrieves the current tactical HUD instance from the game, if available.
    /// </summary>
    /// <returns>The current tactical HUD instance, or null if unavailable.</returns>
    private static UITacticalHUD GetHUD() => GetState()?.GetUI()?.GetHUD();

    /// <summary>
    /// Sets the flanking preview HUD icon for the specified title, positioning it
    /// according to the given tile and saved HUD icon position settings.
    /// </summary>
    /// <param name="targetTile">
    /// The tile over which the flanking preview HUD icon should be displayed.
    /// </param>
    public static void SetIcon(Tile targetTile)
    {
        if (GetState() == null)
        {
            EnhancedFlanking.DebugLog("[HUD] Tactical state is null, cannot spawn HUD icon.");
            return;
        }

        if (GetHUD() == null)
        {
            EnhancedFlanking.DebugLog("[HUD] Tactical HUD is null, cannot spawn HUD icon.");
            return;
        }

        Vector3 worldPos = targetTile.GetPos();
        // Modify according to settings for HUD icon position
        worldPos.x += ModSettings.Get<int>(
            EnhancedFlanking.MOD_SETTINGS_GROUP,
            EnhancedFlanking.HUD_ICON_POSITION_X_KEY
        );
        worldPos.y += ModSettings.Get<int>(
            EnhancedFlanking.MOD_SETTINGS_GROUP,
            EnhancedFlanking.HUD_ICON_POSITION_Y_KEY
        );
        _activeTile = targetTile;

        // Check if we already have an icon in the same position
        if (_activeIcon != null)
        {
            if (_activeTile == targetTile)
            {
                EnhancedFlanking.DebugLog(
                    "[HUD] Icon already exists for this tile, skipping spawn."
                );
                return;
            }
            if (_activeTile != targetTile)
            {
                EnhancedFlanking.DebugLog(
                    "[HUD] Icon exists for a different tile, clearing old icon."
                );
                ClearIcon();
            }
        }

        EnhancedFlanking.Log(
            $"[HUD Success] Spawning flanking preview text over {_activeTile} at scene pos {worldPos}"
        );

        // Add the engine-native world space icon
        _activeTile = targetTile;
        _activeIcon = GetHUD().AddSimpleWorldSpaceIcon(FlankingIconSprite, worldPos, 24, 24);
    }

    /// <summary>
    /// Safely cleans up the floating world text graphic to prevent visual clutter and leaks.
    /// </summary>
    public static void ClearIcon()
    {
        if (_activeIcon != null)
        {
            // Explicitly request the engine HUD layer to de-allocate our icon instance
            GetHUD()?.RemoveWorldSpaceIcon(_activeIcon);
            EnhancedFlanking.DebugLog("[HUD] Cleared visual text layer markers.");
        }
        else
        {
            EnhancedFlanking.DebugLog("[HUD] Icon is null");
        }
        _activeIcon = null;
        _activeTile = null;
    }
}
