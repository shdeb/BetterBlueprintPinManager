using BepInEx;
using BepInEx.Logging;
//using BetterBlueprintPinManager.Items.Equipment;
using HarmonyLib;
using Microsoft.Cci;
using Nautilus.Crafting;
using Nautilus.Handlers;
using Nautilus.Json.Converters;
using Nautilus.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using static HandReticle;
using static VFXParticlesPool;


namespace BetterBlueprintPinManager
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("com.snmodding.nautilus")]
    public class Plugin : BaseUnityPlugin
    {
        public new static ManualLogSource Logger { get; private set; }

        private static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();
        private static Harmony hPatches;
        private static Plugin thisPlugin;

        internal static ModConfig modConfig;
        internal static GameInput.Button AutoHidePeekKeybind;
        internal static MyCoroutine myCoroutine;

        internal static CraftDagService dag;
        internal static Dictionary<TechType, int> tInvBuf = new(16);
        internal class _wantDetails
        {
            public uGUI_RecipeEntry recipeEntry;
            public int count;

        }
        internal static Dictionary<TechType, _wantDetails> pinned_wants = new(8);
        internal class _hasNeedCount { public int has, need; }

        //total raw needs temp buffer, should get consumed post all updateIngredients
        internal static Dictionary<TechType, _hasNeedCount> total_raw_needs_buf = new(8);

        ////total raw ingredients, persistant curent/last touched state
        //internal static List<TechType> total_raw_ingredients = new();


        private void Awake()
        {
            thisPlugin = this;
            // set project-scoped logger instance
            Logger = base.Logger;
            ModProfiler.Init(Logger);
            // Initialize custom prefabs
            //InitializePrefabs();

            //register 
            WaitScreenHandler.RegisterLateLoadTask(PluginInfo.PLUGIN_NAME, initBBPM, "Initializing");

            //load mod config
            modConfig = OptionsPanelHandler.RegisterModOptions<ModConfig>();
            //register keybind
            AutoHidePeekKeybind = KeybindHelper.Register(
                "Keybind to peek or show ingredients",
                "When the options 'Auto Hide ingredients or 'Show only Pending ingredients'," +
                " are enabled, this keybind is used to show full ingredients temporarily( AutoHideDelay ).",
                GameInputHandler.Paths.Keyboard.G);
            myCoroutine = this.gameObject.AddComponent<MyCoroutine>();
            myCoroutine.canHide = true;
            //myCoroutine.startAutohide();

            // register harmony patches, if there are any
            hPatches = Harmony.CreateAndPatchAll(Assembly, $"{PluginInfo.PLUGIN_GUID}");
            foreach (var method in hPatches.GetPatchedMethods())
                Logger.LogDebug($"Patched: {method}");
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

        }
        public static void initCraftDag(out Dictionary<TechType, RecipeData> recipeMap)
        {
            var s = ModProfiler.Start();
            //warmup
            CraftDataHandler.GetRecipeData(TechType.Knife);
            // get all valid bluprint recipies
            recipeMap = new Dictionary<TechType, RecipeData>(2000);
            var allTech = new HashSet<TechType>(2000);
            foreach (var groupKvp in CraftData.groups)
            {
                if (groupKvp.Key == TechGroup.Uncategorized)
                    continue; // not shown in blueprints tab  

                foreach (var categoryKvp in groupKvp.Value)
                {
                    foreach (var techType in categoryKvp.Value)
                    {
                        if (techType == TechType.Titanium) continue; // ignore Titanium (scrapmetal dep). assume titanium as raw material.
                        RecipeData recipe = CraftDataHandler.GetRecipeData(techType);
                        if (recipe != null && recipe.ingredientCount > 0)
                        {
                            recipeMap[techType] = recipe;
                            allTech.Add(techType);
                            // also record every ingredient so pure raws appear in the graph
                            allTech.UnionWith(recipe.Ingredients.Select(x => x.techType));
                        }
                    }
                }
            }
            s.LogAndRestart("initCraftDag > blueprint recipeMap loop");
            dag = new CraftDagService(recipeMap, allTech);
            syncInv();
            s.LogAndStop("initCraftDag > dag init");
        }
        private void initBBPM(WaitScreenHandler.WaitScreenTask task)
        {
            Action<string> log = (m) =>
            {
                Logger.LogInfo(m);
                task.Status = m;
            };
            log("Late Initializing...");
            //FindObjectOfType<AtmosphereDirector>().GetComponent<DayNightCycle>().Pause();
            //UnityEngine.Object.FindObjectOfType<AtmosphereDirector>().GetComponent<DayNightCycle>().SetTimePassed(645f);
            //UnityEngine.Object.FindObjectOfType<AtmosphereDirector>().GetComponent<DayNightCycle>().Resume();
            //log("DayNightCycle Paused");

            var s = ModProfiler.Start();

            initCraftDag(out var recipeMap);

            s.LogAndRestart("initBBPM > total initCraftDag took ");

            // trigger ui update ;  for pending ingredients hide
            Plugin.modConfig.triggerUiUpdate();
            // trigger Mode update; for full auto hide
            Plugin.modConfig.triggerModeRefresh();

#if DEBUG
            //string bpRecipesjson = JsonConvert.SerializeObject(recipeMap, Formatting.Indented, new CustomEnumConverter());
            JsonUtils.Save(recipeMap, Path.Combine(Path.GetDirectoryName(Info.Location), "recipes_dump.json"), new CustomEnumConverter());
            s.LogAndStop("initBBPM > json dump");

            //Logger.LogInfo("All blueprint TechTypes:\n" + string.Join("\n", recipeMap.Keys));

            //
            UnitTests.recipieJsonFilePath = Path.Combine(Path.GetDirectoryName(Info.Location), "recipes_dump.json");
            UnitTests.S1_CannotCraftRoot_WhenCopperIsShared();
#endif
            s.Stop();
        }

        private static void syncInv()
        {
            if (tInvBuf.Count == 0 || dag == null) return;
            foreach (var kvp in tInvBuf)
            {
                dag.AddInvItem(kvp.Key , kvp.Value);
            }
            tInvBuf.Clear();
        }
        internal static void OnInvAdd(InventoryItem item)
        {
            if (item == null || item.techType == TechType.None ) return;
            if (dag != null)
            {
                syncInv();
                dag.AddInvItem(item.techType, 1);
            }
            else
            {
                tInvBuf[item.techType] = tInvBuf.TryGetValue(item.techType, out var val) ? val + 1 : 1;
            }
        }
        internal static void OnInvRem(InventoryItem item)
        {
            if (item == null || item.techType == TechType.None ) return;
            if (dag != null)
            {
                syncInv();
                dag.AddInvItem(item.techType, -1); 
            }
            else
            {
                if (tInvBuf.TryGetValue(item.techType, out var val))
                    tInvBuf[item.techType] = val - 1;
            }
        }

        internal static void OnPinAdd(TechType type, uGUI_RecipeEntry recipeEntry)
        {
            var h = recipeEntry.gameObject.AddComponent<MyScrollHandler>();
            h.onScroll += updateWantsOnScroll;
            if (!pinned_wants.ContainsKey(type))
                pinned_wants[type] = new _wantDetails { recipeEntry = recipeEntry, count = 1 };
            recipeEntry.UpdateIngredients(recipeEntry.manager.GetContainer(), true);
        }

        internal static void OnPinRemove(TechType type)
        {
            if (!pinned_wants.ContainsKey(type)) return;
            var h = pinned_wants[type].recipeEntry.gameObject.GetComponent<MyScrollHandler>();
            h.onScroll -= updateWantsOnScroll;
            pinned_wants.Remove(type);
            if (Plugin.modConfig.showTotalRaw) Plugin.modConfig.triggerUiUpdate();

        }
        internal static void updateWantsOnScroll(Vector2 scrollDelta, GameObject gameObject)
        {
            var u_rp = gameObject.GetComponent<uGUI_RecipeEntry>();
            if (pinned_wants.ContainsKey(u_rp.techType))
            {
                int oldCount = pinned_wants[u_rp.techType].count;
                pinned_wants[u_rp.techType].count = Math.Max(1, oldCount + (int)scrollDelta.y);
                if(pinned_wants[u_rp.techType].count != oldCount)
                    u_rp.UpdateIngredients(u_rp.manager.GetContainer(), true);
            }
        }
        private void InitializePrefabs()
        {
            //YeetKnifePrefab.Register();
        }
    }
    public class MyScrollHandler : MonoBehaviour, IScrollHandler
    {
        public delegate void OnScrollEvent(Vector2 scrollDelta, GameObject gameObject);
        public event OnScrollEvent onScroll;

        public void OnScroll(PointerEventData e)
        {

            if (e.scrollDelta.y > 0f) onScroll?.Invoke(Vector2.up, e.pointerCurrentRaycast.gameObject);
            else if (e.scrollDelta.y < 0f) onScroll?.Invoke(Vector2.down, e.pointerCurrentRaycast.gameObject);
        }
    }
    public class MyCoroutine : MonoBehaviour
    {
        public bool canHide = false;
        private Coroutine _AutohideRoutine;
        public void startAutohide() { 
            if (this._AutohideRoutine == null) // allow only one instance
                this._AutohideRoutine = this.StartCoroutine(AutoHideAfterDelay()); 
        }
        public void stopAutohide() {
            if (this.canHide)
            {
                this.canHide = false;
                Plugin.Logger.LogInfo("stopAutohide : canHide is now false");
                // trigger ui update ;  for pending ingredients hide
                Plugin.modConfig.triggerUiUpdate();
                // trigger Mode update; for full auto hide
                Plugin.modConfig.triggerModeRefresh();
            }
            if (this._AutohideRoutine != null) { 
                StopCoroutine(this._AutohideRoutine); this._AutohideRoutine = null; 
                Plugin.Logger.LogInfo("Stopped Autohide Routine");
            }
        }

        IEnumerator AutoHideAfterDelay()
        {
            yield return new WaitForSeconds(Plugin.modConfig.AutoHideDelay);
            this.canHide = true;

            // trigger ui update ;  for pending ingredients hide
            Plugin.modConfig.triggerUiUpdate();
            // trigger Mode update; for full auto hide
            Plugin.modConfig.triggerModeRefresh();

            Plugin.Logger.LogInfo("AutoHideAfterDelay : canHide is now true");
            this._AutohideRoutine = null;
            Plugin.Logger.LogInfo("Finished Autohide Routine now reset to null");
        }
    }
    public static class KeybindHelper
    {
        public static GameInput.Button Register(
            string name, 
            string tooltip,
            string defaultKeyboardPath)
        {
            var builder = EnumHandler
                .AddEntry<GameInput.Button>(PluginInfo.PLUGIN_NAME)
                .CreateInput(name, tooltip);

            if (defaultKeyboardPath != null)
                builder = builder.WithKeyboardBinding(defaultKeyboardPath);

            return builder.Value;
        }
    }
    //public class RawTotalEntry : MonoBehaviour
    //{
    //    private PrefabPool<uGUI_RecipeEntry> pool;
    //    public GameObject prefabEntry;
    //    [AssertNotNull]
    //    public RectTransform canvas;
    //    private void Start()
    //    {
    //        this.pool = new PrefabPool<uGUI_RecipeEntry>(this.prefabEntry, this.canvas, 1, 1, delegate (uGUI_RecipeEntry entry)
    //        {
    //            entry.manager = null;
    //            entry.Deinitialize();
    //        }, delegate (uGUI_RecipeEntry entry)
    //        {
    //            entry.Deinitialize();
    //        });
    //    }

    //    private void OnUpdate()
    //    {
    //        uGUI_RecipeEntry uGUI_RecipeEntry = this.pool.Get();
    //        uGUI_RecipeEntry.rectTransform.anchoredPosition = 
    //    }
    //}

    [HarmonyPatch]
    class MyPatches
    {
        internal static uGUI_PinnedRecipes PinnedRecipes_instance;
        static Color orange = new Color(1f, 0.35f, 0f);
        internal static uGUI_RecipeEntry cloneEntry;
        static bool canShowTotalsUI = false;
        private static bool updatingIngredients = false;
        internal static readonly int modeCount = Enum.GetNames(typeof(uGUI_PinnedRecipes.Mode)).Length;

        // System.Void uGUI_PinnedRecipes::Initialize()
        [HarmonyPostfix]
        [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.Initialize))]
        static void PinnedRecipes_Initialize_postfix(uGUI_PinnedRecipes __instance)
        {
            if(PinnedRecipes_instance != null && !ReferenceEquals(__instance, PinnedRecipes_instance)) 
                Plugin.Logger.LogInfo("U_PR init POFX : WARN : PinnedRecipes_instance NOT CONSISTENT");

            if (!__instance.initialized) return;
            if (PinnedRecipes_instance != null) return;
            Plugin.Logger.LogInfo("U_PR init POFX : try inventory container");
            PinnedRecipes_instance = __instance;
            ItemsContainer inv = __instance.GetContainer();
            if (inv == null) return;  // container not ready yet

            inv.onAddItem += Plugin.OnInvAdd;
            inv.onRemoveItem += Plugin.OnInvRem;
            Plugin.Logger.LogInfo("U_PR init POFX : subed to inventory events");

            Plugin.dag = null;
            Plugin.tInvBuf.Clear();
            Plugin.pinned_wants.Clear();

            Plugin.Logger.LogInfo("U_PR init POFX : init raw totals UI entry prefab clone");
            cloneEntry = UnityEngine.Object
                .Instantiate(PinnedRecipes_instance.prefabEntry, PinnedRecipes_instance.canvas)
                .GetComponent<uGUI_RecipeEntry>();
            cloneEntry.Initialize(TechType.Fabricator);
            cloneEntry.rectTransform.anchoredPosition = new Vector2(-100f, 100f);
            cloneEntry.text.text = string.Empty;
            //cloneEntry.Deinitialize();  //i.e base.gameObject.SetActive(false);
            clear_UI_TotalRaw();

            //PinManager.onAdd += Plugin.OnPinAdd;
            //PinManager.onRemove += Plugin.OnPinRemove;
            //Plugin.Logger.LogInfo("U_PR init POFX : subed to pin mannager events");

        }

        // System.Void uGUI_PinnedRecipes::Deinitialize()
        [HarmonyPrefix]
        [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.Deinitialize))]
        static void PinnedRecipes_Deinitialize_prefix(uGUI_PinnedRecipes __instance)
        {
            if (PinnedRecipes_instance != null && !ReferenceEquals(__instance, PinnedRecipes_instance)) 
                Plugin.Logger.LogInfo("U_PR deinit PRFX : WARN : PinnedRecipes_instance NOT CONSISTENT");
            if (!__instance.initialized) return;
            if (PinnedRecipes_instance == null) return;
            Plugin.Logger.LogInfo("U_PR deinit PRFX : try inventory container");
            PinnedRecipes_instance = null;
            
            ItemsContainer inv = __instance.GetContainer();
            if (inv != null)
            {
                inv.onAddItem -= Plugin.OnInvAdd;
                inv.onRemoveItem -= Plugin.OnInvRem;
                Plugin.Logger.LogInfo("U_PR deinit PRFX : UNsubed to inventory events");
            }
            //PinManager.onAdd -= Plugin.OnPinAdd;
            //PinManager.onRemove -= Plugin.OnPinRemove;
            //Plugin.Logger.LogInfo("U_PR deinit PRFX : UNsubed to pin mannager events");


        }

        //System.Void uGUI_PinnedRecipes::OnPinAdd(TechType)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.OnPinAdd))]
        static void PinnedRecipes_OnPinAdd_postfix(TechType techType, uGUI_PinnedRecipes __instance)
        {
            uGUI_RecipeEntry uGUI_RecipeEntry = __instance.entries.Last();
            Plugin.OnPinAdd(techType, uGUI_RecipeEntry);
            Plugin.Logger.LogInfo($"U_PR OnPinAdd POFX : {techType} added");
        }
        //System.Void uGUI_PinnedRecipes::OnPinRemove(TechType)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.OnPinRemove))]
        static void PinnedRecipes_OnPinRemove_prefix(TechType techType, uGUI_PinnedRecipes __instance)
        {

            Plugin.OnPinRemove(techType);
            Plugin.Logger.LogInfo($"U_PR OnPinRemove PRFX : {techType} Removed");

        }

        // System.Void uGUI_PinnedRecipes::UpdateIngredients(); always runs like update fast loop
        [HarmonyPrefix]
        [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.UpdateIngredients))]
        static bool PinnedRecipes_UpdateIngredients_prefix(uGUI_PinnedRecipes __instance)
        {
            //catch my key change
            if (GameInput.GetButtonDown(Plugin.AutoHidePeekKeybind))
            {
                Plugin.myCoroutine.stopAutohide();
            }
            if (GameInput.GetButtonUp(Plugin.AutoHidePeekKeybind))
            {
                Plugin.myCoroutine.startAutohide();
            }

            //original code
            if (!__instance.ingredientsDirty)
            {
                return false;   // skips the original
            }
            //__instance.ingredientsDirty = false;

            updatingIngredients = true;
            ItemsContainer container = __instance.GetContainer();
            for (int i = 0; i < __instance.entries.Count; i++)
            {
                __instance.entries[i].UpdateIngredients(container, true);
            }
            updatingIngredients = false;

            if (Plugin.modConfig.showTotalRaw)
            {
                update_UI_TotalRaw();
            }
            __instance.ingredientsDirty = false;

            return false;   // skips the original
        }

        //System.Void uGUI_RecipeEntry::SetMode(uGUI_PinnedRecipes/Mode); only runs when mode changed
        [HarmonyPrefix]
        [HarmonyPatch(typeof(uGUI_RecipeEntry), nameof(uGUI_RecipeEntry.SetMode))]
        [HarmonyPatch(new Type[] { typeof(uGUI_PinnedRecipes.Mode)})]
        static bool RecipeEntry_SetMode_prefix(uGUI_PinnedRecipes.Mode mode, uGUI_RecipeEntry __instance)
        {
            bool flag = false;
            bool entriesIsActive = false;
            canShowTotalsUI = false;
            if (mode == uGUI_PinnedRecipes.Mode.Off)
            {
                //flag = false;
                //entriesIsActive = false;
                //canShowTotalsUI = false;
            }
            else if (mode == uGUI_PinnedRecipes.Mode.Blueprints)
            {
                flag = true;
                //entriesIsActive = false;
                //canShowTotalsUI = false;
            }
            else if (mode == uGUI_PinnedRecipes.Mode.Full)
            {
                flag = true;
                canShowTotalsUI = true;
                entriesIsActive = !(Plugin.modConfig.AutoHide && Plugin.myCoroutine.canHide);
            }

            __instance.icon.gameObject.SetActive(flag);
            __instance.text.gameObject.SetActive(flag);
            __instance.canvas.gameObject.SetActive(entriesIsActive);

            if (Plugin.modConfig.showTotalRaw)
            {
                cloneEntry.gameObject.SetActive(canShowTotalsUI);
                //update raw totals entry clone ui visibility
                cloneEntry.canvas.gameObject.SetActive(canShowTotalsUI);
            }

            return false; // skips the original
        }

        //System.Void uGUI_RecipeEntry::UpdateIngredients(ItemsContainer,System.Boolean)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(uGUI_RecipeEntry), nameof(uGUI_RecipeEntry.UpdateIngredients))]
        [HarmonyPatch(new Type[] { typeof(ItemsContainer), typeof(bool)})]
        static bool RecipeEntry_UpdateIngredients_prefix(ItemsContainer container, bool ping, uGUI_RecipeEntry __instance)
        {
            //helper local func
            static void getHasNeedCounts(List<TechType> ingredients, float[] has, float[] need, int i, out TechType techType, out int hasCount, out int needAmount)
            {
                techType = ingredients[i];
                int idx = Plugin.dag.TechToIndex[techType];
                hasCount = Math.Max(0, (int)has[idx]);
                needAmount = Math.Max(0, (int)Math.Ceiling(need[idx]));
            }
            if (Plugin.dag == null)
            {
                Plugin.Logger.LogWarning($"RE_UpdateIng_PRFX > dag is not initialized yet; returning...");
                return false;
            }
            // ===== Logic Start ======
            if (__instance.techType == TechType.Titanium)
                return true; // DO NOT skip, Run the original; as we assume titanium as raw material in our eval

            //if this entry.UpdateIngredients is called directly and showTotalRaw UI is enabled; then do all entry UI refresh
            if (Plugin.modConfig.showTotalRaw && !updatingIngredients)
            {
                __instance.manager.ingredientsDirty = true;     // so that totals acumulate accross all pinned entries
                return false; // skips the original
            }

            if (!Plugin.pinned_wants.TryGetValue(__instance.techType, out var wantDetails))
                return false; // skips the original
            var wantCount = wantDetails.count;

            var s = ModProfiler.Start();
            //# run evaluate
            Plugin.dag.Evaluate(
                root: __instance.techType,
                want: wantCount,
                ingredientsView: out List<TechType> ingredients,
                has: out float[] has,
                need: out float[] need,
                canCraft: out float canCraft,
                considerOnlyRaws: Plugin.modConfig.considerOnlyRaw
                );

            s.LogAndRestart("RE_UpdateIng_PRFX > dag.Evaluate took");

            if (Plugin.modConfig.showOnlyPending && Plugin.myCoroutine.canHide)
            {
                var filterIngredients = new List<TechType>();
                for (int i = 0; i < ingredients.Count; i++)
                {
                    getHasNeedCounts(ingredients, has, need, i, out TechType techType, out int hasCount, out int needAmount);
                    if (hasCount < needAmount) filterIngredients.Add(techType);
                }
                ingredients = filterIngredients;
            }

            //# gui setup stuff

            //add or release item pool, prep for next step
            updateEntryRecipeItemPool(__instance, ingredients.Count);

            //## update gui item has, needs
            for (int i = 0; i < ingredients.Count; i++)
            {
                getHasNeedCounts(ingredients, has, need, i, out TechType techType, out int hasCount, out int needAmount);
                uGUI_RecipeItem uGUI_RecipeItem3 = __instance.items[i];
                uGUI_RecipeItem3.text.color = ((hasCount >= needAmount) ? __instance.manager.colorGreen : __instance.manager.colorRed);
                SetRecipeItem(uGUI_RecipeItem3, techType, hasCount, needAmount, ping);

                //accumulate raw total counts, as uGUI_RecipeEntry::UpdateIngredients will be called on all pinned entries
                if (Plugin.modConfig.showTotalRaw && Plugin.dag.RawIngredients.Contains(techType))
                {
                    if(Plugin.total_raw_needs_buf.TryGetValue(techType, out var val))
                    {
                        val.need += needAmount;
                    }
                    else
                    {
                        Plugin.total_raw_needs_buf[techType] = new() {has = hasCount, need = needAmount };
                    }
                }
            }
            __instance.background.SetActive(ingredients.Count > 0);

            __instance.text.color = ((canCraft >= wantCount) ? __instance.manager.colorGreen : orange);

            s.LogAndStop("RE_UpdateIng_PRFX > RecipeEntry create took");

            if (canCraft > 0 || wantCount > 1)
            {
                if (__instance.min != canCraft || !__instance.text.text.SplitByChar('/')[0].Equals("" + wantCount))
                {
                    __instance.min = (int)canCraft;
                    __instance.text.text = string.Format("{1}/x{0}", IntStringCache.GetStringForInt(__instance.min), IntStringCache.GetStringForInt(wantCount));
                    return false;// skips the original
                }
            }
            else
            {
                __instance.min = int.MinValue;
                __instance.text.text = string.Empty; //string.Format("{0}/x0", IntStringCache.GetStringForInt(wantCount));
            }
            return false; // skips the original
        }

        //static void entry_init(this uGUI_RecipeEntry entry)
        //{

        //}

        /// <summary>
        /// shows Total UI when: <br/>
        /// 1) showTotalRaw true -> total_raw_needs_buf is filled <br/>
        /// 2) when entries.Count > 0 AND total_raw_needs_buf.count > 0 <br/>
        /// </summary>
        private static void update_UI_TotalRaw()
        {
            if(!Plugin.modConfig.showTotalRaw || PinnedRecipes_instance.entries.Count <= 0)
            {
                clear_UI_TotalRaw();
                return;
            }
            if (Plugin.total_raw_needs_buf.Count > 0)
            {
                // Convert keys to a list once before the loop
                var ingredients = new List<TechType>(Plugin.total_raw_needs_buf.Keys);

                //add or release item pool
                updateEntryRecipeItemPool(cloneEntry, ingredients.Count);

                // consume the total_raw_needs
                for (int i = 0; i < ingredients.Count; i++)
                {
                    var techType = ingredients[i];
                    var v = Plugin.total_raw_needs_buf[techType];

                    uGUI_RecipeItem uGUI_RecipeItem3 = cloneEntry.items[i];
                    uGUI_RecipeItem3.text.color = ((v.has >= v.need) ? PinnedRecipes_instance.colorGreen : PinnedRecipes_instance.colorRed);
                    SetRecipeItem(uGUI_RecipeItem3, techType, v.has, v.need, true);

                    //consumed so removed
                    Plugin.total_raw_needs_buf.Remove(techType);

                }
                cloneEntry.background.SetActive(ingredients.Count > 0);
            }

        }
        internal static void clear_UI_TotalRaw()
        {
            //clear pool
            updateEntryRecipeItemPool(cloneEntry, 0);

            Plugin.total_raw_needs_buf.Clear();

            cloneEntry.icon.gameObject.SetActive(false);
            cloneEntry.text.gameObject.SetActive(false);
            cloneEntry.canvas.gameObject.SetActive(false);

            cloneEntry.Deinitialize();
            return;
        }

        /// <summary>add to OR release from RecipeItem pool in RecipeEntry</summary>
        static void updateEntryRecipeItemPool(uGUI_RecipeEntry entry, int Count)
        {
            //## update new items from pool when ingredientsCount changes
            while (entry.items.Count < Count)
            {
                uGUI_RecipeItem uGUI_RecipeItem = entry.pool.Get();
                uGUI_RecipeItem.Initialize();
                entry.items.Add(uGUI_RecipeItem);
            }
            //## remove items from pool when ingredientsCount changes
            while (entry.items.Count > Count)
            {
                int lastIdx = entry.items.Count - 1;
                uGUI_RecipeItem uGUI_RecipeItem2 = entry.items[lastIdx];
                entry.items.RemoveAt(lastIdx);
                entry.pool.Release(uGUI_RecipeItem2);
            }
        }
        static void SetRecipeItem(uGUI_RecipeItem ugui_recipeItem, TechType techType, int has, int needs, bool ping)
        {
            ugui_recipeItem.techType = techType;
            Sprite ttIcon = SpriteManager.Get(techType);
            ugui_recipeItem.icon.SetForegroundSprite(ttIcon);
            if (ping && has > ugui_recipeItem.has && ugui_recipeItem.icon != null)
            {
                try
                {
                    ugui_recipeItem.icon.PunchScale();
                }
                catch (Exception)
                {
                    Plugin.Logger.LogWarning($"SetRecipeItem : at {techType} PunchScale failed.");
                }

            }
            if (ugui_recipeItem.has != has || ugui_recipeItem.needs != needs)
            {
                ugui_recipeItem.has = has;
                ugui_recipeItem.needs = needs;
                ugui_recipeItem.text.text = string.Format("{0}/{1}", IntStringCache.GetStringForInt(ugui_recipeItem.has), IntStringCache.GetStringForInt(ugui_recipeItem.needs));
            }
        }
    }
}