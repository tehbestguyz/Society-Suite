# Commoners: design notes

A worker caste between slave and colonist. Lower expectations like a slave, but with no suppression, terror or rebellion. The balance comes from fixed costs instead of a need to manage.

## Status

- A commoner is a normal free colonist with the `CM_Commoner` hediff (`Hediff_Commoner`). Adding or removing it refreshes the pawn's disabled work types.
- `CasteUtility.IsCommoner` = has the hediff, and isn't a slave or a prisoner. An arrested commoner keeps the marker and is a commoner again on release.
- Enslaving a pawn removes the marker and any recruitment offer (`Patch_SetGuestStatus`).
- `Pawn.LegalStatus` returns "Commoner", so the vanilla age 3 letter reads "Remain commoner".

## Benefit: expectations

`CM_ExpectationsCommoner` replaces the vanilla expectations thought, which is switched off for commoners. The values sit halfway between colonist and slave:

| Tier | Colonist | Commoner | Slave |
|---|---|---|---|
| Extremely low | +30 | +38 | +46 |
| Very low | +24 | +32 | +40 |
| Low | +18 | +26 | +34 |
| Moderate | +12 | +20 | +28 |
| High | +6 | +17 | +28 |
| Sky-high | 0 | +14 | +28 |

## Costs (all configurable in mod settings)

- **Work:** Research, Art and Dark Study are disabled by default. Patched in `Pawn.GetDisabledWorkTypes`, with a tooltip reason added in `GetReasonsForDisabledWorkType`. Backstory work disables still apply, unlike for slaves.
- **Skills:** every skill stops gaining XP at 6, except Plants and Animals, which stop at 12 (`SkillRecord.Learn` prefix/postfix). Skills already above the cap are kept, and skill decay still applies.
- **Roles:** at startup, every `Precept_Role` def gets a `RoleRequirement_NotCommoner`, so vanilla role validation and UI handle it. Demotion unassigns any current role.
- **Royalty:** `GainFavor` is blocked for commoners, with a message. Commoners can't accept quests that give honor to the accepter, and never qualify for bestowing ceremonies. Pawns with a royal title can't be demoted.
- **Social:** colonists get −4 opinion of commoners ("commoner"; Kind nullifies it). Commoners get +3 opinion of each other ("fellow commoner"). Romance between castes is ×0.5 (`SecondaryRomanceChanceFactor`).

## Moving between castes

| From → to | How | Mood |
|---|---|---|
| Prisoner → commoner | "Recruit as commoner" toggle on the prisoner (adds `CM_CommonerOffer`). Resistance drops ×1.5. `RecruitUtility.Recruit` converts the offer into the marker. | none |
| Slave → commoner | Slave mode "Elevate to commoner". A warden does it (`WorkGiver_Warden_ElevateSlave`, `JobDriver_ElevateSlave`). | vanilla "was enslaved" −6, plus "raised from slavery" +8 |
| Colonist → commoner | "Demote to commoner" gizmo, with a confirmation | −12 for 20 days (Ascetic is immune). None within 3 days of joining (the `TimeAsColonistOrColonyAnimal` record). |
| Commoner → colonist | "Promote to colonist" gizmo | +10 for 10 days. Removes any "demoted" memory. |

## Children (Biotech)

- At birth (`PregnancyUtility.ApplyBirthOutcome`), the baby takes the mother's caste if she's in the same faction, otherwise the father's. This covers natural births and growth vats.
- **Coming of age (2026-10-17, not tested in-game):** on the birthday the game treats as adulthood (`AdultMinAge`, 13 for humans), a postfix on the private `Pawn_AgeTracker.BirthdayBiological` resets the child's caste. It becomes commoner, or the higher caste of a parent still in the player faction (alive or dead). The user chose a reset, so whatever the child was before (birth caste, the age 3 choice, the caste menu) is replaced. Artisan parents count as citizens (the user's choice), and so do noble parents when hereditary nobility is off. There are no promotion or demotion memories, just a message. Royal-titled and non-free children are skipped. Teens who were already past 13 when this shipped aren't changed. Since it's inheritance, Wages' "citizenship needs a home" rule doesn't apply.
- The age 3 letter gets a third option: "Raise as colonist" for commoner children, or "Raise as commoner" for free and slave children. It's inserted after the vanilla colonist/slave pair.

## Not in v1

- An Ideology precept for caste systems
- A replacement need for suppression ("standing")

---

# v2: five castes (built 2026-10-16, not tested in-game)

