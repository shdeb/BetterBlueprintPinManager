using Nautilus.Json;
using Nautilus.Options.Attributes;
using System;
using UnityEngine;

namespace BetterBlueprintPinManager
{
    /// The Menu attribute allows us to set the title of our section within the "Mods" tab of the options menu.
    [Menu($"Better Blueprint Pin Manager (v{PluginInfo.PLUGIN_VERSION}) -- Options")]
    [ConfigFile("BBPMconfig")]
    public class ModConfig : ConfigFile
    {
        
        [Toggle("Show Total Raw ingredients needed", Tooltip = "Shows a total cumulative raw ingredients UI")]
        [OnChange(nameof(triggerShowTotalsUi))]
        public bool showTotalRaw = true;


        [Toggle("Show only Pending ingredients",Tooltip= "Hides the 'green' ingredients (need is met so requirement is fulfilled), can be shown momentarily with peek keybind")]
        [OnChange(nameof(triggerUiUpdate))]
        public bool showOnlyPending = false;

        [Toggle("Auto Hide ingredients", Tooltip = "Hides ingredients (just like when PDA is open) and only shown momentarily with peek keybind")]
        [OnChange(nameof(triggerAutoHide))]
        public bool AutoHide = false;
        [Slider("Auto Hide Delay Seconds", 0.5f, 60f, DefaultValue = 1f, Tooltip = "This delay time determines when the Auto Hide activates, after peek keybind was pressed")]
        public float AutoHideDelay;
        //[Keybind("Keybind to peek/show ingredients")]
        //public KeyCode AutoHidePeekKeybind = KeyCode.G;

        [Toggle("Consider only Raw ingredients", Tooltip = "Does not show or count intermediate ingredients from inventory toward fulfilling pinned Blueprint item requirements")]
        [OnChange(nameof(triggerUiUpdate))]
        public bool considerOnlyRaw = false;
        
        internal void triggerUiUpdate()
        {
            if(MyPatches.PinnedRecipes_instance !=null) MyPatches.PinnedRecipes_instance.ingredientsDirty = true ;
        }
        internal void triggerModeRefresh()
        {
            if (MyPatches.PinnedRecipes_instance != null) 
                MyPatches.PinnedRecipes_instance.mode = (uGUI_PinnedRecipes.Mode)(((int)MyPatches.PinnedRecipes_instance.mode + 1) % MyPatches.modeCount);
        }
        private void triggerAutoHide()
        {
            if (Plugin.modConfig.AutoHide)
                Plugin.myCoroutine.startAutohide();
            else
                Plugin.myCoroutine.stopAutohide();
            triggerUiUpdate();
            triggerModeRefresh();
        }
        private void triggerShowTotalsUi()
        {
            MyPatches.clear_UI_TotalRaw();
            triggerUiUpdate();
            triggerModeRefresh();
        }
    }
}
