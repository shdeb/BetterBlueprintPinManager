using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nautilus.Crafting;
using Nautilus.Json.Converters;
using Nautilus.Utility;
using static UnityEngine.SpookyHash;
using static VFXParticlesPool;
//using Nautilus.Handlers;          // RecipeData
//using UnityEngine;                // for Mathf if needed

namespace BetterBlueprintPinManager
{
    
    public sealed class CraftDagService
    {
        // -- dense mapping ----------------------
        private readonly Dictionary<TechType, int> _techToIdx;
        private readonly TechType[] _idxToTech;
        private readonly int _n;

        // -- matrix & topology -------------------------------
        private readonly float[] _costs;  // [i * n + j] = amount of j needed for one i
        private readonly int[] _topo;         // topological order (leaves → roots)
        private readonly int[] _topoRank;    // inverse of _topo
        // live dense inventory – updated incrementally
        private readonly float[] _inv;
        private readonly Dictionary<TechType, List<TechType>> _dependencies;
        private readonly HashSet<TechType> _rawIngredients;

        // -- public read-only view ------------------------------
        public int TypeCount => _n;
        public float[] Inventory => _inv;
        public IReadOnlyDictionary<TechType, int> TechToIndex => _techToIdx;
        public TechType GetTech(int idx) => _idxToTech[idx];
        public IReadOnlyDictionary<TechType, List<TechType>> Dependencies => _dependencies;
        public IReadOnlyCollection<TechType> RawIngredients => _rawIngredients;


        // --------------------------------------------------
        // Construction (call once after finished collecting recipes)
        // ------------------------------------------------------
        public CraftDagService(
            IReadOnlyDictionary<TechType, RecipeData> recipesWithIngredients,
            IReadOnlyCollection<TechType> allMentionedTechTypes)
        {
            //Build dense index
            
            _n = allMentionedTechTypes.Count;
            _idxToTech = allMentionedTechTypes.OrderBy(tt => (int)tt).ToArray();
            _techToIdx = new Dictionary<TechType, int>(_n);
            _dependencies = new Dictionary<TechType, List<TechType>>(_n);
            _rawIngredients = new HashSet<TechType>();

            int idx = 0;
            foreach (var tt in _idxToTech)
            {
                _techToIdx[tt] = idx++;
            }

            //Allocate flat cost matrix (row-major)
            _costs = new float[_n * _n];

            // Fill matrix + build adjacency for topo
            var inDegree = new int[_n];
            var dependents = new List<int>[_n];   // who needs me
            for (int i = 0; i < _n; i++) dependents[i] = new List<int>(16);

            foreach (var kvp in recipesWithIngredients)
            {
                if (!_techToIdx.TryGetValue(kvp.Key, out int r_idx)) continue;
                var recipe = kvp.Value;
                if (recipe == null || recipe.ingredientCount == 0) continue;

                for (int k = 0; k < recipe.ingredientCount; k++)
                {
                    var ing = recipe.Ingredients[k];
                    if (!_techToIdx.TryGetValue(ing.techType, out int i_idx)) continue;
                    //unit costs
                    _costs[r_idx * _n + i_idx] = (float)ing.amount / recipe.craftAmount;
                    // for topo
                    dependents[i_idx].Add(r_idx);
                    inDegree[r_idx]++;
                }
            }

            //Kahn topological sort (leaves first) + transitive dependency lists
            _topo = new int[_n];
            _topoRank = new int[_n];
            var queue = new Queue<int>(_n);

            var dependencies = new List<int>[_n];   // what is needed to make me
            var seen = new HashSet<int>[_n];

            for (int i = 0; i < _n; i++)
            {
                if (inDegree[i] == 0)
                {
                    queue.Enqueue(i);
                    // at this point, inDegree[i] == 0 means, raw ingredients
                    _rawIngredients.Add(_idxToTech[i]);
                }

                dependencies[i] = new List<int>();
                seen[i] = new HashSet<int>();

                // pre-populate the main deps dict
                _dependencies[_idxToTech[i]] = new List<TechType>();
            }


            int pos = 0;
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                _topo[pos] = u;
                _topoRank[u] = pos++;
                foreach (int v in dependents[u])
                {
                    if (--inDegree[v] == 0)
                        queue.Enqueue(v);

                    // u is a direct dependency of v; everything u depends on is also a dependency of v
                    // u's own dependencies come first (they must be built before u)
                    for (int i = 0; i < dependencies[u].Count; i++)
                        if (seen[v].Add(dependencies[u][i]))
                            dependencies[v].Add(dependencies[u][i]);
                    // then u itself
                    if (seen[v].Add(u))
                        dependencies[v].Add(u);
                }
            }

            // If the graph contained a cycle we simply leave the remaining
            // nodes unordered – Subnautica recipes are acyclic, so this is fine.

            //populate _dependencies dict
            for (int i = 0; i < dependencies.Length; i++)
                foreach (int itt in dependencies[i].OrderBy(ttidx => _topoRank[ttidx]))
                    _dependencies[_idxToTech[i]].Add(_idxToTech[itt]);

            //init inventory buffer
            _inv = new float[_n];
        }
        internal static CraftDagService GetDagFromJson(string RecipieDumpJsonFilePath)
        {
            var recipeMap = JsonUtils.Load<Dictionary<TechType, RecipeData>>(RecipieDumpJsonFilePath, false, new CustomEnumConverter());
            var allTech = new HashSet<TechType>(2000);
            foreach (var kvp in recipeMap)
            {
                allTech.Add(kvp.Key);
                allTech.UnionWith(kvp.Value.Ingredients.Select(x => x.techType));
            }
            return new CraftDagService(recipeMap, allTech);
        }