## As built: changes from the plan below

- **Specialties** are `CM_SpecialtyDef` defs: Crafting, Art, Construction, Cooking and Medicine. Tailoring is part of Crafting, because the quality hook (`QualityUtility.GenerateQualityCreatedByPawn`) only knows the skill, not the product. Each specialty adds stat bonuses through `Hediff_Artisan.CurStage`. Cooking and Medicine have no item quality, so they get stats only.
- **Empire nobles** count as nobles for status, opinion, romance, the lordship aura, inheritance and leader roles. The colony-noble costs (expectations, menial work ban, quarters, standing) and the `CM_Noble` stat bonuses only apply to pawns with the marker and no Empire title (`IsColonyNoble`), so they never stack with a title's own requirements.
- **Changing caste** uses one "Caste: X" gizmo and the Castes main tab, which share `CasteMenu`. Moving down asks for confirmation.
- **Standing need** (`Need_Standing`) drains in about 6 days.
  - A fine bedroom halves the drain.
  - When a lower caste talks to the noble: +0.025 for chitchat, +0.05 for a deep talk, −0.06 for an insult.
  - A finished ritual: +0.25, or +0.4 for the organizer.
  - Mood: below 15% "humiliated" (−10), below 35% "slighted" (−5), above 80% "respected" (+4).
- **Noble quarters:** a barracks bed is −8, and a bedroom below the impressiveness setting (50) is −4. The hall or throne room is not built.
- **Lordship aura:** it only reaches lower castes within 15 cells. A noble below their minor break threshold gives −3; otherwise the bonus is +1, +2 or +4 by Social skill (8 and 15 are the steps).
- **Precept issue `CM_Castes`:** egalitarian, meritocratic, accepted (no effect) and hierarchical. Ideoligions from before the mod have none until they're reformed.
- **Free ennoblement** is hidden when Wages is loaded. Wages sets `CasteUtility.NobilityMustBePurchased`.
- **Opinion:** "looking down" is −3 per tier (`lookDownPerTier`), with a stage per lower caste. Fellow commoner is +3, guildmate +3 and fellow noble +2. Jealous pawns get −4 toward anyone higher. Greedy commoners and citizens get −3 mood.

## Soldiers (2026-10-17, not tested in-game)

The user's design: a soldier caste equal in standing to citizens, with commoners' expectations, commoners' pay and no taxes. Soldiers learn Melee and Shooting faster, dislike skilled labor, and have every other skill capped at 8. The user's calls: enlisting from commoner needs no home (military service is the other way up), and "skilled labor" means crafting and study work, not cooking or construction.

