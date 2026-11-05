using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    // Landed titles: the colony's own nobility ladder, separate from the Empire's. Ideology only.
    // Colony nobles (not Empire-titled ones) earn prestige, a running total that's never spent. Crossing a
    // title's prestige makes it pending, and an investiture ritual makes it official. Prestige and the held
    // title are stored on the noble's Hediff_Noble, so losing nobility loses the title.
    public class LandedTitleDef : Def
    {
        [MustTranslate]
        public string labelFemale;

        // The running total of prestige the title needs.
        public int prestige;

        // King and up: one living holder at a time.
        public bool unique;

        // Petty-King and up: the Empire becomes hostile, and can't warm up while a holder lives.
        public bool declaresIndependence;

        public bool IsLandless => prestige <= 0;

        public string LabelFor(Pawn pawn)
        {
            return pawn?.gender == Gender.Female && !labelFemale.NullOrEmpty() ? labelFemale : label;
        }

        public string LabelBothGenders => labelFemale.NullOrEmpty() ? label : label + " / " + labelFemale;
    }

    public static class LandedTitles
    {
        private static List<LandedTitleDef> ladder;

        private static bool descriptionsBuilt;

        public static bool Active => ModsConfig.IdeologyActive;

        // Every title, lowest first.
        public static List<LandedTitleDef> Ladder => ladder ??= DefDatabase<LandedTitleDef>.AllDefs.OrderBy(t => t.prestige).ToList();

        public static LandedTitleDef Landless => Ladder[0];

        public static Faction Empire => Find.FactionManager?.OfEmpire;

        // The noble's landed data, or null for anyone who can't hold a landed title right now: non-nobles,
        // Empire-titled nobles, slaves, prisoners, or any pawn when Ideology is off.
        public static Hediff_Noble Data(Pawn p)
        {
            if (!Active || p?.health?.hediffSet == null || !CasteUtility.IsColonyNoble(p))
            {
                return null;
            }
            return p.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_Noble) as Hediff_Noble;
        }

        public static int Prestige(Pawn p) => Data(p)?.prestige ?? 0;

        // The held title (Landless if none yet), or null for pawns without landed data.
        public static LandedTitleDef Title(Pawn p)
        {
            Hediff_Noble data = Data(p);
            return data == null ? null : data.title ?? Landless;
        }

        // Whether the pawn holds a real (non-landless) landed title. Checks the marker directly, so it
        // still counts if an Empire title was somehow added on top.
        public static bool HoldsTitle(Pawn p)
        {
            return Active && p?.health?.hediffSet?.GetFirstHediffOfDef(CM_DefOf.CM_Noble) is Hediff_Noble noble
                && noble.title != null && !noble.title.IsLandless;
        }

        // The highest title the pawn's prestige reaches, ignoring who holds what.
        public static LandedTitleDef EarnedTitle(Pawn p)
        {
            int prestige = Prestige(p);
            return Ladder.LastOrDefault(t => t.prestige <= prestige) ?? Landless;
        }

        // The title an investiture would give: the highest one the pawn has earned that's above their
        // current title and not held by someone else. Null if there's nothing to invest.
        public static LandedTitleDef PendingTitle(Pawn p)
        {
            Hediff_Noble data = Data(p);
            if (data == null || !p.DevelopmentalStage.Adult())
            {
                return null;
            }
            LandedTitleDef held = data.title ?? Landless;
            for (int i = Ladder.Count - 1; i >= 0; i--)
            {
                LandedTitleDef title = Ladder[i];
                if (title.prestige <= held.prestige)
                {
                    break;
                }
                if (title.prestige > data.prestige || (title.unique && Holder(title, p) != null))
                {
                    continue;
                }
                return title;
            }
            return null;
        }

        // The living colony member who holds this title, other than `except`.
        public static Pawn Holder(LandedTitleDef title, Pawn except = null)
        {
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (p != except && Title(p) == title)
                {
                    return p;
                }
            }
            return null;
        }

        // Whether a Petty-King or higher lives in the colony, which keeps the Empire hostile.
        public static bool SovereignLives()
        {
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (Title(p)?.declaresIndependence == true)
                {
                    return true;
                }
            }
            return false;
        }

        // Every prestige source (Prestige.cs) goes through this. With a reason, the player gets a message
        // like "Aldo gained 1 prestige: led a stirring military parade." Prestige never goes below zero, and
        // a held title is never taken away by losing prestige.
        public static void AddPrestige(Pawn p, int amount, string reason = null)
        {
            Hediff_Noble data = Data(p);
            if (data == null || amount == 0 || !p.DevelopmentalStage.Adult())
            {
                return;
            }
            int before = data.prestige;
            data.prestige = Math.Max(0, data.prestige + amount);
            if (reason != null && data.prestige != before)
            {
                string key = amount > 0 ? "CM_MessagePrestigeGained" : "CM_MessagePrestigeLost";
                Messages.Message(key.Translate(p.Named("PAWN"), Math.Abs(data.prestige - before).Named("AMOUNT"), reason.Named("REASON")), p,
                    amount > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
            }
            LandedTitleDef pending = PendingTitle(p);
            if (pending != null && (data.notifiedTitle == null || data.notifiedTitle.prestige < pending.prestige))
            {
                data.notifiedTitle = pending;
                Find.LetterStack.ReceiveLetter(
                    "CM_LetterTitleEarnedLabel".Translate(pending.LabelFor(p).Named("TITLE")),
                    "CM_LetterTitleEarned".Translate(p.Named("PAWN"), pending.LabelFor(p).Named("TITLE")),
                    LetterDefOf.PositiveEvent, p);
            }
        }

        // Called when an investiture finishes.
        public static void Invest(Pawn p)
        {
            Hediff_Noble data = Data(p);
            LandedTitleDef title = PendingTitle(p);
            if (data == null || title == null)
            {
                return;
            }
            bool firstSovereign = title.declaresIndependence && !SovereignLives();
            data.title = title;
            data.notifiedTitle = null;
            if (CM_DefOf.CM_InvestedWithTitle != null)
            {
                p.needs?.mood?.thoughts?.memories?.TryGainMemory(CM_DefOf.CM_InvestedWithTitle);
            }
            Find.LetterStack.ReceiveLetter(
                "CM_LetterInvestedLabel".Translate(title.LabelFor(p).Named("TITLE"), p.Named("PAWN")),
                "CM_LetterInvested".Translate(p.Named("PAWN"), title.LabelFor(p).Named("TITLE"), Faction.OfPlayer.Name.Named("REALM")),
                LetterDefOf.PositiveEvent, p);
            if (firstSovereign)
            {
                DeclareIndependence(p, title);
            }
        }

        private static void DeclareIndependence(Pawn p, LandedTitleDef title)
        {
            Faction empire = Empire;
            if (empire == null || empire.defeated)
            {
                return;
            }
            empire.TryAffectGoodwillWith(Faction.OfPlayer, -200, canSendMessage: false, canSendHostilityLetter: false);
            Find.LetterStack.ReceiveLetter(
                "CM_LetterIndependenceLabel".Translate(),
                "CM_LetterIndependence".Translate(p.Named("PAWN"), title.LabelFor(p).Named("TITLE"), Faction.OfPlayer.Name.Named("REALM"), empire.Named("EMPIRE")),
                LetterDefOf.NegativeEvent, p);
        }

        // Gives up the landed title and all prestige, so the pawn can accept Empire honor.
        public static void Renounce(Pawn p)
        {
            if (!(p?.health?.hediffSet?.GetFirstHediffOfDef(CM_DefOf.CM_Noble) is Hediff_Noble data))
            {
                return;
            }
            string label = (data.title ?? Landless).LabelFor(p);
            data.title = null;
            data.prestige = 0;
            data.notifiedTitle = null;
            data.patronage = 0;
            Messages.Message("CM_MessageRenouncedLanded".Translate(p.Named("PAWN"), label.Named("TITLE")), p, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        // Who performs the investiture: the leader; if the leader is the one being invested (or isn't
        // here), the highest-titled other noble; failing that, the moral guide. All must be free and on
        // the investee's map.
        public static Pawn InvestorFor(Pawn investee)
        {
            Map map = investee?.MapHeld;
            Ideo ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
            if (map == null || ideo == null)
            {
                return null;
            }
            Pawn leader = RoleHolder(ideo, PreceptDefOf.IdeoRole_Leader);
            if (leader != null && leader != investee && Available(leader, map))
            {
                return leader;
            }
            Pawn best = null;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                LandedTitleDef title = Title(p);
                if (p == investee || title == null || title.IsLandless || !Available(p, map))
                {
                    continue;
                }
                if (best == null || title.prestige > Title(best).prestige)
                {
                    best = p;
                }
            }
            if (best != null)
            {
                return best;
            }
            Pawn moralist = RoleHolder(ideo, PreceptDefOf.IdeoRole_Moralist);
            return moralist != null && moralist != investee && Available(moralist, map) ? moralist : null;
        }

        // Whether the pawn could invest someone, before we know who: used where vanilla checks a role
        // without the dialog's assignments.
        public static bool CouldInvest(Pawn p)
        {
            Ideo ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
            return HoldsTitle(p) || RoleHolder(ideo, PreceptDefOf.IdeoRole_Leader) == p || RoleHolder(ideo, PreceptDefOf.IdeoRole_Moralist) == p;
        }

        private static Pawn RoleHolder(Ideo ideo, PreceptDef roleDef)
        {
            return ideo?.RolesListForReading.FirstOrDefault(r => r.def == roleDef)?.ChosenPawnSingle();
        }

        private static bool Available(Pawn p, Map map)
        {
            return p.MapHeld == map && p.Spawned && !p.Downed && !p.InMentalState && p.IsFreeNonSlaveColonist;
        }

        // Why no investiture can be held at all right now, or null.
        public static string InvestitureBlockedReason()
        {
            Ideo ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
            if (ideo == null || CM_DefOf.CM_Investiture == null || !(ideo.GetPrecept(CM_DefOf.CM_Investiture) is Precept_Ritual))
            {
                return "CM_InvestitureNoRitual".Translate();
            }
            if (CM_DefOf.CM_Castes_Egalitarian != null && ideo.HasPrecept(CM_DefOf.CM_Castes_Egalitarian))
            {
                return "CM_InvestitureEgalitarian".Translate(ideo.Named("IDEO"));
            }
            return null;
        }

        public static Gizmo InvestitureGizmo(Pawn pawn)
        {
            if (!pawn.IsColonistPlayerControlled)
            {
                return null;
            }
            LandedTitleDef pending = PendingTitle(pawn);
            if (pending == null)
            {
                return null;
            }
            Command_Action command = new Command_Action
            {
                defaultLabel = "CM_HoldInvestiture".Translate(pending.LabelFor(pawn)),
                defaultDesc = "CM_HoldInvestitureDesc".Translate(pawn.Named("PAWN"), pending.LabelFor(pawn).Named("TITLE")),
                icon = CommonersTextures.Investiture,
                action = () => BeginInvestiture(pawn)
            };
            string blocked = InvestitureBlockedReason();
            if (blocked != null)
            {
                command.Disable(blocked);
            }
            return command;
        }

        // Opens the begin-ritual dialog with the pawn locked in as the one being invested, the same way
        // vanilla opens the role-change ritual from the social tab.
        private static void BeginInvestiture(Pawn pawn)
        {
            Precept_Ritual ritual = Faction.OfPlayer.ideos?.PrimaryIdeo?.GetPrecept(CM_DefOf.CM_Investiture) as Precept_Ritual;
            if (ritual == null)
            {
                return;
            }
            TargetInfo target = ritual.targetFilter.BestTarget(pawn, TargetInfo.Invalid);
            if (!target.IsValid)
            {
                Messages.Message("CM_InvestitureNeedsSpot".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Find.WindowStack.Add(ritual.GetRitualBeginWindow(target, null, null, pawn, new Dictionary<string, Pawn> { { "investee", pawn } }));
        }

        // "Duke of New Arcadia", for the inspect pane. Landless nobles get no line.
        public static string InspectLine(Pawn p)
        {
            LandedTitleDef title = Title(p);
            if (title == null || title.IsLandless)
            {
                return null;
            }
            return "CM_LandedTitleInspect".Translate(title.LabelFor(p), Faction.OfPlayer.NameColored).Resolve();
        }

        // The tooltip for the character card badge and the Castes tab columns.
        public static string ProgressTip(Pawn p)
        {
            LandedTitleDef title = Title(p);
            if (title == null)
            {
                return null;
            }
            int prestige = Prestige(p);
            StringBuilder text = new StringBuilder();
            text.AppendLine("CM_LandedTitleInspect".Translate(title.LabelFor(p), Faction.OfPlayer.Name).Resolve());
            text.AppendLine();
            text.AppendLine("CM_LandedTipPrestige".Translate(prestige));
            LandedTitleDef next = Ladder.FirstOrDefault(t => t.prestige > Math.Max(prestige, title.prestige));
            text.AppendLine(next == null
                ? "CM_LandedTipTop".Translate().Resolve()
                : "CM_LandedTipNext".Translate(next.LabelFor(p), next.prestige, next.prestige - prestige).Resolve());
            LandedTitleDef pending = PendingTitle(p);
            if (pending != null)
            {
                text.AppendLine();
                text.AppendLine("CM_LandedTipPending".Translate(pending.LabelFor(p)).Resolve());
            }
            LandedTitleDef earned = EarnedTitle(p);
            if (earned.unique && earned.prestige > title.prestige && Holder(earned, p) is Pawn holder)
            {
                text.AppendLine("CM_LandedTipTaken".Translate(earned.LabelFor(p), holder.Named("HOLDER")).Resolve());
            }
            return text.ToString().TrimEndNewlines();
        }

        // Opens the rank card: the info card for a title, which lists the whole ladder like vanilla's
        // Empire title cards. The ladder text is built the first time, once all defs and translations exist.
        public static void OpenRankCard(LandedTitleDef title)
        {
            BuildDescriptions();
            Find.WindowStack.Add(new Dialog_InfoCard(title));
        }

        private static void BuildDescriptions()
        {
            if (descriptionsBuilt)
            {
                return;
            }
            descriptionsBuilt = true;
            StringBuilder ladderText = new StringBuilder();
            ladderText.AppendLine("CM_LandedLadderHeader".Translate().Resolve());
            int previous = 0;
            foreach (LandedTitleDef title in Ladder)
            {
                ladderText.AppendLine(title.IsLandless
                    ? "  - " + "CM_LandedLadderFirst".Translate(title.LabelBothGenders).Resolve()
                    : "  - " + "CM_LandedLadderLine".Translate(title.LabelBothGenders, title.prestige - previous, title.prestige).Resolve());
                previous = title.prestige;
            }
            ladderText.AppendLine();
            ladderText.AppendLine("CM_LandedLadderUnique".Translate(Ladder.First(t => t.unique).label).Resolve());
            ladderText.Append("CM_LandedLadderIndependence".Translate(Ladder.First(t => t.declaresIndependence).label).Resolve());
            foreach (LandedTitleDef title in Ladder)
            {
                title.description = title.description + "\n\n" + ladderText;
                title.descriptionHyperlinks = Ladder.Where(t => t != title).Select(t => new DefHyperlink(t)).ToList();
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class LandedTitlesStartup
    {
        static LandedTitlesStartup()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return;
            }
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CM_ColTitle".Translate(),
                width = 110f,
                value = p => LandedTitles.Title(p)?.LabelFor(p) ?? "",
                tooltip = LandedTitles.ProgressTip
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CM_ColPrestige".Translate(),
                width = 70f,
                value = p => LandedTitles.Data(p) != null ? LandedTitles.Prestige(p).ToString() : "",
                tooltip = LandedTitles.ProgressTip
            });
        }
    }

    // ===== Investiture ritual =====

    // Only a colony noble with a title waiting can be invested.
    public class RitualRole_Investee : RitualRole
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            reason = null;
            if (LandedTitles.PendingTitle(p) != null)
            {
                return true;
            }
            if (!skipReason)
            {
                reason = "CM_InvesteeNoPendingTitle".Translate(p.Named("PAWN"));
            }
            return false;
        }

        public override bool AppliesToRole(Precept_Role role, out string reason, Precept_Ritual ritual = null, Pawn p = null, bool skipReason = false)
        {
            reason = null;
            return false;
        }
    }

    // Only the pawn LandedTitles.InvestorFor picks can invest. When vanilla checks the role without knowing
    // who's being invested, anyone who could ever invest passes.
    public class RitualRole_Investor : RitualRole
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!AppliesIfChild(p, out reason, skipReason))
            {
                return false;
            }
            if (!p.IsFreeNonSlaveColonist)
            {
                if (!skipReason)
                {
                    reason = "MessageRitualRoleMustBeFreeColonist".Translate(Label);
                }
                return false;
            }
            Pawn investee = assignments?.FirstAssignedPawn("investee") ?? ritual?.assignments?.FirstAssignedPawn("investee");
            if (investee == null)
            {
                return LandedTitles.CouldInvest(p);
            }
            Pawn designated = LandedTitles.InvestorFor(investee);
            if (designated == p)
            {
                return true;
            }
            if (!skipReason)
            {
                reason = designated == null
                    ? "CM_NoInvestor".Translate(investee.Named("PAWN"))
                    : "CM_OnlyInvestor".Translate(designated.Named("INVESTOR"), investee.Named("PAWN"));
            }
            return false;
        }

        public override bool AppliesToRole(Precept_Role role, out string reason, Precept_Ritual ritual = null, Pawn p = null, bool skipReason = false)
        {
            reason = null;
            return false;
        }
    }

    // Blocks the investiture under an egalitarian ideoligion, says in the dialog which title will be given,
    // and asks for confirmation before the first crown, which turns the Empire hostile.
    public class RitualBehaviorWorker_Investiture : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_Investiture()
        {
        }

        public RitualBehaviorWorker_Investiture(RitualBehaviorDef def)
            : base(def)
        {
        }

        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null)
        {
            string blocked = LandedTitles.InvestitureBlockedReason();
            if (blocked != null)
            {
                return blocked;
            }
            if (forcedForRole != null && forcedForRole.TryGetValue("investee", out Pawn investee) && investee != null && LandedTitles.InvestorFor(investee) == null)
            {
                return "CM_NoInvestor".Translate(investee.Named("PAWN"));
            }
            return base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        }

        public override string GetExplanation(Precept_Ritual ritual, RitualRoleAssignments assignments, float quality)
        {
            Pawn investee = assignments?.FirstAssignedPawn("investee");
            LandedTitleDef title = LandedTitles.PendingTitle(investee);
            if (title == null)
            {
                return null;
            }
            string text = "CM_InvestitureExplanation".Translate(investee.Named("PAWN"), title.LabelFor(investee).Named("TITLE"), Faction.OfPlayer.Name.Named("REALM"));
            if (DefiesEmpire(title))
            {
                text += "\n\n" + "CM_InvestitureIndependenceWarning".Translate(investee.Named("PAWN"), LandedTitles.Empire.Named("EMPIRE")).Resolve().Colorize(ColorLibrary.RedReadable);
            }
            return text;
        }

        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            Pawn investee = assignments.FirstAssignedPawn("investee");
            LandedTitleDef title = LandedTitles.PendingTitle(investee);
            if (title != null && DefiesEmpire(title))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "CM_InvestitureIndependenceConfirm".Translate(investee.Named("PAWN"), title.LabelFor(investee).Named("TITLE"), LandedTitles.Empire.Named("EMPIRE")),
                    () => base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced),
                    destructive: true));
                return;
            }
            base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced);
        }

        // The first crown turns the Empire hostile; after that it already is.
        private static bool DefiesEmpire(LandedTitleDef title)
        {
            return title.declaresIndependence && LandedTitles.Empire != null && !LandedTitles.Empire.defeated && !LandedTitles.SovereignLives();
        }
    }

    // The usual quality-based mood for everyone who attended, then the title itself.
    public class RitualOutcomeEffectWorker_Investiture : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_Investiture()
        {
        }

        public RitualOutcomeEffectWorker_Investiture(RitualOutcomeEffectDef def)
            : base(def)
        {
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            Pawn investee = jobRitual.assignments.FirstAssignedPawn("investee");
            base.Apply(progress, totalPresence, jobRitual);
            if (investee != null && !investee.Dead)
            {
                LandedTitles.Invest(investee);
            }
        }
    }

    // ===== Display =====

    // "Duke of New Arcadia" on the line under the pawn's description, where vanilla shows an Empire title.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class Patch_Pawn_GetInspectString
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            string line = LandedTitles.InspectLine(__instance);
            if (line == null)
            {
                return;
            }
            int firstBreak = __result?.IndexOf('\n') ?? -1;
            if (firstBreak < 0)
            {
                __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
            }
            else
            {
                __result = __result.Insert(firstBreak + 1, line + "\n");
            }
        }
    }

    // A title badge in the character card's top row, next to where Empire titles appear. Vanilla builds that
    // row in a private list and draws it with GenUI.DrawElementStack; this inserts a call that adds our badge
    // to the list just before the last DrawElementStack call in DoTopStack.
    [HarmonyPatch(typeof(CharacterCardUtility), "DoTopStack")]
    public static class Patch_CharacterCardUtility_DoTopStack
    {
        private static readonly FieldInfo StackElements = AccessTools.Field(typeof(CharacterCardUtility), "tmpStackElements");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            int draw = codes.FindLastIndex(c => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && c.operand is MethodInfo m && m.Name == nameof(GenUI.DrawElementStack));
            if (draw < 0)
            {
                Log.Warning("[Commoners] Couldn't find the character card's title row; landed title badges won't show.");
                return codes;
            }
            // The arguments for DrawElementStack are already on the stack here. Our call pushes the pawn
            // (argument 0 of DoTopStack) and consumes it, so the stack is unchanged when DrawElementStack runs.
            codes.InsertRange(draw, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.Call(typeof(Patch_CharacterCardUtility_DoTopStack), nameof(AddBadge))
            });
            return codes;
        }

        public static void AddBadge(Pawn pawn)
        {
            LandedTitleDef title = LandedTitles.Title(pawn);
            if (title == null || !(StackElements?.GetValue(null) is List<GenUI.AnonymousStackElement> elements))
            {
                return;
            }
            string label = title.LabelFor(pawn) + " (" + LandedTitles.Prestige(pawn) + ")";
            elements.Add(new GenUI.AnonymousStackElement
            {
                drawer = r =>
                {
                    Color color = GUI.color;
                    GUI.color = CharacterCardUtility.StackElementBackground;
                    GUI.DrawTexture(r, BaseContent.WhiteTex);
                    GUI.color = Faction.OfPlayer.Color;
                    GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, 20f, 20f), CommonersTextures.Investiture);
                    GUI.color = color;
                    Widgets.Label(new Rect(r.x + 27f, r.y, r.width - 27f, r.height), label);
                    if (Mouse.IsOver(r))
                    {
                        Widgets.DrawHighlight(r);
                        TooltipHandler.TipRegion(r, () => LandedTitles.ProgressTip(pawn), pawn.thingIDNumber * 59);
                    }
                    if (Widgets.ButtonInvisible(r))
                    {
                        LandedTitles.OpenRankCard(title);
                    }
                },
                width = Text.CalcSize(label).x + 22f + 14f
            });
        }
    }

    // ===== The Empire =====

    // Landed and Empire titles are exclusive: a pawn holding a landed title refuses Empire honor and titles.
    // (Empire-titled pawns can't earn prestige, because they don't count as colony nobles.)
    [HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GainFavor))]
    public static class Patch_Pawn_RoyaltyTracker_GainFavor
    {
        public static bool Prefix(Pawn_RoyaltyTracker __instance, Faction faction, int amount)
        {
            if (amount <= 0 || !LandedTitles.HoldsTitle(__instance.pawn))
            {
                return true;
            }
            Messages.Message("CM_RefusesEmpireHonor".Translate(__instance.pawn.Named("PAWN"), faction.Named("FACTION")), __instance.pawn, MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.SetTitle))]
    public static class Patch_Pawn_RoyaltyTracker_SetTitle
    {
        public static bool Prefix(Pawn_RoyaltyTracker __instance, Faction faction, RoyalTitleDef title)
        {
            if (title == null || !LandedTitles.HoldsTitle(__instance.pawn))
            {
                return true;
            }
            Messages.Message("CM_RefusesEmpireHonor".Translate(__instance.pawn.Named("PAWN"), faction.Named("FACTION")), __instance.pawn, MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }
    }

    // While a Petty-King or higher lives in the colony, goodwill between the Empire and the player can't rise.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_Faction_TryAffectGoodwillWith
    {
        public static bool Prefix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (goodwillChange <= 0 || other == null || !LandedTitles.Active)
            {
                return true;
            }
            Faction empire = LandedTitles.Empire;
            if (empire == null || !((__instance == empire && other.IsPlayer) || (__instance.IsPlayer && other == empire)))
            {
                return true;
            }
            if (!LandedTitles.SovereignLives())
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    // ===== Dev tools =====

    public static class LandedTitleDebugActions
    {
        [DebugAction("Commoners", "Add 1 prestige", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, requiresIdeology = true)]
        private static void AddOne(Pawn p) => LandedTitles.AddPrestige(p, 1);

        [DebugAction("Commoners", "Add 5 prestige", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, requiresIdeology = true)]
        private static void AddFive(Pawn p) => LandedTitles.AddPrestige(p, 5);

        [DebugAction("Commoners", "Remove 5 prestige", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, requiresIdeology = true)]
        private static void RemoveFive(Pawn p) => LandedTitles.AddPrestige(p, -5);

        [DebugAction("Commoners", "Invest pending title now", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, requiresIdeology = true)]
        private static void InvestNow(Pawn p) => LandedTitles.Invest(p);

        [DebugAction("Commoners", "Renounce landed title", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap, requiresIdeology = true)]
        private static void RenounceNow(Pawn p) => LandedTitles.Renounce(p);
    }
}
