using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Utilities;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KK_MobReplacer {
    [BepInProcess("Koikatu")]
    [BepInProcess("Koikatsu Party")]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInPlugin(GUID, PluginName, Version)]
    public class KK_MobReplacer : BaseUnityPlugin {

        public const string PluginName = "Generic Mob Replacer";
        public const string GUID = "mobReplacer";
        public const string Version = "1.0.0";

        public const string FileExtension = ".png";
        public const string Filter = "Character cards (*.png)|*.png|All files|*.*";

        internal static new ManualLogSource Logger;
#pragma warning disable 169
        private ConfigEntry<string> op_mob_a;
        private ConfigEntry<string> op_mob_b;
        private ConfigEntry<string> op_mob_c;
        private ConfigEntry<string> idol_class_a;
        private ConfigEntry<string> idol_class_b;
        private ConfigEntry<string> idol_class_c;
        private ConfigEntry<string> idol_class_d;
        private ConfigEntry<string> idol_mob_a;
        private ConfigEntry<string> idol_mob_b;
        private ConfigEntry<string> idol_normal_a;
        private ConfigEntry<string> idol_normal_b;
        private ConfigEntry<string> idol_normal_c;
        private ConfigEntry<string> suiei_a;
        private ConfigEntry<string> suiei_b;
        private ConfigEntry<string> suiei_c;
        private ConfigEntry<string> suiei_d;
        private ConfigEntry<string> manken_new_a;
        private ConfigEntry<string> manken_new_b;
        private ConfigEntry<string> manken_normal_a;
        private ConfigEntry<string> manken_normal_b;
        private ConfigEntry<string> manken_normal_c;
        private ConfigEntry<string> manken_normal_d;
        private ConfigEntry<string> manken_normal;
        private ConfigEntry<string> tia_a;
        private ConfigEntry<string> tia_b;
        private ConfigEntry<string> tia_c;
#pragma warning restore 0169

        private List<FieldInfo> fields;
        internal static Dictionary<string, string> replacementMap = new Dictionary<string, string>();


        private void Awake() {
            Logger = base.Logger;

            Config.SettingChanged += settingChanged;

            fields = new List<FieldInfo>();
            fields.AddRange(((IEnumerable<FieldInfo>)AccessTools.GetDeclaredFields(typeof(KK_MobReplacer))).Where(x => x.FieldType == typeof(ConfigEntry<string>)));

            Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
            
            SetUpButtons();
        }

        private void settingChanged(object sender, EventArgs e) {
            IterateFields();            
        }

        void Start() {
            IterateFields();
        }

        private void IterateFields() {// SHOOT ME NOW
            foreach (FieldInfo field in fields) {
                var cE = (ConfigEntry<string>)(field.GetValue(this));
                if (!cE.Value.IsNullOrWhiteSpace()) {
                    if (!replacementMap.ContainsKey(field.Name)) {
                        replacementMap.Add(field.Name, cE.Value);
                    }
                } else {
                    if (replacementMap.ContainsKey(field.Name)) {
                        replacementMap.Remove(field.Name);
                    }
                }
            }
        }

        private void SetUpButtons() {
            int counter = 25;
            foreach(FieldInfo field in fields) {
                Config.Bind("Config", $"{field.Name} Card Replacement", "", new ConfigDescription("Browse for a card.", null, new ConfigurationManagerAttributes { Order = counter--, HideDefaultButton = true, CustomDrawer = new Action<ConfigEntryBase>(CardButtonDrawer) }));
                field.SetValue(this, Config.Bind("Config", $"{field.Name} Replacement Card Path", "", new ConfigDescription("Path of the replacement card on disk.", null, new ConfigurationManagerAttributes { Order = counter-- })));
            }
        }

        private void CardButtonDrawer(ConfigEntryBase configEntry) {
            var name = configEntry.Definition.Key.Split(null)[0];
            if (GUILayout.Button($"Browse for {name} Replacement", GUILayout.ExpandWidth(true))) {
                GetCard(name);
            }
        }
        private void GetCard(string key) => OpenFileDialog.Show(path => OnCardAccept(key, path), "Select replacement card", GetDir(), Filter, FileExtension, OpenFileDialog.OpenSaveFileDialgueFlags.OFN_FILEMUSTEXIST);
        private string GetDir() => Path.Combine(Paths.GameRootPath, @"userdata\chara");

        private void OnCardAccept(string key, string[] path) {           
            if (path.IsNullOrEmpty()) return;
            var field = fields.FirstOrDefault(x => x.Name == key);
            if (field == null) return;
            var conField = (ConfigEntry<string>)field.GetValue(this);
            conField.Value = path[0];
        }

        private static bool ValidateCard(string path) {
            if (!File.Exists(path)) return false;
            using (var fS = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                using (var bR = new BinaryReader(fS)) {
                    try {
                        PngFile.SkipPng(bR);

                        string tag = bR.ReadString();
                        tag = tag.Remove(0, tag.IndexOf("【") + 1);
                        tag = tag.Remove(tag.IndexOf("】"));
                        switch (tag) {
                            case "KoiKatuChara":
                            case "KoiKatuCharaS":
                            case "KoiKatuCharaSP":
                                return true;
                            default:
                                return false;
                        }
                    } catch (Exception) {
                        return false;
                    }
                }
            }
        }
        private static class Hooks {
            [HarmonyILManipulator, HarmonyPatch(typeof(ADV.Commands.Chara.MobCreate), nameof(ADV.Commands.Chara.MobCreate.Do))]
            public static void MobCreateTranspiler(ILContext ctx) {

                var c = new ILCursor(ctx);

                c.GotoNext(MoveType.After, x => x.MatchStloc(5)); //the SaveData.Heroine constructor has been called and the result put into the local variable

                c.Emit(OpCodes.Ldloc, 2); //load the int "no" onto the stack (probably the character index in the scene?)
                c.Emit(OpCodes.Ldloc, 4); //load the assetname of the character onto the stack i.e. "op_mob_a"
                c.Emit(OpCodes.Ldloc, 5); //load the SaveData.Heroine instance that will hold the character onto the stack

                c.EmitDelegate<Func<int, string, SaveData.Heroine, bool>>((no, assetname, heroine) => LoadDelegate(no, assetname, heroine)); //call our delegate, consuming the 3 parameters (which we just pushed onto the stack) and pushing the result bool onto the stack       

                var lab = c.DefineLabel(); //define our target label to jump to
                c.Emit(OpCodes.Brtrue, lab); // our bool is on the stack. if our bool is true, jump to our target label (the label isn't set yet)
                c.GotoNext(MoveType.After, x => x.MatchCall(AccessTools.Method(typeof(AssetBundleManager), "UnloadAssetBundle", new Type[] { typeof(string), typeof(bool), typeof(string), typeof(bool) }))); //the location the label points to - after the call to unloadassetbundle
                c.MarkLabel(lab); //assign the label to the place we want as our jump destination              
            }


            private static bool LoadDelegate(int no, string assetName, SaveData.Heroine heroine) {
                if (!replacementMap.TryGetValue(assetName, out string path)) return false;
                if (path.IsNullOrWhiteSpace()) return false;
                if (!ValidateCard(path)) return false;
                if (!heroine.charFileInitialized) heroine.charFileInitialized = true;                
                heroine.charFile.LoadFile(path, true); 
                heroine.ChaFileUpdate();
                heroine.fixCharaID = no;
                return true;
            }       
        }
    } 
}
internal sealed class ConfigurationManagerAttributes {
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
    public bool? HideDefaultButton;
    public int? Order;
}