- **Caste:** `Caste.Soldier` comes before Noble in the enum, with `Tier()` 2 (the same as citizens). The marker is `CM_Soldier`, using the plain `Hediff_CasteMark`. Anyone not incapable of violence can enlist from the caste menu (`CM_CannotNonViolent`). Moving between citizen and soldier is sideways: no mood, just `CM_MessageCasteChanged`. Enlisting from commoner is a promotion (`CM_Promoted` stage 4, "enlisted as a soldier", +8).
- **Skills:** `CasteUtility.SkillCapFor` replaces the commoner-only cap. Soldiers are capped at `soldierSkillCap` (8) in everything except Shooting and Melee, which learn at `soldierLearnFactor` (×1.5). Skills already above the cap aren't lowered; they just stop rising.
- **Expectations:** `CM_ExpectationsSoldier` has the same values as commoners, with soldier text. `ThoughtWorker_ExpectationsCommoner` now reads the caste from `CasteThoughtExtension` (default Commoner).
- **Skilled labor:** `CM_NotSoldiersWork` is −3 for a day (Ascetic-immune), from the same `StartJob` postfix, for work in `skilledWork`. The default list is Crafting, Smithing, Tailoring, Art, Research, Dark Study and Doctor, editable in settings like the menial list.
- **Elsewhere:**
  - Respect (the user's tweak): commoners, citizens and nobles look up to soldiers as the colony's defenders. `CM_OurDefender` gives +4 opinion (`soldierRespectOpinion`), and nobles no longer look down on them (`CasteUtility.RespectsSoldiers`). Artisans and scholars still look down on soldiers as brutish, through the normal per-tier offset.
  - Social: `CM_LowerStation` has a "soldier" stage 4. Greedy soldiers still want to rise. Egalitarians count soldiers as a caste.
  - Children: soldiers' children come of age as citizens.
  - UI: soldiers have their own group on the Castes tab, and a shield-and-sword icon.
- **Wages:** `soldierWage` 25 (the same as commoners). `TaxFor` returns 0. The Wages citizenship-needs-home postfix skips a target of Soldier.

## Scholars (2026-10-17, not tested in-game)

The user wanted a sixth caste on the same tier as artisans, based on Research, Dark Study and doctoring, with no commissions, lower wages and lower tax than artisans. The user's calls: Medicine moves from artisans to scholars, and scholars mirror artisans.

- **Enum:** `Caste.Scholar` sits between Artisan and Noble in the enum, but the enum order is no longer the ranking. `CasteUtility.Tier()` puts artisans and scholars both at tier 3, and every comparison uses it: opinion gap, romance gap, resentment, looking down, demotion depth, the promote/demote direction in the menu, and coming of age. `IsSpecialistCaste` / `IsSpecialist` cover both castes.
- **Marker:** `CM_Scholar` with `Hediff_Scholar : Hediff_Artisan`, so it stores the specialty the same way. `CM_SpecialtyDef` gained `caste` (Artisan by default) and `workType`.
- **Specialties:**
  - Doctoring: the old `CM_Specialty_Medicine` defName, relabelled, Medicine skill, Doctor work.
  - Research: Intellectual skill, +15% ResearchSpeed.
  - Dark study: Intellectual skill, +15% StudyEfficiency, `MayRequire` Anomaly.
  The menu lists each specialty under its own caste.
- **Migration:** the `ScholarMigration` GameComponent turns any living artisan with a scholar specialty into a scholar in `FinalizeInit`, with one message.
- **Perks, mirroring artisans:** faster learning in their skill, the specialty stat bonus, more inspirations, the guildmate opinion bonus (same specialty), and Meritocratic respect (precept thoughts about artisans now cover the whole tier).
- **Costs:** their own `CM_ExpectationsScholar` (same values as artisans), the menial-work thought, and `CM_PoorStudy` (−3 for a day). The last comes from a `StartJob` postfix whenever a scholar starts their specialty's work in a room below `artisanWorkshopImpressiveness`. The room is taken from the job's target A: the bench, the patient or the entity.
- **New stages, appended so old saves keep their stage numbers:** `CM_Promoted` gets "certified as a scholar" as stage 3, and `CM_LowerStation` gets "scholar" as stage 3.
- **Children:** scholars' children inherit citizen at 13, like artisans'.
- **Icon:** `UI/Commoners/Scholar` is an open book on a purple cover, drawn with PIL.
- **Wages:** `scholarWage` 50 (artisans 60), `taxScholar` 80 (artisans 100), no commissions. The Wages DLL has to be rebuilt whenever the `Caste` enum changes, because enum values are compiled in as constants.

All 24 Commoners patch classes and 20 Wages patch classes were smoke-tested against the new DLLs.

---

# v2 plan as agreed

Slave < Commoner < Citizen < Artisan < Noble. Free colonists are citizens by default. Each step up makes a pawn harder to please, and more useful or more privileged.

| | Slave | Commoner | Citizen | Artisan | Noble |
|---|---|---|---|---|---|
| Marker | vanilla slave | `CM_Commoner` | none | `CM_Artisan` | `CM_Noble` |
| Expectations | vanilla slave | halfway (v1 table) | vanilla | one step pickier | two steps pickier |
| Work | slave rules | no Research/Art/Dark Study | everything | "beneath my station" mood penalty for menial work | refuses menial work |
| Skills | — | cap 6 (12 for Plants/Animals) | uncapped | learning and quality bonus in their specialty | — |
| Wages (with the Wages add-on) | none | yes | yes | yes, plus commissions | none: they own the settlement |

Menial work means Hauling, Cleaning, PlantCutting and Mining. Each should be a setting.

## Architecture

- A `Caste` enum, plus a `CasteExtension` mod extension on each marker hediff that holds that tier's rules (expectations, work bans, caps, opinion). The patches read the rules from the data instead of calling `IsCommoner`.
- `CasteUtility.GetCaste(p)` replaces `IsCommoner`. Keep `IsCommoner` as a wrapper so the Wages add-on keeps compiling during the move.
- Citizens have no marker, so vanilla colonists aren't touched and removing the mod stays clean.
- `CM_Commoner` keeps its def name, so v1 saves still load.
- Player-facing "colonist" strings become "citizen" ("Promote to citizen", legal status "Citizen", and so on).

## Artisan

- **Specialty:** each artisan has one, stored on the hediff: Crafting, Art, Construction, Cooking, Medicine or Tailoring (Tailoring is the Crafting skill on tailoring benches).
- **Perks in the specialty:** faster learning, a chance at one quality level higher (`QualityUtility.GenerateQualityCreatedByPawn`), a higher inspiration chance, and a "maker's mark" thought when a colonist uses their masterwork.
- **Becoming one:** a "Certify as artisan" gizmo needs 10 or more in the specialty skill (a setting). It's earned, not bought, so commoners have to become citizens first because of their skill cap.
- **Costs:** pickier expectations, the menial work penalty, and "wants a proper workshop" (based on how impressive the room with their bench is).
- **Social:** a guild bonus between artisans of the same specialty.
- **Children:** artisan status isn't inherited, so their children are citizens.

## Noble

- **Seats:** 1 noble per 10 free colonists, counting commoners, citizens, artisans and nobles, but not slaves, prisoners or quest lodgers. With 9 free colonists you have no seats. The limit only blocks new ennoblements: nobles keep their rank if the population drops, and hereditary births may go over it.
- **Becoming one:** pawns save up silver, the same way commoners buy citizenship (Wages add-on, 5000 silver by default). Without Wages, a seat-limited "Ennoble" gizmo is free, the same way "Promote" works in core today.
- **Empire titles imply nobility.** A pawn with any Empire title counts as a noble and doesn't use a seat. If the title is lost, the pawn drops back to citizen unless they were ennobled independently. The two systems work together or separately.
- **Perks:**
  - A lordship aura: lower castes nearby get a small work speed or mood bonus, scaled by the noble's Social skill. An unhappy noble turns it negative.
  - Better trade prices and bigger goodwill gains.
  - Optionally, Ideology leader and moral guide are reserved for nobles (a setting).
- **Costs:** refuses menial work, demanding room requirements (bedroom, plus an optional hall or throne room), and the "standing" need below.
- **Children:** nobility is hereditary (a setting): a child is noble if either parent is.

## Shared rules (replace the v1 flat values)

- **Moving between castes:** promote or demote one tier or several, with requirements checked against the target tier only. The demotion mood penalty scales with how many tiers the pawn drops. Ascetic pawns are still immune.
- **Opinion:** a few points of looking down per tier of difference, and a same-tier bonus. Looking *up* is respect or resentment depending on traits and ideoligion. Kind still ignores it.
- **Romance:** the chance factor shrinks with each tier of difference. Marrying a noble can raise the spouse to noble consort.
- **Traits:** Greedy and Jealous want to rise and are upset when passed over. Ascetic doesn't care.
- **Ideology precept "Castes":**
  - Egalitarian hates castes colony-wide, like "slavery: abhorrent".
  - Hierarchical likes them.
  - Meritocratic respects artisans and resents nobility.
- **Standing need (nobles only):** filled by deference from lower castes, good rooms and ceremonies.
- **UI:** a caste icon on the colonist bar (chain, hand, none, hammer, crown), and a Castes main tab listing pawns by tier with seat counts and promote/demote buttons.

## Open

- Should the mod's display name change from "Commoners" to something like "Castes"? The packageId stays `cashew.commoners` either way.

## Castes tab buttons for add-ons (2026-10-17)

`CastesTabExtensions` gained `RowButtons`, `GroupButtons` and `TopButtons` (lists of `CastesTabButton`), in addition to `Columns` and `SummaryLines`. Each button gets the pawns in its scope: the row's pawn, the caste's members, or every colony member. It's hidden while its `label` returns empty. Row buttons sit left of "Change caste" and widen the window by their width. Group buttons sit on the right of each caste header, and top buttons sit to the right of the title. The Wages add-on uses them for "Forgive taxes", so rebuild Commoners before Wages.

## Militarist meme (2026-10-17, not tested in-game)

`CM_Militarist` in `Defs/Memes_Militarist.xml` (MayRequire Ideology), icon `Textures/UI/Commoners/Militarist.png`. Normal meme, impact 1, group Misc.

- **Unlocks:** `Patches/Militarist.xml` adds it to `requiredMemes` on `IdeoRole_ShootingSpecialist`, `IdeoRole_MeleeSpecialist`, `GladiatorDuel` and `Corpses_DontCare` (allowed only, not added to its associatedMemes). `requiredMemes` means "any of these". The meme tooltip lists the roles on its own (`MemeDef.UnlockedRoles`), but it only lists rituals that use a consumable building, so the description mentions the duel.
- **Required precepts:** `requireOne` is a list of groups, and the ideoligion needs one precept from each group. Group 1 is Execution: required, respected if guilty, or don't care (same as Supremacist). "Death: don't care" is `VME_Death_DontCare` from Vanilla Ideology Expanded - Memes and Structures, not vanilla, so a FindMod patch adds it as group 2 only when that mod is active.
- **Conversion:** Bloodlust is agreeable and Wimp disagreeable (±20% conversion power, via `agreeableTraits`/`disagreeableTraits`).
- **Patch gating:** the patch loader ignores `MayRequire` on `<Operation>` nodes (only def nodes and list items honor it), so the patches use `PatchOperationFindMod` ("Ideology" and the VME mod name).

## Military parade ritual (2026-10-21, not tested in-game)

`Defs/Ritual_MilitaryParade.xml` (all MayRequire Ideology) and `Source/MilitaryParade.cs`. Unlocked by Militarist through the precept's `requiredMemes`; `IdeoFoundation.AddRitualsForMemes` adds it automatically when an ideoligion gains the meme. An ideoligion that already had Militarist before this gets it by adding the ritual in Reform.

The user's calls: placeable route markers (`CM_ParadeStart`, `CM_ParadeEnd`, free, Ideology build tab), Militarist only, the leader speaks with anyone of the ideoligion standing in, and any free fighter can march, with soldier-caste pawns counting double toward quality.

- **Target:** any ritual spot or ideo building, like the leader speech. Both markers must be in the target's gathering area (`GatheringsUtility.InGatheringArea`: same room, or 18 cells outdoors), because pawns outside it don't count as attending. Markers 5+ cells apart. The nearest marker of each kind is used. Hovering the ritual button draws the route.
- **Stages:** form up (ends when all have arrived, or 20%), speech (35%), march at Amble to the end marker (ends when all soldiers have arrived, or 35%), salute facing the ritual spot (25%). The last stage sets `ignoreDurationToFinishAfterStage`: `RitualFinished` otherwise needs the full `durationTicks` of progress, and the early-ending stages would make it count as called off.
- **Formation:** ranks of up to 3, one step apart, front rank on the marker, direction snapped to 8 ways. Blocked cells move to the nearest free standable cell. Vanilla computes every stage's positions once, at ritual start.
- **Soldier role:** `RitualRole_ParadeSoldier` (RitualRoleColonist + not slave + capable of violence, other ideoligions allowed, max 20). The dialog only pre-fills career soldiers (soldier caste or shooting/melee specialists), or the top 3 fighters if there are none, by trimming `OrderByDesirability`, which only `RitualRoleAssignments.FillPawns` calls.
- **Arrival trigger:** vanilla `StageEndTrigger_RolesArrived` fires when one pawn of a role has the tag, so `StageEndTrigger_AllRolePawnsTagged` waits for all of them (skipping downed pawns and pawns that left).
- **Outcome:** `RitualOutcomeEffectWorker_FromQuality`, moods −6/−2/+6/+10 for 6 days. Quality from soldiers (custom comp, curve to 12 weighted), spectators (vanilla participant count, which excludes speaker and soldiers), speaker social impact, leader speaking, and a lectern within 5 cells.
- **Not done:** soldiers walk to their own end cells independently, so a column with mixed move speeds spreads out rather than marching in lockstep.

## Day off button (2026-10-25, not tested in-game)

`Source/DayOff.cs`. A "Day off" button in a new second row of Castes tab title buttons (`CastesTabExtensions.TopButtonsBelow`, right-aligned under Estates' "Colony management"). It sets every colony member's 24 timetable hours to Anything and saves the old ones in the `DayOff` GameComponent. Each pawn's schedule goes back at that pawn's next local midnight (`GenLocalDate.DayPercent(pawn)`). While a day off is active the button reads "End day off" and restores everything at once. Restoring only touches hours that are still Anything, so the player's edits during the day survive. Pressing it twice keeps the first saved schedule.

Performance: the schedule is written twice (click, midnight). `GameComponentTick` is one comparison against the earliest restore tick. A Harmony patch on `Pawn_TimetableTracker.CurrentAssignment` was rejected: that getter runs constantly. Custom Schedules only swaps allowed areas and never writes `times`, so it doesn't conflict. With Sleep On Schedule's default, Anything counts as sleep time, so pawns on a day off can nap whenever they're tired.
