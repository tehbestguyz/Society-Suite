# Society: Estates — design notes

A new Society add-on (`cashew.society.estates`, folder `Mods/SocietyEstates`). Royals have the run of the place, so they should actively work to better their holdings and act on behalf of the colony. It needs only Society: Castes (for `CasteUtility.IsNoble`, which counts colony nobles and Empire title holders) and uses Castes' existing `CastesTabExtensions.TopButtons`, so Castes needs no rebuild. Society: Wages isn't required.

Status (2026-10-21): Colony Needs built and switched on in the mod list (`ModsConfig.backup-before-estates.xml` is the list from before). Not tested in-game.

## First feature: Colony Needs (built 2026-10-21, not tested in-game)

The user's calls:
- It's a new add-on, not part of Wages or Castes.
- Each need is a **keep-on-hand** level ("Medicine: 30"). A royal buys enough to bring the colony back up to it at every merchant visit.
- **Visiting caravans first**, including RimMarket stall merchants. Orbital trade ships come later.
- The royal **walks over and trades**, followed by a letter listing what was bought.
- **Budget:** a silver reserve the royal never spends below (default 1000). Needs are bought in list order until the money runs out.
- **Who:** the best negotiator, meaning the free, awake noble on the map with the highest trade price improvement (Social).
- **Price cap:** each need can optionally say "pay at most X% of market value". It's off by default.

### Window

- A "Colony management" button by the Castes tab title (a `TopButton`) opens a window with tabs. It has one tab for now, "Colony needs".
- **Adding:** a search box filters every item a trader could sell (`ThingCategory.Item`, tradeable, not corpses), showing each with its icon. Clicking one adds it to the list.
- **Each line:** icon and name, "Keep on hand" (a number field), how many the colony has now, the optional price cap (a toggle and a %), move up/down (list order is buying priority), and remove.
- **At the top:** the silver reserve, and the colony silver the royals can spend right now.
- The list is per save, kept in a GameComponent.

### Buying

1. **Noticing a merchant:** a MapComponent checks every 250 ticks for a pawn that can trade now (`pawn.trader`, `CanTradeNow`, not hostile) and that the colony hasn't already traded with this visit (remembered by pawn ID until they leave).
2. **Whether anything is needed:** for each need, "on hand" is what the merchant counts as the colony's: stored items and items in the home area (`Tradeable.CountHeldBy(Transactor.Colony)`). Items in pawns' inventories or worn don't count. Nothing is done unless the merchant carries at least one needed item the colony is short of.
3. **Choosing the royal:** a noble who is a free colonist, spawned on the merchant's map, not downed, drafted, asleep or in a mental state, and allowed to trade with this merchant (`CanTradeWith(faction, traderKind)`). The one with the best `TradePriceImprovement` goes.
4. **The job:** a new job walks the royal to the merchant (touch) and has them talk for a few seconds. If they're interrupted, the MapComponent hands it out again on its next check, while the merchant is still here.
5. **The deal:** at the end of the job, `TradeSession.SetupWith(merchant, royal, giftMode: false)`. For each need in list order: wanted = keep-on-hand − on hand; the unit price is the merchant's buy price with the royal negotiating, skipped if it's over the cap. The count is the smallest of wanted, what the merchant has, and what the budget above the reserve allows. Set the counts on the tradeables, then `TryExecute`, then close the session. Bought goods arrive the vanilla way (placed near the royal), and haulers store them. The silver comes from colony silver the merchant can reach (stockpiles and the home area), the same as a trade you do yourself.
6. **Letter:** "Lady X bought 20 medicine and 40 steel from the caravan for $1,240". If nothing could be bought (too expensive, over the reserve), nothing is sent unless the merchant had something needed, in which case a short message says why.

### As built

