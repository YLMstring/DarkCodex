﻿using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;
using HarmonyLib;
using Guid = DarkCodex.GuidManager;
using DarkCodex.Components;
using Kingmaker.Modding;
using Kingmaker.Achievements;
using Kingmaker;
using Config;
using System.IO;
using Newtonsoft.Json;
using Kingmaker.PubSubSystem;
using System.Reflection;
using System.Linq.Expressions;
using Kingmaker.UI.Common;
using Kingmaker.UI;
using Kingmaker.EntitySystem.Stats;
using System.Runtime.CompilerServices;

namespace DarkCodex
{
    public class Main
    {
        public static Harmony harmony;
        public static bool IsInGame { get { return Game.Instance.Player?.Party?.Any() ?? false; } }

        /// <summary>True if mod is enabled. Doesn't do anything right now.</summary>
        public static bool Enabled { get; set; } = true;
        /// <summary>Path of current mod.</summary>
        public static string ModPath;

        internal static UnityModManager.ModEntry.ModLogger logger;

        #region GUI

        /// <summary>Called when the mod is turned to on/off.
        /// With this function you control an operation of the mod and inform users whether it is enabled or not.</summary>
        /// <param name="value">true = mod to be turned on; false = mod to be turned off</param>
        /// <returns>Returns true, if state can be changed.</returns>
        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Main.Enabled = value;
            return true;
        }

        internal static List<PatchInfoAttribute> patchInfos = new List<PatchInfoAttribute>();

        private static bool restart;
        private static GUIStyle StyleBox;
        private static GUIStyle StyleLine;
        /// <summary>Draws the GUI</summary>
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings state = Settings.StateManager.State;

            if (StyleBox == null)
            {
                StyleBox = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
                StyleLine = new GUIStyle() { fixedHeight = 1, margin = new RectOffset(0, 0, 4, 4), };
                StyleLine.normal.background = new Texture2D(1, 1);
            }

            GUILayout.Label("Disclaimer: Remember that playing with mods often makes them mandatory for your save game!");
            GUILayout.Label("Legend: [F] This adds a feat. You still need to pick feats/talents for these effects. If you already picked these features, then they stay in effect regardless of the option above."
                + "\n[*] Option is enabled/disabled immediately, without restart.");

            if (Patch_AllowAchievements.Patched)
                Checkbox(ref state.allowAchievements, "[*] Allow achievements - enables achievements while mods are active and also set corresponding flag to future save files");
            else
                GUILayout.Label("Allow achievements - managed by other mod");

            Checkbox(ref state.PsychokineticistStat, "Psychokineticist Main Stat");

            //NumberField(nameof(Settings.magicItemBaseCost), "Cost of magic items (default: 1000)");
            //NumberFieldFast(ref _debug1, "Target Frame Rate");

            GUILayout.Space(10);
            GUILayout.Label("Advanced: Patch Control");
            GUILayout.Label("Options marked with <color=red><b>✖</b></color> will not be loaded. You can use this to disable certain patches you don't like or that cause you issues ingame."
                    + " Options marked with <color=yellow><b>!</b></color> are missing patches to work properly. Check the \"Patch\" section."
                    + "\n<color=yellow>Warning: All option require a restart. Disabling options may cause your current saves to be stuck at loading, until re-enabled.</color>");
            if (GUILayout.Button("Disable all homebrew"))
                patchInfos.Where(w => w.Homebrew).Select(s => s.Class + "." + s.Method).ForEach(name => state.SetEnable(false, name));
            GUILayout.Space(10);
            Checkbox(ref state.newFeatureDefaultOn, "New features default on", b => restart = true);

            string category = null;
            bool disableAll = false;

            foreach (var info in patchInfos)
            {
                if (info.Class != category)
                {
                    category = info.Class;
                    disableAll = state.IsDisenabledCategory(category + ".*");
                }
                info.DisabledAll = disableAll;
                info.Disabled = state.IsDisenabledSingle(info.Class + "." + info.Method);
            }

