using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PerishableSpoilTimeFilter
{
    static class SpoilTimeCalc {
        public static int convertToTicks(int spoilTime)
        {
            switch (spoilTime)
            {
                // 0-6 h
                case 0: case 1: case 2: case 3: case 4: case 5: case 6:
                    return GenDate.TicksPerHour * spoilTime;
                // 8/10/12 h
                case 7: case 8: case 9:
                    return GenDate.TicksPerHour * (6 + (spoilTime - 6) * 2);
                // 16/20/24 h
                case 10: case 11: case 12:
                    return GenDate.TicksPerHour * (12 + (spoilTime - 9) * 4);
                // 2-6 d
                case 13: case 14: case 15: case 16: case 17:
                    return GenDate.TicksPerDay * (spoilTime - 11);
                // 8/10/12 d
                case 18: case 19: case 20:
                    return GenDate.TicksPerDay * (6 + (spoilTime - 17) * 2);
                // 15/30/45/60 d
                case 21: case 22: case 23: case 24:
                    return GenDate.TicksPerQuadrum * (spoilTime - 20);
                // 2-6 y
                case 25: case 26: case 27: case 28: case 29:
                    return GenDate.TicksPerYear * (spoilTime - 23);
                // infinite
                case 30: default:
                    return -1;
            }
        }

        public static IntRange convertToTicks(IntRange spoilTimes)
        {
            return new IntRange(convertToTicks(spoilTimes.min), convertToTicks(spoilTimes.max));
        }

        public static string spoilTimeTicksToString(int spoilTime)
        {
            if (spoilTime == -1)
                return "SpoilTime_Forever".Translate();
            return spoilTime.ToStringTicksToPeriod(false, false, false, true);
        }

        public static string spoilTimeTicksToString(IntRange spoilTicks)
        {
            return "SpoilTime_MinMax_Label".Translate(spoilTimeTicksToString(spoilTicks.min), spoilTimeTicksToString(spoilTicks.max));
        }
    }

    class WrappedIntRange
    {
        public IntRange value;

        public WrappedIntRange(int start, int stop)
        {
            value = new IntRange(start, stop);
        }
    }

    class MyWorldComponent : WorldComponent
    {
        private Dictionary<ThingFilter, WrappedIntRange> filterSpoilTimes = new Dictionary<ThingFilter, WrappedIntRange>();
        private Dictionary<ThingCategoryDef, bool> cacheShowSpoilTime = new Dictionary<ThingCategoryDef, bool>();

        public MyWorldComponent(World world)
            : base(world)
        {
        }

        public ref IntRange getSpoilTime(ThingFilter filter)
        {
            if (!filterSpoilTimes.ContainsKey(filter))
                filterSpoilTimes[filter] = new WrappedIntRange(0, 30);
            WrappedIntRange w = filterSpoilTimes[filter];
            return ref w.value;
        }

        public bool ShowSpoilTime(ThingCategoryDef catDef)
        {
            if (!cacheShowSpoilTime.ContainsKey(catDef))
                cacheShowSpoilTime[catDef] = CalculateShowSpoilTime(catDef);
            return cacheShowSpoilTime[catDef];
        }

        public bool ShowSpoilTime(ThingFilter filter)
        {
            return ShowSpoilTime(filter.DisplayRootCategory.catDef);
        }

        private static bool CalculateShowSpoilTime(ThingCategoryDef catDef)
        {
            foreach (ThingDef descendantThingDef in catDef.DescendantThingDefs)
                if (descendantThingDef.HasComp(typeof(CompRottable)))
                    return true;
            return false;
        }
    }

    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {
        static string MagicTranslatePrefix = "{MrHacky.TranslatedAlready}";

        // this static constructor runs to create a HarmonyInstance and install a patch.
        static HarmonyPatches()
        {
            Log.Message("[PerishableSpoilTimeFilter] Initializing.");

            Harmony harmony = new Harmony("MrHacky.PerishableSpoilTimeFilter");

            harmony.Patch(
                AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.Allows), new Type[] { typeof(Thing) }),
                postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(HarmonyPatches.IsAllowed_Postfix)))
            );

            harmony.Patch(
                AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.CopyAllowancesFrom)),
                postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(HarmonyPatches.CopyAllowancesFrom_Postfix)))
            );

            harmony.Patch(
                AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.ExposeData)),
                postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(HarmonyPatches.ExposeData_Postfix)))
            );

            harmony.Patch(
                AccessTools.Method(typeof(ThingFilterUI), "DrawHitPointsFilterConfig"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(HarmonyPatches.DrawSpoilTimeFilterConfig)))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Verse.Translator), nameof(Verse.Translator.Translate), new Type[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(HarmonyPatches.Translate_Prefix)))
            );
        }

        public static bool Translate_Prefix(string key, ref TaggedString __result)
        {
            if (key.StartsWith(MagicTranslatePrefix))
            {
                __result = key.Substring(MagicTranslatePrefix.Length);
                return false;
            }
            return true;
        }
        public static void IsAllowed_Postfix(ThingFilter __instance, ref bool __result, Thing t)
        {
            MyWorldComponent world = Find.World.GetComponent<MyWorldComponent>();
            CompRottable rot = t.TryGetComp<CompRottable>();
            if (rot != null && world.ShowSpoilTime(__instance))
            {
                IntRange spoilTickRange = SpoilTimeCalc.convertToTicks(world.getSpoilTime(__instance));
                int spoilTicks = rot.TicksUntilRotAtTemp(25.0f);

                if (spoilTickRange.min == -1 || spoilTicks < spoilTickRange.min)
                    __result = false;
                else if (spoilTickRange.max != -1 && spoilTicks > spoilTickRange.max)
                    __result = false;
            }
        }

        public static void DrawSpoilTimeFilterConfig(ref float y, float width, ThingFilter filter)
        {
            MyWorldComponent world = Find.World.GetComponent<MyWorldComponent>();
            if (!world.ShowSpoilTime(filter))
                return;
            Rect rect = new Rect(20f, y, width - 20f, 28f);
            ref IntRange local = ref world.getSpoilTime(filter);
            IntRange spoilTicks = SpoilTimeCalc.convertToTicks(local);
            Widgets.IntRange(rect, 1884285639, ref local, 0, 30,
                labelKey: MagicTranslatePrefix + SpoilTimeCalc.spoilTimeTicksToString(spoilTicks)
            );
            y += 28f;
            y += 5f;
            Text.Font = GameFont.Small;
        }

        public static void ExposeData_Postfix(ThingFilter __instance)
        {
            // Not sure how, but early in loading we get called when Find.World does not exists?
            if (Find.World == null)
                return;

            MyWorldComponent world = Find.World.GetComponent<MyWorldComponent>();

            if (!world.ShowSpoilTime(__instance))
                return;

            ref IntRange local = ref world.getSpoilTime(__instance);
            Scribe_Values.Look<IntRange>(ref local, "MrHacky.allowedPerishableSpoilTimes", new IntRange(0, 30), false);
        }

        public static void CopyAllowancesFrom_Postfix(ThingFilter __instance, ThingFilter other)
        {
            MyWorldComponent world = Find.World.GetComponent<MyWorldComponent>();

            if (!world.ShowSpoilTime(__instance) || !world.ShowSpoilTime(other))
                return;

            ref IntRange local = ref world.getSpoilTime(__instance);
            local = world.getSpoilTime(other);
        }
    }
}