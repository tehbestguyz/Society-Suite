using System.Linq;
using RimWorld;
using Verse;

namespace Commoners
{
    // Before scholars existed, Medicine was an artisan specialty. Its def now belongs to scholars, so
    // when a game loads, any artisan still holding a scholar specialty becomes a scholar in it.
    public class ScholarMigration : GameComponent
    {
        public ScholarMigration(Game game)
        {
        }

        public override void FinalizeInit()
        {
            int moved = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
            {
                if (!(pawn?.health?.hediffSet?.GetFirstHediffOfDef(CM_DefOf.CM_Artisan) is Hediff_Artisan artisan))
                {
                    continue;
                }
                CM_SpecialtyDef specialty = artisan.specialty;
                if (specialty == null || specialty.caste != Caste.Scholar)
                {
                    continue;
                }
                CasteUtility.SetCaste(pawn, Caste.Scholar, specialty, thoughts: false);
                moved++;
            }
            if (moved > 0)
            {
                Messages.Message("CM_MessageScholarMigration".Translate(moved), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }
    }
}