            category = null;
            foreach (var info in patchInfos)
            {
#if !DEBUG
                if (info.IsHidden) continue;
#endif
                if (info.Class != category)
                {
                    GUILayout.Box(GUIContent.none, StyleLine);
                    GUILayout.Label(info.Class);
                    category = info.Class;
                    disableAll = info.DisabledAll;

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(!disableAll ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>", StyleBox, GUILayout.Width(20)))
                    {
                        restart = true;
                        state.SetEnable(disableAll, category + ".*");
                    }
                    GUILayout.Space(5);
                    GUILayout.Label("Disable all", GUILayout.ExpandWidth(false));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(5);
                if (DrawInfoButton(info))
                {
                    string str = info.Class + "." + info.Method;
                    restart = true;
                    state.SetEnable(info.Disabled, str);
                }
                GUILayout.Space(5);
                GUILayout.Label(info.Name.Grey(info.IsHidden).Red(info.IsDangerous), GUILayout.Width(300));
                GUILayout.Label(info.Description, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("Debug");

            if (GUILayout.Button("Debug: Export Player Data", GUILayout.ExpandWidth(false)))
                ExportPlayerData();
            //if (GUILayout.Button("Debug: Date minus 1", GUILayout.ExpandWidth(false)))
            //    DEBUG.Date.SetDate();
            //if (GUILayout.Button("Debug: Open Shared Stash", GUILayout.ExpandWidth(false)))
            //    DEBUG.Loot.Open();
            //if (GUILayout.Button("Debug: Export Icons", GUILayout.ExpandWidth(false)))
            //    DEBUG.ExportAllIconTextures();
            if (GUILayout.Button("Debug: Pause Area Fxs", GUILayout.ExpandWidth(false)))
                Control_AreaEffects.Stop();
            if (GUILayout.Button("Debug: Continue Area Fxs", GUILayout.ExpandWidth(false)))
                Control_AreaEffects.Continue();
            if (GUILayout.Button("Debug: Print Content Table", GUILayout.ExpandWidth(false)))
            {
                using var sw = new StreamWriter(Path.Combine(Main.ModPath, "content.md"), false);
                sw.WriteLine("Content");
                sw.WriteLine("-----------");
                sw.WriteLine("| Option | Description | HB | Status |");
                sw.WriteLine("|--------|-------------|----|--------|");

                foreach (var info in patchInfos)
                    if (!info.IsHarmony && !info.IsEvent && !info.IsHidden)
                        sw.WriteLine($"|{info.Class}.{info.Method}|{info.Description.Replace('\n', ' ')}|{info.HomebrewStr}|{info.StatusStr}|");
            }
            Checkbox(ref state.polymorphKeepInventory, "Debug: Enable polymorph equipment (restart to disable)");
            Checkbox(ref state.polymorphKeepModel, "Debug: Disable polymorph transformation [*]");
            Checkbox(ref state.debug_1, "Debug: Flag1");
            Checkbox(ref state.debug_2, "Debug: Flag2");
            Checkbox(ref state.debug_3, "Debug: Flag3");
            Checkbox(ref state.debug_4, "Debug: Flag4");

            GUILayout.Label("");

            if (GUILayout.Button("Save settings!"))
                OnSaveGUI(modEntry);
        }

        private static void OnHideGUI(UnityModManager.ModEntry modEntry)
        {
            if (restart)
            {
                restart = false;
                UIUtility.ShowMessageBox("Warning! Patch selection changed. Restart game before saving. If you cannot load your save, re-enable patches.", MessageModalBase.ModalType.Message, a => { }, null, 0, null, null, null);
            }
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            foreach (var entry in NumberTable)
            {
                try
                {
                    var field = typeof(Settings).GetField(entry.Key);
                    if (field.FieldType == typeof(int))
                    {
                        if (int.TryParse(entry.Value, out int num))
                            field.SetValue(Settings.StateManager.State, num);
                    }
                    else if (field.FieldType == typeof(float))
                    {
                        if (float.TryParse(entry.Value, out float num))
                            field.SetValue(Settings.StateManager.State, num);
                    }
                }
                catch (Exception)
                {
                    Helper.Print($"Error while parsing number '{entry.Value}' for '{entry.Key}'");
                }
            }

            Settings.StateManager.TrySaveConfigurationState();
        }

        private static void Checkbox(ref bool value, string label, Action<bool> action = null)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(value ? "<color=green><b>✔</b></color>" : "<color=red><b>✖</b></color>", StyleBox, GUILayout.Width(20)))
            {
                value = !value;
                action?.Invoke(value);
            }
            GUILayout.Space(5);
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        private static string lastEnum;
        private static void Checkbox<T>(ref T value, string label, Action<T> action = null) where T : Enum
        {
            if (GUILayout.Button(label))
            {
                if (lastEnum == label)
                    lastEnum = null;
                else
                    lastEnum = label;
            }

            if (lastEnum != label)
                return;

            string name = value.ToString();
            var names = Enum.GetNames(typeof(T));
            int index = names.IndexOf(name);
            int newindex = GUILayout.SelectionGrid(index, names, 10); //GUILayout.SelectionGrid(index, names, names.Length)

            if (index == newindex)
                return;

            value = (T)Enum.Parse(typeof(T), names[newindex]);
            action?.Invoke(value);
        }

        private static void NumberFieldFast(ref float value, string label)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(label + ": ", GUILayout.ExpandWidth(false));
            if (float.TryParse(GUILayout.TextField(value.ToString(), GUILayout.Width(230f)), out float newvalue))
                value = newvalue;

            GUILayout.EndHorizontal();
        }

        private static Dictionary<string, string> NumberTable = new Dictionary<string, string>();
        private static void NumberField(string key, string label)
        {
            NumberTable.TryGetValue(key, out string str);
            if (str == null) try { str = typeof(Settings).GetField(key).GetValue(Settings.StateManager.State).ToString(); } catch (Exception) { }
            if (str == null) str = "couldn't read";

            GUILayout.BeginHorizontal();

            GUILayout.Label(label + ": ", GUILayout.ExpandWidth(false));
            NumberTable[key] = GUILayout.TextField(str, GUILayout.Width(230f));

            GUILayout.EndHorizontal();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool DrawInfoButton(PatchInfoAttribute info)
        {
            GUIContent text;
            if (info.DisabledAll)
            {
                if (info.Disabled)
                    text = new("<color=grey><b>✖</b></color>", "Disabled by category.");
                else
                    text = new("<color=grey><b>✔</b></color>", "Disabled by category.");
            }
            else if (info.Disabled)
                text = new("<color=red><b>✖</b></color>", "");
            else if (info.Requirement?.Name is string req
                    && patchInfos.FirstOrDefault(f => f.Method == req) is PatchInfoAttribute other
                    && (other.Disabled || other.DisabledAll))
                text = new("<color=yellow><b>!</b></color>", "This patch won't work without " + req);
            else
                text = new("<color=green><b>✔</b></color>", "");

            return GUILayout.Button(text, StyleBox, GUILayout.Width(20));
        }

        private static void DrawToolip()
        {
            Vector3 x = Input.mousePosition;

            Rect r = new(x.x - 10, x.y, 150, 20);

            GUI.Box(r, "This patch won't work without {0}");
        }

        #endregion

        #region Load

        /// <summary>Loads on game start.</summary>
        /// <param name="modEntry.Info">Contains all fields from the 'Info.json' file.</param>
        /// <param name="modEntry.Path">The path to the mod folder e.g. '\Steam\steamapps\common\YourGame\Mods\TestMod\'.</param>
        /// <param name="modEntry.Active">Active or inactive.</param>
        /// <param name="modEntry.Logger">Writes logs to the 'Log.txt' file.</param>
        /// <param name="modEntry.OnToggle">The presence of this function will let the mod manager know that the mod can be safely disabled during the game.</param>
        /// <param name="modEntry.OnGUI">Called to draw UI.</param>
        /// <param name="modEntry.OnSaveGUI">Called while saving.</param>
        /// <param name="modEntry.OnUpdate">Called by MonoBehaviour.Update.</param>
        /// <param name="modEntry.OnLateUpdate">Called by MonoBehaviour.LateUpdate.</param>
        /// <param name="modEntry.OnFixedUpdate">Called by MonoBehaviour.FixedUpdate.</param>
        /// <returns>Returns true, if no error occurred.</returns>
        internal static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModPath = modEntry.Path;
            logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnHideGUI = OnHideGUI;

            try
            {
                harmony = new Harmony(modEntry.Info.Id);
                Helper.Patch(typeof(StartGameLoader_LoadAllJson));
                Helper.Patch(typeof(MainMenu_Start));
                //harmony.PatchAll(typeof(Main).Assembly);
                //harmony.Patch(HarmonyLib.AccessTools.Method(typeof(EnumUtils), nameof(EnumUtils.GetMaxValue), null, new Type[] { typeof(ActivatableAbilityGroup) }),
                //    postfix: new HarmonyMethod(typeof(Patch_ActivatableAbilityGroup).GetMethod("Postfix")));
            }
            catch (Exception ex)
            {
                Helper.PrintException(ex);
            }


            return true;
        }

        #endregion

        #region Load_Patch

        [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
        public static class MainMenu_Start
        {
            public static void Postfix()
            {
                if (Settings.StateManager.State.showBootupWarning)
                {
                    UIUtility.ShowMessageBox("Installed Dark Codex.\nIf you want to disable certain features, do it now. Disabling 'red' features during a playthrough is not possible. Enabling features can be done at any time.", MessageModalBase.ModalType.Message, a => { }, null, 0, null, null, null);
                    Settings.StateManager.State.showBootupWarning = false;
                    Settings.StateManager.TrySaveConfigurationState();
                }
            }
        }

        [HarmonyPatch(typeof(StartGameLoader), "LoadAllJson")]
        public static class StartGameLoader_LoadAllJson
        {
            private static bool Run = false;
            public static void Postfix()
            {
                if (Run) return; Run = true;

                try
                {
                    Helper.Print("Loading Dark Codex");

                    // Debug
                    LoadSafe(DEBUG.Enchantments.NameAll);
                    PatchSafe(typeof(DEBUG.Enchantments));
#if DEBUG
                    PatchSafe(typeof(DEBUG.Settlement1));
                    PatchSafe(typeof(DEBUG.Settlement2));
                    PatchSafe(typeof(DEBUG.ArmyLeader1));
                    PatchSafe(typeof(DEBUG.SpellReach));
                    LoadSafe(Kineticist.xCombineBlade);
                    PatchSafe(typeof(Kineticist.Patch_ActivatableGroups));
#endif
                    PatchSafe(typeof(Patch_FixLoadCrash1));
                    LoadSafe(General.createBardStopSong);
                    //PatchSafe(typeof(General.DEBUGTEST));

                    // Cache
                    LoadSafe(PropertyMaxAttribute.createPropertyMaxMentalAttribute);
                    LoadSafe(PropertyGetterSneakAttack.createPropertyGetterSneakAttack);
                    LoadSafe(PropertyMythicLevel.createMythicDispelProperty);
                    LoadSafe(ContextActionIncreaseBleed.createBleedBuff);

                    // Harmony Patches
                    PatchUnique(typeof(Patch_AllowAchievements));
                    PatchUnique(typeof(Patch_DebugReport));
                    PatchSafe(typeof(Patch_FixPolymorphGather));
                    PatchSafe(typeof(Patch_TrueGatherPowerLevel));
                    PatchSafe(typeof(Patch_KineticistAllowOpportunityAttack));
                    PatchSafe(typeof(Patch_EnvelopingWindsCap));
                    PatchSafe(typeof(Patch_MagicItemAdept));
                    PatchSafe(typeof(Patch_ActivatableOnNewRound));
                    PatchSafe(typeof(Patch_ActivatableHandleUnitRunCommand));
                    PatchSafe(typeof(Patch_ActivatableOnTurnOn));
                    PatchSafe(typeof(Patch_ActivatableApplyValidation));
                    PatchSafe(typeof(Patch_ActivatableTryStart));
                    PatchSafe(typeof(Patch_ActivatableActionBar));
                    PatchSafe(typeof(Patch_ResourcefulCaster));
                    PatchSafe(typeof(Patch_FeralCombat));
                    PatchSafe(typeof(Patch_SpellSelectionParametrized));
                    PatchSafe(typeof(Patch_PreferredSpellMetamagic));
                    PatchSafe(typeof(Patch_AlwaysAChance));
                    PatchSafe(typeof(Patch_FixAreaDoubleDamage));
                    PatchSafe(typeof(Patch_FixAreaEndOfTurn));
                    PatchSafe(typeof(Patch_Polymorph));

                    // General
                    LoadSafe(General.patchAngelsLight);
                    LoadSafe(General.patchBasicFreebieFeats);
                    LoadSafe(General.patchHideBuffs);
                    LoadSafe(General.patchDispelMagic);
                    LoadSafe(General.patchVarious);

                    // Items
                    LoadSafe(Items.patchArrows);
                    LoadSafe(Items.patchTerendelevScale);
                    LoadSafe(Items.createKineticArtifact);
                    LoadSafe(Items.createButcheringAxe);
                    LoadSafe(Items.createImpactEnchantment);

                    // Mythic
                    LoadSafe(Mythic.createLimitlessBardicPerformance);
                    LoadSafe(Mythic.createLimitlessSmite);
                    LoadSafe(Mythic.createLimitlessBombs);
                    LoadSafe(Mythic.createLimitlessArcanePool);
                    LoadSafe(Mythic.createLimitlessArcaneReservoir);
                    LoadSafe(Mythic.createLimitlessKi);
                    LoadSafe(Mythic.createLimitlessDomain);
                    LoadSafe(Mythic.createLimitlessShaman);
                    LoadSafe(Mythic.createLimitlessWarpriest);
                    LoadSafe(Mythic.createKineticMastery);
                    LoadSafe(Mythic.createMagicItemAdept);
                    LoadSafe(Mythic.createResourcefulCaster);
                    LoadSafe(Mythic.createSwiftHuntersBond);
                    LoadSafe(Mythic.patchKineticOvercharge);
                    LoadSafe(Mythic.patchLimitlessDemonRage);
                    LoadSafe(Mythic.patchUnstoppable);
                    LoadSafe(Mythic.patchBoundlessHealing);
                    LoadSafe(Mythic.patchRangingShots);
                    LoadSafe(Mythic.patchWanderingHex);
                    LoadSafe(Mythic.patchJudgementAura);
                    LoadSafe(Mythic.patchVarious);

                    // Kineticist
                    LoadSafe(Kineticist.fixWallInfusion);
                    LoadSafe(Kineticist.createKineticistBackground);
                    LoadSafe(Kineticist.createMobileGatheringFeat);
                    LoadSafe(Kineticist.createImpaleInfusion);
                    LoadSafe(Kineticist.createWhipInfusion);
                    LoadSafe(Kineticist.createBladeRushInfusion);
                    LoadSafe(Kineticist.createAutoMetakinesis);
                    LoadSafe(Kineticist.createHurricaneQueen);
                    LoadSafe(Kineticist.createMindShield);
                    LoadSafe(Kineticist.patchGatherPower);
                    LoadSafe(Kineticist.patchDarkElementalist);
                    LoadSafe(Kineticist.patchDemonCharge); // after createMobileGatheringFeat
                    LoadSafe(Kineticist.patchVarious);
                    LoadSafe(Kineticist.fixBlastsAreSpellLike);
                    LoadSafe(Kineticist.createSelectiveMetakinesis); // keep late

                    // Monk
                    LoadSafe(Monk.createFeralCombatTraining);

                    // Witch
                    LoadSafe(Witch.createIceTomb);
                    LoadSafe(Witch.fixBoundlessHealing);

                    // Hexcrafter
                    LoadSafe(Hexcrafter.fixProgression);

                    // Rogue
                    LoadSafe(Rogue.createBleedingAttack);

                    // Ranger
                    LoadSafe(Ranger.createImprovedHuntersBond);

                    // Extra Features - keep last
                    LoadSafe(Mythic.createLimitlessWitchHexes); // keep last
                    LoadSafe(General.createPreferredSpell); // keep last
                    LoadSafe(General.createAbilityFocus); // keep last
                    LoadSafe(Kineticist.createExtraWildTalentFeat); // keep last
                    LoadSafe(Witch.createExtraHex); // keep last
                    LoadSafe(Witch.createCackleActivatable); // keep last
                    LoadSafe(Rogue.createExtraRogueTalent); // keep last
                    LoadSafe(Mythic.createExtraMythicFeats); // keep last
                    LoadSafe(Mythic.createSwiftHex); // keep last

                    // Event subscriptions
                    SubscribeSafe(typeof(RestoreEndOfCombat));
                    SubscribeSafe(typeof(Control_AreaEffects));

                    patchInfos.Sort(); // sort info list for GUI

                    Helper.Print("Finished loading Dark Codex");
#if DEBUG
                    Helper.PrintDebug("Running in debug.");
                    Helper.ExportStrings();
                    Guid.i.WriteAll();
#endif
                }
                catch (Exception ex)
                {
                    Helper.PrintException(ex);
                }
            }
        }

        #endregion

        #region Helper

        public static bool LoadSafe(Action action)
        {
            ProcessInfo(action.Method);
#if DEBUG
            var watch = Stopwatch.StartNew();
#endif
            string name = action.Method.DeclaringType.Name + "." + action.Method.Name;

            if (CheckSetting(name))
            {
                Helper.Print($"Skipped loading {name}");
                return false;
            }

            try
            {
                Helper.Print($"Loading {name}");
                action();
#if DEBUG
                watch.Stop();
                Helper.PrintDebug("Loaded in milliseconds: " + watch.ElapsedMilliseconds);
#endif
                return true;
            }
            catch (Exception e)
            {
#if DEBUG
                watch.Stop();
#endif
                Helper.PrintException(e);
                return false;
            }
        }

        public static bool LoadSafe(Action<bool> action, bool flag)
        {
            ProcessInfo(action.Method);
#if DEBUG
            var watch = Stopwatch.StartNew();
#endif
            string name = action.Method.DeclaringType.Name + "." + action.Method.Name;

            if (CheckSetting(name))
            {
                Helper.Print($"Skipped loading {name}");
                return false;
            }

            try
            {
                Helper.Print($"Loading {name}:{flag}");
                action(flag);
#if DEBUG
                watch.Stop();
                Helper.PrintDebug("Loaded in milliseconds: " + watch.ElapsedMilliseconds);
#endif
                return true;
            }
            catch (Exception e)
            {
#if DEBUG
                watch.Stop();
#endif
                Helper.PrintException(e);
                return false;
            }
        }

        public static void PatchUnique(Type patch)
        {
            ProcessInfo(patch);
            if (Helper.IsPatched(patch))
            {
                Helper.Print("Skipped patching because not unique " + patch.Name);
                return;
            }

            if (PatchSafe(patch))
                patch.GetField("Patched", BindingFlags.Static | BindingFlags.Public)?.SetValue(null, true);
        }

        public static bool PatchSafe(Type patch)
        {
            ProcessInfo(patch);
            if (CheckSetting("Patch." + patch.Name))
            {
                Helper.Print("Skipped patching " + patch.Name);
                return false;
            }

            try
            {
                if (patch.GetCustomAttributes(false).Any(a => a is ManualPatchAttribute))
                    Helper.Patch(patch, default);
                else
                    Helper.Patch(patch);
                return true;
            }
            catch (Exception e)
            {
                Helper.PrintException(e);
                return false;
            }
        }

        public static void SubscribeSafe(Type type)
        {
            ProcessInfo(type);
            if (CheckSetting("Patch." + type.Name))
            {
                Helper.Print("Skipped subscribing to " + type.Name);
                return;
            }

            try
            {
                Helper.Print("Subscribing to " + type.Name);
                EventBus.Subscribe(Activator.CreateInstance(type));
            }
            catch (Exception e)
            {
                Helper.PrintException(e);
            }
        }

        private static bool CheckSetting(string name)
        {
            return Settings.StateManager.State.IsDisenabled(name);
        }

        private static void ProcessInfo(MemberInfo info)
        {
            if (info == null)
                return;

            var attr = Attribute.GetCustomAttribute(info, typeof(PatchInfoAttribute)) as PatchInfoAttribute;
            if (attr == null)
            {
                Helper.PrintDebug(info.Name + " has no PatchInfo");
                return;
            }

            attr.Class = info.DeclaringType?.Name ?? "Patch";
            attr.Method = info.Name;

            if (!patchInfos.Contains(attr))
                patchInfos.Add(attr);
        }

        private static void ExportPlayerData()
        {
            try
            {
                JsonSerializer serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.All,
                });

                using (StreamWriter sw = new StreamWriter(Path.Combine(ModPath, "player.json")))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, Game.Instance.Player.Party);
                }

                Helper.Print("Exported player data.");
            }
            catch (Exception e)
            {
                Helper.PrintException(e);
            }
        }

        #endregion

    }
}
