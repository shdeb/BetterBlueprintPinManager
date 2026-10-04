using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Assertions;

namespace BetterBlueprintPinManager
{
    internal static class UnitTests
    {

        public static string recipieJsonFilePath = "B:\\Shanyu\\Dev\\GameDev\\Modding\\Subnautica\\recipes_dump.json";
        public static void S1_CannotCraftRoot_WhenCopperIsShared()
        {
            //var recipes = new Dictionary<TechType, RecipeData>
            //{

            //    [TechType.Battery] = MakeRecipe(new Ingredient(TechType.Copper, 1)),
            //    [TechType.PowerCell] = MakeRecipe(new Ingredient(TechType.Battery, 1), new Ingredient(TechType.Copper, 1))
            //};

            //var all = new HashSet<TechType>
            //{
            //    TechType.Copper, TechType.Battery, TechType.PowerCell
            //};

            //var dag = CraftDagService(recipes, all);
            var dag = CraftDagService.GetDagFromJson(recipieJsonFilePath);
            dag.AddInvItem(TechType.Copper, 2);

            dag.Evaluate(TechType.PowerCell, want: 1,
                ingredientsView: out _, has: out _, need: out _, canCraft: out float canCraft);

            Assert.AreEqual(0f, canCraft);
        }


        public static void MaxCraft_IgnoresExistingRoots()
        {
            // arrange a simple chain that can produce 3 extra roots
            // …
            var dag = CraftDagService.GetDagFromJson(recipieJsonFilePath);
            dag.AddInvItem(TechType.PowerCell, 5);   // already own 5

            dag.Evaluate(TechType.PowerCell, want: 1,
                ingredientsView: out _, has: out _, need: out _, canCraft: out float canCraft);

            Assert.AreEqual(3f, canCraft);            // must ignore the existing 5
        }
    }
}
