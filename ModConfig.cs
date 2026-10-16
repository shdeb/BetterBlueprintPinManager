using HarmonyLib;
using Nautilus.Json;
using Nautilus.Options.Attributes;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using static BetterBlueprintPinManager.Plugin;

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
        [Slider("Auto Hide Delay Seconds", 0.5f, 60f, DefaultValue = 4f, Tooltip = "This delay time determines when the Auto Hide activates, after peek keybind was pressed")]
        public float AutoHideDelay = 4f;
        //[Keybind("Keybind to peek/show ingredients")]
        //public KeyCode AutoHidePeekKeybind = KeyCode.G;

        [Toggle("Consider only Raw ingredients", Tooltip = "Does not show or count intermediate ingredients from inventory toward fulfilling pinned Blueprint item requirements")]
        [OnChange(nameof(triggerUiUpdate))]
        public bool considerOnlyRaw = false;

        [Slider("Pinned Icon size", 0.5f, 80f, DefaultValue = 80f, Tooltip = "Pinned Item Icon size; may need to reload save...")]
        [OnChange(nameof(iconSizeChange))]
        public float iconSize = 80f;
        [Slider("Pinned entry height", 55f, 100f, DefaultValue = 100f, Tooltip = "Pinned Item virtical spacing, also allows for more pins; may need to reload save...")]
        [OnChange(nameof(iconSizeChange))]
        public float entryHeight = 100f;
        [Slider("Item Icon size", 0.5f, 80f, DefaultValue = 55f, Tooltip = "Item Icon size; may need to reload save...")]
        [OnChange(nameof(iconSizeChange))]
        public float itemIconSize = 55f;
        [Slider("Item Icon horizontal spacing", -40f, 10f, DefaultValue = 0f, Tooltip = "Item Icon horizontal spacing; may need to reload save...")]
        [OnChange(nameof(iconSizeChange))]
        public float itemIconSpacing = 0f;
        [Slider("Pinned background opacity", 0f, 1f, DefaultValue = 1f, Tooltip = "Pinned Item Background opacity; may need to reload save...")]
        [OnChange(nameof(iconSizeChange))]
        public float bgOpacity = 1f;

        Coroutine debounce = null;
        IEnumerator lazy_ui_update()
        {
            yield return new WaitForSeconds(0.3f);
            MyPatches.Initialize_UI_TotalRaw();
            triggerHardUpdate();
            debounce = null;
        }
        internal void iconSizeChange()
        {
            Plugin.Logger.LogInfo("ModConfig : iconSizeChange");
            //var iconSize_field = AccessTools.Field(typeof(uGUI_RecipeEntry), "iconSize");
            //iconSize_field.SetValue(null, new Vector2(iconSize, iconSize));

            var itemIconSize_field = AccessTools.Field(typeof(uGUI_RecipeEntry), "itemIconSize");
            itemIconSize_field.SetValue(null, new Vector2(itemIconSize, itemIconSize));

            //var itemIconOffset_field = AccessTools.Field(typeof(uGUI_RecipeEntry), "itemIconOffset");
            //itemIconOffset_field.SetValue(null, new Vector2(itemIconOffset, itemIconOffset));

            if (MyPatches.PinnedRecipes_instance != null)
            {
                var entryHeight_field = AccessTools.Field(typeof(uGUI_PinnedRecipes), "entryHeight");
                entryHeight_field.SetValue(MyPatches.PinnedRecipes_instance, entryHeight);
                MyPatches.PinnedRecipes_instance.UpdateMaxPins();
            }

            if (debounce == null)
                debounce = UWE.CoroutineHost.StartCoroutine(lazy_ui_update());
        }

        internal void triggerUiUpdate()
        {
            Plugin.Logger.LogInfo("ModConfig : triggerUiUpdate");
            if (MyPatches.PinnedRecipes_instance !=null) MyPatches.PinnedRecipes_instance.ingredientsDirty = true ;
        }
        internal void triggerModeRefresh()
        {
            Plugin.Logger.LogInfo("ModConfig : triggerModeRefresh");
            if (MyPatches.PinnedRecipes_instance != null) 
                MyPatches.PinnedRecipes_instance.mode = (uGUI_PinnedRecipes.Mode)(((int)MyPatches.PinnedRecipes_instance.mode + 1) % MyPatches.modeCount);
        }
        private void triggerAutoHide()
        {
            Plugin.Logger.LogInfo("ModConfig : triggerModeRefresh");
            if (Plugin.modConfig.AutoHide)
                Plugin.myCoroutine.startAutohide();
            else
                Plugin.myCoroutine.stopAutohide();
            triggerUiUpdate();
            triggerModeRefresh();
        }
        private void triggerShowTotalsUi()
        {
            Plugin.Logger.LogInfo("ModConfig : triggerShowTotalsUi");
            MyPatches.clear_UI_TotalRaw();
            triggerUiUpdate();
            triggerModeRefresh();
        }

        private void triggerHardUpdate()
        {
            Plugin.Logger.LogInfo("ModConfig : triggerHardUpdate");
            var pins = new List<TechType>();
            using (IEnumerator<TechType> i = PinManager.GetPins()) {
                while (i.MoveNext())
                    pins.Add(i.Current);
            }

            // bkp pinned_wants status
            var pw = new Dictionary<TechType, _wantDetails>(Plugin.pinned_wants);

            // check consistency
            if (!pw.Keys.ToSet().SetEquals(pins.ToSet())) 
                Plugin.Logger.LogWarning($"pinned wants NOT CONSISTENT with PinManager.GetPins() ! \npw:{pw.Keys}\ngp:{pins}");

            foreach (var tt in pins) {
                PinManager.SetPin(tt,false);
            }
            if (MyPatches.PinnedRecipes_instance != null)
            {
                MyPatches.PinnedRecipes_instance.pool = null;
                MyPatches.PinnedRecipes_instance.pool = new PrefabPool<uGUI_RecipeEntry>(
                    MyPatches.PinnedRecipes_instance.prefabEntry, 
                    MyPatches.PinnedRecipes_instance.canvas, 
                    10, 4, 
                    delegate (uGUI_RecipeEntry entry)
                    {
                        entry.manager = MyPatches.PinnedRecipes_instance;
                        entry.Deinitialize();
                    }, delegate (uGUI_RecipeEntry entry)
                    {
                        entry.Deinitialize();
                    }
                );
            }
            foreach (var tt in pins) {
                PinManager.SetPin(tt, true);
            }

            // reset pinned_wants status from bkp
            foreach(TechType tt in Plugin.pinned_wants.Keys)
            {
                if (pw.TryGetValue(tt, out var pww))
                    Plugin.pinned_wants[tt].count = pww.count;
            }

            triggerUiUpdate();
            triggerModeRefresh();
        }
    }
}
