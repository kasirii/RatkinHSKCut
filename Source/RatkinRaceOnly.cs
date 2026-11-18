using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RatkinRaceOnly
{
    public sealed class RaceOnlyMod : Mod
    {
        public RaceOnlyMod(ModContentPack content) : base(content)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (name == "NewRatkin" || name == "SYS") ContentPatches.Sources.Add(assembly);
            }

            ContentPatches.LoadSettings(Path.Combine(content.RootDir, "Runtime.xml"));
            var harmony = new Harmony("local.ratkin.raceonly.runtime");
            ContentPatches.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(DefOfHelper), "BindDefsFor"),
                prefix: new HarmonyMethod(typeof(DefOfPatch), nameof(DefOfPatch.Prefix)));
        }
    }

    public static class ContentPatches
    {
        internal static readonly HashSet<Assembly> Sources = new HashSet<Assembly>();
        private static readonly HashSet<string> blocked = new HashSet<string>(StringComparer.Ordinal);

        internal static void LoadSettings(string path)
        {
            blocked.Clear();
            var xml = new XmlDocument();
            xml.Load(path);
            foreach (XmlNode node in xml.SelectNodes("/RatkinRaceOnly/disabledPatches/li"))
                blocked.Add(node.InnerText.Trim());
        }

        internal static bool IsSource(Type type)
        {
            return type != null && Sources.Contains(type.Assembly);
        }

        private static bool Disabled(Type type)
        {
            return IsSource(type) && blocked.Contains(type.FullName);
        }

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Harmony), nameof(Harmony.Patch)),
                prefix: new HarmonyMethod(typeof(ContentPatches), nameof(ManualPrefix)));
            harmony.Patch(AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch)),
                prefix: new HarmonyMethod(typeof(ContentPatches), nameof(ClassPrefix)));

            // Snapshot once: Unpatch changes Harmony's collection of patched methods.
            foreach (MethodBase original in new List<MethodBase>(Harmony.GetAllPatchedMethods()))
            {
                Patches patches = Harmony.GetPatchInfo(original);
                if (patches == null) continue;
                HashSet<MethodInfo> remove = null;
                Collect(patches.Prefixes, ref remove);
                Collect(patches.Postfixes, ref remove);
                Collect(patches.Transpilers, ref remove);
                Collect(patches.Finalizers, ref remove);
                if (remove == null) continue;
                foreach (MethodInfo callback in remove) harmony.Unpatch(original, callback);
            }
        }

        private static void Collect(IList<Patch> patches, ref HashSet<MethodInfo> remove)
        {
            for (int i = 0; i < patches.Count; i++)
            {
                MethodInfo method = patches[i].PatchMethod;
                if (!Disabled(method.DeclaringType)) continue;
                if (remove == null) remove = new HashSet<MethodInfo>();
                remove.Add(method);
            }
        }

        private static bool Filter(ref HarmonyMethod patch)
        {
            if (patch == null || patch.method == null || !Disabled(patch.method.DeclaringType)) return false;
            patch = null;
            return true;
        }

        private static bool ManualPrefix(ref HarmonyMethod prefix, ref HarmonyMethod postfix,
            ref HarmonyMethod transpiler, ref HarmonyMethod finalizer)
        {
            bool blocked = Filter(ref prefix);
            blocked |= Filter(ref postfix);
            blocked |= Filter(ref transpiler);
            blocked |= Filter(ref finalizer);
            return !blocked || prefix != null || postfix != null || transpiler != null || finalizer != null;
        }

        private static bool ClassPrefix(Type ___containerType, ref List<MethodInfo> __result)
        {
            if (!Disabled(___containerType)) return true;
            __result = new List<MethodInfo>();
            return false;
        }
    }

    public static class DefOfPatch
    {
        public static bool Prefix(Type __0)
        {
            if (!ContentPatches.IsSource(__0)) return true;
            foreach (FieldInfo field in __0.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!typeof(Def).IsAssignableFrom(field.FieldType)) continue;
                DefAliasAttribute alias = field.GetCustomAttribute<DefAliasAttribute>();
                string name = alias == null ? field.Name : alias.defName;
                object value = field.FieldType == typeof(SoundDef)
                    ? SoundDef.Named(name)
                    : GenDefDatabase.GetDef(field.FieldType, name, false);
                field.SetValue(null, value);
            }
            return false;
        }
    }
}
