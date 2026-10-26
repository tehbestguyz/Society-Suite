using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace GuardPosts
{
    // Shows Shooting and Melee side by side and highlights the higher one, so it's clear what weapon a guard should carry.
    public class PawnColumnWorker_CombatSkill : PawnColumnWorker
    {
        private const float IconSize = 16f;

        private static readonly Color BestBackground = new Color(1f, 1f, 1f, 0.08f);

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            SkillRecord shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            if (shooting == null || melee == null)
            {
                return;
            }

            int best = Best(shooting, melee);
            Rect left = new Rect(rect.x, rect.y + 2f, rect.width / 2f - 2f, rect.height - 4f);
            Rect right = new Rect(left.xMax + 4f, left.y, left.width, left.height);
            DrawSkill(left, shooting, best >= 0 && !shooting.TotallyDisabled);
            DrawSkill(right, melee, best <= 0 && !melee.TotallyDisabled);

            TooltipHandler.TipRegion(rect, new TipSignal(() => Tip(shooting, melee), pawn.thingIDNumber ^ 0x5C011));
        }

        private static void DrawSkill(Rect rect, SkillRecord skill, bool highlight)
        {
            if (highlight)
            {
                Widgets.DrawBoxSolid(rect, BestBackground);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = highlight ? Color.white : Color.gray;
            string text = skill.def.skillLabel.CapitalizeFirst() + " " + (skill.TotallyDisabled ? "-" : skill.Level.ToString());
            Rect label = new Rect(rect.x + 4f, rect.y, rect.width - IconSize - 6f, rect.height);
            Widgets.Label(label, text.Truncate(label.width));
            if (!skill.TotallyDisabled)
            {
                Texture2D icon = PassionUtil.Icon(skill.passion);
                if (icon != null)
                {
                    float x = Mathf.Min(label.x + Text.CalcSize(text).x + 2f, rect.xMax - IconSize);
                    GUI.DrawTexture(new Rect(x, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize), icon);
                }
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // 1 = shooting is better, -1 = melee, 0 = tie (both highlighted).
        private static int Best(SkillRecord shooting, SkillRecord melee)
        {
            if (shooting.TotallyDisabled || melee.TotallyDisabled)
            {
                return shooting.TotallyDisabled ? (melee.TotallyDisabled ? 0 : -1) : 1;
            }
            int byLevel = shooting.Level.CompareTo(melee.Level);
            return byLevel != 0 ? byLevel : PassionUtil.LearnFactor(shooting.passion).CompareTo(PassionUtil.LearnFactor(melee.passion));
        }

        private static string Tip(SkillRecord shooting, SkillRecord melee)
        {
            string tip = SkillLine(shooting) + "\n" + SkillLine(melee);
            if (shooting.TotallyDisabled && melee.TotallyDisabled)
            {
                return tip;
            }
            int best = Best(shooting, melee);
            string advice = best > 0 ? "GP_BestShooting".Translate() : best < 0 ? "GP_BestMelee".Translate() : "GP_BestEither".Translate();
            return tip + "\n\n" + advice;
        }

        private static string SkillLine(SkillRecord skill)
        {
            string line = skill.def.skillLabel.CapitalizeFirst() + ": ";
            if (skill.TotallyDisabled)
            {
                return line + "GP_SkillDisabled".Translate();
            }
            return line + skill.Level + " (" + PassionUtil.Label(skill.passion) + ")";
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 180);

        public override int GetOptimalWidth(PawnTable table) => Mathf.Clamp(200, GetMinWidth(table), GetMaxWidth(table));

        // Shooters first, then melee fighters, each by their better skill's level.
        public override int Compare(Pawn a, Pawn b) => SortKey(a).CompareTo(SortKey(b));

        private static int SortKey(Pawn pawn)
        {
            SkillRecord shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee = pawn.skills?.GetSkill(SkillDefOf.Melee);
            if (shooting == null || melee == null || (shooting.TotallyDisabled && melee.TotallyDisabled))
            {
                return -1;
            }
            return Best(shooting, melee) >= 0 ? 100 + shooting.Level : melee.Level;
        }
    }

    // Vanilla Skills Expanded adds its own passions (stored as extra Passion values), so read their icons and labels from it when it's loaded.
    [StaticConstructorOnStartup]
    public static class PassionUtil
    {
        private static Func<Passion, Def> vsePassionToDef;
        private static PropertyInfo vseIcon;
        private static FieldInfo vseLearnRate;

        static PassionUtil()
        {
            try
            {
                Type manager = GenTypes.GetTypeInAnyAssembly("VSE.Passions.PassionManager");
                MethodInfo toDef = manager?.GetMethod("PassionToDef", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Passion) }, null);
                if (toDef != null)
                {
                    vseIcon = toDef.ReturnType.GetProperty("Icon");
                    vseLearnRate = toDef.ReturnType.GetField("learnRateFactor");
                    vsePassionToDef = p => (Def)toDef.Invoke(null, new object[] { p });
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Guard Posts] Couldn't read Vanilla Skills Expanded passions: " + e.Message);
                vsePassionToDef = null;
            }
        }

        private static Def VseDef(Passion passion)
        {
            if (vsePassionToDef == null)
            {
                return null;
            }
            try
            {
                return vsePassionToDef(passion);
            }
            catch
            {
                return null;
            }
        }

        public static Texture2D Icon(Passion passion)
        {
            Def def = VseDef(passion);
            if (def != null && vseIcon != null)
            {
                return passion == Passion.None ? null : vseIcon.GetValue(def) as Texture2D;
            }
            switch (passion)
            {
                case Passion.Minor:
                    return SkillUI.PassionMinorIcon;
                case Passion.Major:
                    return SkillUI.PassionMajorIcon;
                default:
                    return null;
            }
        }

        public static string Label(Passion passion)
        {
            Def def = VseDef(passion);
            return def != null && !def.label.NullOrEmpty() ? def.LabelCap.ToString() : passion.GetLabel();
        }

        public static float LearnFactor(Passion passion)
        {
            Def def = VseDef(passion);
            if (def != null && vseLearnRate != null)
            {
                return (float)vseLearnRate.GetValue(def);
            }
            return passion.GetLearningFactor();
        }
    }
}
