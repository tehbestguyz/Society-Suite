using System.Collections.Generic;
using System.Linq;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SocietyEstates
{
    // A purchase or sale the royal plans to make: so many of one tradeable, for so much silver.
    public struct PlannedTrade
    {
        public string label;
        public int count;
        public float silver;
        public bool sold;
    }

    // Watches each map for visiting merchants. When one carries something on the needs list that the
    // colony is short of and can afford, it sends the best-negotiating royal over to buy it.
    public class MapComponent_RoyalTrade : MapComponent
    {
        // How often a merchant with nothing worth buying is looked at again (an in-game hour), in case
        // the needs list or the colony's silver has changed.
        private const int RecheckTicks = 2500;
        // The longest a royal waits for a merchant to arrive before going out to them (two in-game hours).
        private const int ArrivalWaitTicks = 5000;

        // Merchants the colony's royals have already traded with on this visit.
        private List<int> tradedWith = new List<int>();
        // Merchants checked and found to have nothing needed, until the tick they're checked again.
        private readonly Dictionary<int, int> checkedUntil = new Dictionary<int, int>();
        // When each merchant was first seen, for the arrival wait.
        private readonly Dictionary<int, int> waitingSince = new Dictionary<int, int>();

        public MapComponent_RoyalTrade(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            ColonyNeeds needs = ColonyNeeds.Get();
            // The player is in a trade window: TradeSession is theirs, so royals wait.
            if (needs == null || (needs.needs.Count == 0 && needs.sales.Count == 0) || TradeSession.Active || !map.IsPlayerHome)
            {
                return;
            }
            List<Pawn> merchants = map.mapPawns.AllPawnsSpawned.Where(RoyalTradeUtility.IsMerchant).ToList();
            // Forget merchants who've left.
            tradedWith.RemoveAll(id => !merchants.Any(m => m.thingIDNumber == id));
            foreach (int gone in waitingSince.Keys.Where(id => !merchants.Any(m => m.thingIDNumber == id)).ToList())
            {
                waitingSince.Remove(gone);
            }
            int now = Find.TickManager.TicksGame;
            foreach (Pawn merchant in merchants)
            {
                int id = merchant.thingIDNumber;
                if (tradedWith.Contains(id) || (checkedUntil.TryGetValue(id, out int until) && now < until) || RoyalTradeUtility.SomeoneOnTheWay(map, merchant))
                {
                    continue;
                }
                // Let the merchant get to their stall or into the colony first, so the royal doesn't walk
                // out to the edge of the map. One who never gets there is visited anyway after a while.
                if (!waitingSince.TryGetValue(id, out int since))
                {
                    waitingSince[id] = since = now;
                }
                if (!RoyalTradeUtility.HasArrived(merchant) && now - since < ArrivalWaitTicks)
                {
                    continue;
                }
                Pawn royal = RoyalTradeUtility.BestRoyal(merchant);
                if (royal == null)
                {
                    continue;
                }
                if (RoyalTradeUtility.Plan(merchant, royal).Count == 0)
                {
                    checkedUntil[id] = now + RecheckTicks;
                    continue;
                }
                RoyalTradeUtility.TryFindTalkSpot(royal, merchant, out IntVec3 spot);
                Job job = JobMaker.MakeJob(SE_DefOf.SE_TradeForColony, merchant, spot);
                royal.jobs.StartJob(job, JobCondition.InterruptForced, resumeCurJobAfterwards: royal.CurJob?.def.suspendable ?? false);
            }
        }

        public void NoteTraded(Pawn merchant)
        {
            if (!tradedWith.Contains(merchant.thingIDNumber))
            {
                tradedWith.Add(merchant.thingIDNumber);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref tradedWith, "seTradedWith", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tradedWith ??= new List<int>();
            }
        }
    }

    public static class RoyalTradeUtility
    {
        // A visitor who's open for trade right now, the same test as vanilla's "Trade with" option.
        public static bool IsMerchant(Pawn p)
        {
            return p.trader != null && ((ITrader)p).CanTradeNow && !p.mindState.traderDismissed;
        }

        // Whether the merchant has settled somewhere a royal can walk to without leaving the colony:
        // - a RimMarket merchant with a stall is standing at it (their duty points at the stall's
        //   merchant cell; checked by name so RimMarket isn't needed);
        // - anyone else is inside the home area;
        // - or their caravan has stopped travelling. Vanilla caravans camp just outside the colony
        //   and never come inside, so the camp counts as arrived.
        public static bool HasArrived(Pawn merchant)
        {
            PawnDuty duty = merchant.mindState?.duty;
            if (duty != null && duty.def.defName == "RimMarket_TendStall" && duty.focus.IsValid)
            {
                return merchant.Position.InHorDistOf(duty.focus.Cell, 2f);
            }
            if (merchant.Map.areaManager.Home[merchant.Position])
            {
                return true;
            }
            return !(merchant.GetLord()?.CurLordToil is LordToil_Travel);
        }

        public static bool SomeoneOnTheWay(Map map, Pawn merchant)
        {
            return map.mapPawns.FreeColonistsSpawned.Any(p => p.CurJobDef == SE_DefOf.SE_TradeForColony && p.CurJob.targetA.Thing == merchant);
        }

        // The free noble on the merchant's map with the best trade prices, who's awake, well and not
        // busy with something that matters more (a ritual, a caravan, an order from the player).
        public static Pawn BestRoyal(Pawn merchant)
        {
            return merchant.Map.mapPawns.FreeColonistsSpawned
                .Where(p => CanGo(p, merchant))
                .OrderByDescending(p => p.GetStatValue(StatDefOf.TradePriceImprovement))
                .FirstOrDefault();
        }

        public static bool CanGo(Pawn p, Pawn merchant)
        {
            if (!CasteUtility.IsNoble(p) || p.Downed || p.Drafted || p.InMentalState || !p.Awake() || p.IsBurning())
            {
                return false;
            }
            if (p.skills == null || p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
            {
                return false;
            }
            if (p.GetLord() != null || (p.CurJob != null && p.CurJob.playerForced) || HealthAIUtility.ShouldSeekMedicalRest(p))
            {
                return false;
            }
            if (p.CurJobDef == JobDefOf.Ingest || p.CurJobDef == SE_DefOf.SE_TradeForColony)
            {
                return false;
            }
            return p.CanTradeWith(merchant.Faction, merchant.TraderKind).Accepted && TryFindTalkSpot(p, merchant, out _);
        }

        // Where the royal stands to talk: next to the merchant if they can get there (`spot` is then
        // invalid), or else the nearest reachable cell within a few steps. A RimMarket merchant stands
        // behind their stall's counter, so customers talk across it.
        public static bool TryFindTalkSpot(Pawn royal, Pawn merchant, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            if (royal.CanReach(merchant, PathEndMode.Touch, Danger.Some))
            {
                return true;
            }
            Map map = merchant.Map;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(merchant.Position, 2.9f, useCenter: false))
            {
                if (cell.InBounds(map) && cell.Standable(map) && royal.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                {
                    spot = cell;
                    return true;
                }
            }
            return false;
        }

        // Opens a trade session with the royal negotiating, without vanilla's "can't sell" message,
        // which is about selling and would repeat every check.
        private static void Open(Pawn merchant, Pawn royal)
        {
            TradeSession.trader = merchant;
            TradeSession.playerNegotiator = royal;
            TradeSession.giftMode = false;
            TradeSession.deal = new TradeDeal();
        }

        // Works out what the royal would buy and sell, then closes the session again.
        public static List<PlannedTrade> Plan(Pawn merchant, Pawn royal)
        {
            Open(merchant, royal);
            try
            {
                return SetCounts(merchant.Map);
            }
            finally
            {
                TradeSession.Close();
            }
        }

        // Plans the whole deal on the open session: sales first, then purchases, and returns what it
        // comes to. Selling first means the merchant's silver can pay for what the colony buys.
        private static List<PlannedTrade> SetCounts(Map map)
        {
            List<PlannedTrade> plan = new List<PlannedTrade>();
            ColonyNeeds needs = ColonyNeeds.Get();
            TradeDeal deal = TradeSession.deal;
            Tradeable currency = deal?.CurrencyTradeable;
            if (needs == null || currency == null)
            {
                return plan;
            }
            float earned = SetSales(map, needs, deal, currency, plan);
            SetBuys(map, needs, deal, currency.CountHeldBy(Transactor.Colony) + earned - needs.reserve, plan);
            deal.UpdateCurrencyCount();
            return plan;
        }

        // For each line on the sells list, in order: whatever the colony holds above "keep", sold
        // cheapest first so the best pieces stay home. A royal only sells while the merchant still has
        // the silver to pay (vanilla would quietly pay less), and skips anything offered below its
        // price floor. Returns the silver the sales bring in.
        private static float SetSales(Map map, ColonyNeeds needs, TradeDeal deal, Tradeable currency, List<PlannedTrade> plan)
        {
            float merchantSilver = currency.CountHeldBy(Transactor.Trader);
            float earned = 0f;
            foreach (ColonyNeed sale in needs.sales)
            {
                int surplus = NeedsUtility.OnHand(map, sale.def) - sale.keep;
                if (surplus <= 0 || merchantSilver <= 0f)
                {
                    continue;
                }
                // Only what the merchant would buy shows up here: a merchant who doesn't deal in an
                // item doesn't list it at all.
                IEnumerable<Tradeable> lots = deal.AllTradeables
                    .Where(t => !t.IsCurrency && t.ThingDef == sale.def && t.CountHeldBy(Transactor.Colony) > 0 && t.TraderWillTrade)
                    .OrderBy(t => t.thingsColony[0].MarketValue);
                foreach (Tradeable lot in lots.ToList())
                {
                    float price = lot.GetPriceFor(TradeAction.PlayerSells);
                    if (price <= 0f || (sale.capped && price < lot.thingsColony[0].MarketValue * sale.capPercent / 100f))
                    {
                        continue;
                    }
                    int payable = Mathf.FloorToInt(merchantSilver / price);
                    int count = Mathf.Min(surplus, Mathf.Min(lot.CountHeldBy(Transactor.Colony), payable));
                    if (count <= 0)
                    {
                        continue;
                    }
                    // A negative count is the colony selling.
                    lot.ForceTo(-count);
                    float silver = lot.CurTotalCurrencyCostForDestination;
                    merchantSilver -= silver;
                    earned += silver;
                    surplus -= count;
                    plan.Add(new PlannedTrade { label = lot.LabelCap, count = count, silver = silver, sold = true });
                    if (surplus <= 0 || merchantSilver <= 0f)
                    {
                        break;
                    }
                }
            }
            return earned;
        }

        // For each need in list order: how many the colony is short of, bought cheapest first, as many
        // as the merchant has and the budget allows, skipping anything over its price cap.
        private static void SetBuys(Map map, ColonyNeeds needs, TradeDeal deal, float budget, List<PlannedTrade> plan)
        {
            foreach (ColonyNeed need in needs.needs)
            {
                int wanted = need.keep - NeedsUtility.OnHand(map, need.def);
                if (wanted <= 0 || budget <= 0f)
                {
                    continue;
                }
                IEnumerable<Tradeable> offers = deal.AllTradeables
                    .Where(t => !t.IsCurrency && t.ThingDef == need.def && t.CountHeldBy(Transactor.Trader) > 0 && t.TraderWillTrade)
                    .OrderBy(t => t.GetPriceFor(TradeAction.PlayerBuys));
                foreach (Tradeable offer in offers.ToList())
                {
                    float price = offer.GetPriceFor(TradeAction.PlayerBuys);
                    if (need.capped && price > offer.BaseMarketValue * need.capPercent / 100f)
                    {
                        continue;
                    }
                    int affordable = price > 0f ? Mathf.FloorToInt(budget / price) : int.MaxValue;
                    int count = Mathf.Min(wanted, Mathf.Min(offer.CountHeldBy(Transactor.Trader), affordable));
                    if (count <= 0)
                    {
                        continue;
                    }
                    // In a trade, a positive count is the colony buying.
                    offer.ForceTo(count);
                    float cost = offer.CurTotalCurrencyCostForSource;
                    budget -= cost;
                    wanted -= count;
                    plan.Add(new PlannedTrade { label = offer.LabelCap, count = count, silver = cost, sold = false });
                    if (wanted <= 0 || budget <= 0f)
                    {
                        break;
                    }
                }
            }
        }

        // Called when the royal reaches the merchant. Returns false if the trade couldn't happen now
        // (the player is trading), so the merchant is tried again later.
        public static bool TradeNow(Pawn royal, Pawn merchant)
        {
            if (TradeSession.Active || !IsMerchant(merchant))
            {
                return false;
            }
            Open(merchant, royal);
            List<PlannedTrade> plan;
            bool traded = false;
            try
            {
                plan = SetCounts(merchant.Map);
                Tradeable currency = TradeSession.deal.CurrencyTradeable;
                // Checked here because TryExecute's own check assumes a trade window is open. Neither
                // side may end up owing silver.
                if (plan.Count > 0 && currency.CountPostDealFor(Transactor.Colony) >= 0 && currency.CountPostDealFor(Transactor.Trader) >= 0)
                {
                    TradeSession.deal.TryExecute(out traded);
                }
            }
            finally
            {
                TradeSession.Close();
            }
            if (traded)
            {
                SendLetter(royal, merchant, plan);
            }
            else
            {
                Messages.Message("SE_MessageNothingBought".Translate(royal.LabelShort, merchant.LabelShort), new LookTargets(royal),
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
            return true;
        }

        private static void SendLetter(Pawn royal, Pawn merchant, List<PlannedTrade> plan)
        {
            List<PlannedTrade> bought = plan.Where(t => !t.sold).ToList();
            List<PlannedTrade> sold = plan.Where(t => t.sold).ToList();
            string text = "SE_LetterTradeIntro".Translate(royal.LabelShort, merchant.LabelShort, merchant.TraderKind.label);
            if (sold.Count > 0)
            {
                text += "\n\n" + "SE_LetterSoldSection".Translate(Lines(sold), Mathf.RoundToInt(sold.Sum(t => t.silver)).ToStringMoney());
            }
            if (bought.Count > 0)
            {
                text += "\n\n" + "SE_LetterBoughtSection".Translate(Lines(bought), Mathf.RoundToInt(bought.Sum(t => t.silver)).ToStringMoney());
            }
            text += "\n\n" + "SE_LetterSilverLeft".Translate(NeedsUtility.ColonySilver(merchant.Map).ToStringMoney());
            string label = bought.Count == 0 ? "SE_LetterSoldLabel" : sold.Count == 0 ? "SE_LetterBoughtLabel" : "SE_LetterTradedLabel";
            Find.LetterStack.ReceiveLetter(label.Translate(), text, LetterDefOf.PositiveEvent, new LookTargets(royal));
        }

        private static string Lines(List<PlannedTrade> trades)
        {
            return string.Join("\n", trades.Select(t => "  - " + "SE_LetterLine".Translate(t.count, t.label, Mathf.RoundToInt(t.silver).ToStringMoney()).ToString()));
        }
    }

    // The royal walks to the merchant, talks a moment, and buys what the colony needs.
    public class JobDriver_TradeForColony : JobDriver
    {
        private Pawn Merchant => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !RoyalTradeUtility.IsMerchant(Merchant));
            // B is a spot to talk from when the merchant can't be reached directly (behind a stall).
            if (job.GetTarget(TargetIndex.B).IsValid)
            {
                yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            }
            else
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            }
            Toil talk = Toils_General.Wait(300, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            talk.AddPreInitAction(() =>
            {
                // A merchant wandering the colony stops to talk; one at a stall is standing still anyway.
                if (pawn.Position.AdjacentTo8WayOrInside(Merchant))
                {
                    PawnUtility.ForceWait(Merchant, 300, pawn);
                }
            });
            yield return talk;
            Toil trade = ToilMaker.MakeToil("TradeForColony");
            trade.initAction = () =>
            {
                if (RoyalTradeUtility.TradeNow(pawn, Merchant))
                {
                    pawn.Map.GetComponent<MapComponent_RoyalTrade>()?.NoteTraded(Merchant);
                }
            };
            yield return trade;
        }
    }
}
