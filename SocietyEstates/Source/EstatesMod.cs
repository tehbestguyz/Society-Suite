using Commoners;
using RimWorld;
using Verse;

namespace SocietyEstates
{
    public class EstatesMod : Mod
    {
        public EstatesMod(ModContentPack content) : base(content)
        {
        }
    }

    // Not readonly: on .NET 10 DefOf binding skips static readonly fields.
    [DefOf]
    public static class SE_DefOf
    {
        public static JobDef SE_TradeForColony;

        static SE_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SE_DefOf));
        }
    }

    public static class MoneyExtensions
    {
        public static string ToStringMoney(this int value) => ((float)value).ToStringMoney();
    }

    [StaticConstructorOnStartup]
    public static class EstatesStartup
    {
        static EstatesStartup()
        {
            // "Colony management" sits by the Castes tab title.
            CastesTabExtensions.TopButtons.Add(new CastesTabButton
            {
                width = 170f,
                label = _ => "SE_ColonyManagement".Translate().ToString(),
                tooltip = _ => "SE_ColonyManagementTip".Translate().ToString(),
                action = _ => Find.WindowStack.Add(new Window_ColonyManagement())
            });
        }
    }
}