        /// <summary>
        /// O(1) inventory update
        /// </summary>
        public void AddInvItem(TechType tt, int count)
        {
            if (_techToIdx.TryGetValue(tt, out int idx))
                _inv[idx] = Math.Max(0, _inv[idx] + count);
        }


        /// <summary>
        /// Bulk load once
        /// </summary>
        public void LoadInventory(IReadOnlyDictionary<TechType, int> inventory)
        {
            Array.Clear(_inv, 0, _n);
            foreach (var kvp in inventory)
                if (_techToIdx.TryGetValue(kvp.Key, out int idx))
                    _inv[idx] = kvp.Value;
        }

        // --------------------------------------------------
        // Core calculation – allocation-free after the first call
        // ----------------------------------------------
        /// <summary>
        /// Computes everything needed for a pinned root.
        /// </summary>
        /// <param name = "root" > TechType that is pinned</param>
        /// <param name = "want" > How many the player wants(usually ≥ 1)</param>
        /// <param name = "ingredientsView" > Output: all dependencies of root; topo order</param>
        /// <param name = "has" > Output: inventory has counts</param>
        /// <param name = "need" > Output: need[i] for every node (to satisfy 'want' of root)</param>
        /// <param name = "canCraft" > Maximum *new* root items that can be crafted right now</param>
        /// <param name="considerOnlyRaws"></param>
        public void Evaluate(
            TechType root,
            int want,
            out List<TechType> ingredientsView,
            out float[] has,
            out float[] need,
            out float canCraft, 
            bool considerOnlyRaws = false)
        {
            has = new float[_n];
            need = new float[_n];
            ingredientsView = new List<TechType>();

            // -- original inventory (untouched) ----------------------
            Array.Copy(_inv, has, _n);

            if (!_techToIdx.TryGetValue(root, out int rootIdx) || want <= 0)
            {
                canCraft = 0f;
                return;
            }

            List<TechType> ingredients = new();
            if (!Dependencies.TryGetValue(root, out ingredients)) 
                Plugin.Logger.LogDebug("");

            if (considerOnlyRaws)
            {
                foreach (var ingr in ingredients)
                    if (_rawIngredients.Contains(ingr)) ingredientsView.Add(ingr);
            }
            else ingredientsView = ingredients;


            // -- build the dependency cone (root + everything it needs) ----
            // Uses the pre-computed list + root; O(cone size)
            var cone = new bool[_n];
            cone[rootIdx] = true;
            foreach (var tt in ingredients)
                if (_techToIdx.TryGetValue(tt, out int idx))
                    cone[idx] = true;

            // -- demand propagation → need[] --------------------
            // Working copy of inventory so we can consume intermediates
            getFilteredInventory(rootIdx,out float[] inv, considerOnlyRaws);
            inv[rootIdx] = 0f;                   // ignore existing roots

            float[] demand = new float[_n];
            demand[rootIdx] = want;

            // reverse topo (complex → basic)
            for (int t = _n - 1; t >= 0; t--)
            {
                int i = _topo[t];
                if (!cone[i]) continue;

                float d = demand[i];
                if (d <= 0f) continue;

                need[i] = d;                     // total required of this item

                float take = Math.Min(d, inv[i]);
                inv[i] -= take;
                d -= take;

                if (d <= 0f) continue;

                // shortfall → expand recipe
                int row = i * _n;
                for (int j = 0; j < _n; j++)
                {
                    float c = _costs[row + j];
                    if (c > 0f)
                        demand[j] += d * c;
                }
            }

            // -- canCraft (single forward pass on the cone only) -------
            canCraft = MaxCraft(rootIdx, cone, considerOnlyRaws);
        }


        /// <summary>
        /// Maximum number of *new* roots that can be produced.
        /// Existing roots are ignored. Only nodes inside the cone are crafted,
        /// so unrelated items cannot steal shared resources.
        /// </summary>
        private float MaxCraft(int rootIdx, bool[] cone, bool considerOnlyRaws = false)
        {
            getFilteredInventory(rootIdx,out float[] inv, considerOnlyRaws);
            inv[rootIdx] = 0f;                   // ignore existing roots

            // forward topo (basic → complex), only inside the cone
            for (int t = 0; t < _n; t++)
            {
                int i = _topo[t];
                if (!cone[i]) continue;

                float extra = float.PositiveInfinity;
                int row = i * _n;
                bool hasRecipe = false;

                for (int j = 0; j < _n; j++)
                {
                    float c = _costs[row + j];
                    if (c > 0f)
                    {
                        extra = Math.Min(extra, inv[j] / c);
                        hasRecipe = true;
                    }
                }

                if (!hasRecipe)
                    extra = 0f;                  // raw material
                else if (float.IsPositiveInfinity(extra))
                    extra = 0f;

                if (i == rootIdx)
                    return extra;                // answer – do not write it back

                // craft the intermediate and consume its ingredients
                if (extra > 0f)
                {
                    inv[i] += extra;
                    for (int j = 0; j < _n; j++)
                    {
                        float c = _costs[row + j];
                        if (c > 0f)
                            inv[j] -= extra * c;
                    }
                }
            }

            return 0f;
        }
        //helper func
        private void getFilteredInventory(int rootIdx,out float[] inv, bool considerOnlyRaws = false, bool ignoreRoot = true)
        {
            inv = new float[_n];
            Array.Copy(_inv, inv, _n);
            if (considerOnlyRaws)
                for (int i = 0; i < _n; i++) {
                    TechType c = _idxToTech[i];
                    if(!_rawIngredients.Contains(c))
                        inv[i] = 0f;
                }
            if (ignoreRoot) inv[rootIdx] = 0f;

        }

    }
}