Code is in `Source/`: `EstatesMod.cs` (mod, DefOf, the Castes tab button), `ColonyNeeds.cs` (the `ColonyNeeds` GameComponent, saved as `seNeeds`/`seReserve`, and `NeedsUtility`), `Window_ColonyManagement.cs`, and `RoyalTrade.cs` (the watcher, the deal and the job). There's one JobDef, `SE_TradeForColony`. No Harmony.
- **Watcher:** `MapComponent_RoyalTrade` runs every 250 ticks on player home maps, and does nothing while the needs list is empty or the player has a trade window open. Merchants already traded with this visit are saved (`seTradedWith`) and forgotten once they leave. A merchant with nothing to buy is checked again an hour later, in case the list or the silver changed.
- **Waiting for arrival (2026-10-25, user's request, not tested in-game):** the watcher doesn't send a royal until `HasArrived`: a RimMarket merchant whose duty is `RimMarket_TendStall` is within 2 cells of the duty's focus (the stall's merchant cell); otherwise the merchant is in the home area, or their lord is no longer in `LordToil_Travel`. Vanilla caravans camp just outside the colony (`LordToil_DefendTraderCaravan`) and never come in, so the camp counts. After 5000 ticks (two hours) since the merchant was first seen, the royal goes anyway. The first-seen ticks aren't saved, so the wait restarts on load.
- **Planning:** the watcher plans the whole deal before sending anyone, so a royal never walks over for nothing. It opens the trade session by setting `TradeSession`'s fields directly rather than calling `SetupWith`, which would post vanilla's "can't sell these items" message on every check.
- **Who:** `CanGo` requires a noble who isn't downed, drafted, in a mental state or asleep. They also can't be in a lord (ritual, caravan, party), on a player-forced job, in need of medical rest, eating, or unable to do Social. They must pass `CanTradeWith` (title permissions) and have somewhere to stand. The one with the best `TradePriceImprovement` goes.
- **The job:** `StartJob` interrupts what the royal was doing, and resumes it afterwards if it can be suspended. The royal walks to touch the merchant, or, if that can't be reached, to the nearest reachable cell within 2.9 (a RimMarket merchant behind a stall counter). They talk for 300 ticks with a progress bar. A merchant beside them is made to wait for it (`ForceWait`). Then the deal runs. If the player is trading by then, the merchant is tried again later.
- **The deal:** `SetCounts` buys, need by need in list order, the cheapest matching offers first. It skips offers over the cap (price vs the item's `BaseMarketValue`) and counts against a budget of colony silver above the reserve. A positive `ForceTo(count)` means the colony buys (`PositiveCountDirection` is Source in a trade). Then `UpdateCurrencyCount`. The colony's post-deal silver is checked before `TryExecute`, because TryExecute's own check flashes the trade window's silver and would crash with no window open. If something was bought, a letter goes out; if not, a message.
- **On hand** is counted by `NeedsUtility.OnHand` itself, not from the deal's tradeables. A merchant only lists colony goods they'd buy, so counting from the deal would miss anything the merchant doesn't buy, and the royal would overbuy.

### Checked while building

- `CountToTransfer` sign: positive means the player buys (see above).
- RimMarket: stall merchants pass vanilla `CanTradeNow`. Its `TryExecute` patch only adds to market reputation and doesn't need a window. Its `GetPriceFor` patch adjusts prices during market visits, so royals pay market prices too.
- Payroll registers (`Building_CashRegister`) aren't `IHaulSource`, so their silver isn't the colony's trade silver and royals never spend it.
- The player's trade window: the watcher and the job both stand down while `TradeSession.Active`.

### Originally to check

- The sign of `CountToTransfer` for buying (positive or negative), in `Tradeable`/`Transferable`.
- Whether RimMarket's stall merchants pass `CanTradeNow` the same way caravan traders do.
- That payroll registers' silver stays out of the colony's trade silver. It sits inside the register, and the merchant only counts containers that are `IHaulSource`, so it should be safe, but check `Building_CashRegister`.
- What happens if the player opens the trade window with the same merchant at the same moment. The royal's deal runs in a single tick, so the two shouldn't overlap.

## Second feature: Colony sells (built 2026-10-25, not tested in-game)

The user asked to "set items to be sold here too", so that a royal sells them to a visiting merchant who buys them.
- **List:** `ColonyNeeds.sales` (saved as `seSales`) reuses the `ColonyNeed` line. Here `keep` means "keep at least" (default 0, sell everything), and the cap is a **price floor** (default 80% when ticked, off by default). It's checked against each item's own `MarketValue`, so quality and hit points count. An item can be on one list or the other, never both (`IsListed`).
- **Window:** a "Colony sells" tab drawn by the same code as Colony needs, switched by a `selling` flag. The search lists `NeedsUtility.SellableDefs` (`tradeability.PlayerCanSell()`). The Have count turns green when there's surplus to sell. Each tab keeps its own list scroll. An "Only items we have" toggle (on by default, remembered while the game runs) limits the search to items the colony holds and shows how many each one has. The counts come from `NeedsUtility.AllOnHand`, a single pass over haulable things and `IHaulSource` contents using the same rules as `OnHand`, refreshed every 120 draws.
- **The deal:** `SetCounts` now plans sales first (`SetSales`), then purchases (`SetBuys`), with a budget of colony silver plus sale earnings minus the reserve. For sales, each line in list order sells the surplus (Have − keep), worst pieces first (lowest `MarketValue`), skipping anything under the floor. A negative `ForceTo` means selling, and the earnings are `CurTotalCurrencyCostForDestination`. Sales are capped by the merchant's silver, because vanilla `TryExecute` calls `LimitCurrencyCountToFunds`, which would quietly pay less. Before `TryExecute`, neither side may end up below 0 silver.
- **Merchants only list what they'd buy,** so the trader-kind check comes free: `deal.AllTradeables` holds a colony item only if `TraderKind.WillTrade` covers it.
- **Letter:** "Royal sale", "Royal purchase" or "Royal trade", with Sold and Bought sections and the colony's silver after.
- The watcher now runs when either list has lines. A merchant with nothing to buy or sell is checked again an hour later, as before.

### Later ideas (not agreed yet)

- Orbital trade ships through the comms console (goods by drop pod, silver near trade beacons).
- Quality or material limits per need.
- A purchase history in the window.
