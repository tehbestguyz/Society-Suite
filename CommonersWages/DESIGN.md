# Commoners: Wages — design notes

An add-on for Commoners (`cashew.commoners.wages`). It requires Commoners, Cash Register (Continued) and Harmony. Gastronomy and Combat Extended support turns on automatically when those mods are loaded.

Pillar for v1: **money buys upgrades, never survival.** Free food, barracks and clothing still work exactly as before.

## The money loop

```
stockpile silver --(haulers: WorkGiver_StockPayroll)--> payroll register
payroll register --(commoner: JobGiver_CollectWages)--> commoner's pocket (savings)
savings --> Gastronomy restaurant register --(Cash Register "empty register" job)--> stockpile
wages / savings --(rent, citizenship)--> payroll register
```

## Ledger

`HediffComp_Wages` is added by XML patch to the `CM_Commoner` hediff, so the ledger lives on the pawn and goes away on promotion. It tracks:
- `owed`: wages earned but not yet collected
- `purse`: how much of the pawn's inventory silver is their own savings
- `owedSinceTick`, the last payday, and whether the "can buy citizenship" letter has been sent

Every 250 ticks the comp checks the pawn's local hour. On the first check at or after the payday hour each local day, it runs payday:
1. The daily wage is added to `owed`. Owed wages are capped at 10 days.
2. Rent is charged.
3. The commoner looks for a better rented room.
4. If savings reach the citizenship price, a one-time letter is sent.

## Payroll registers

`CompPayroll` is added by XML patch to every ThingDef whose `thingClass` is `CashRegister.Building_CashRegister`. That covers Cash Register's own register and Progression: Hospitality's merchant box (`Ferny_MerchantBox`). It provides:
- a "Payroll register" toggle
- a target amount (presets from 100 to 10,000; the default comes from settings)

`Building_CashRegister.ShouldEmpty` returns false for payroll registers, so Cash Register's empty job leaves them alone. `WorkGiver_StockPayroll` (Hauling work type) uses the vanilla `HaulToContainer` job to carry stored silver in until the register reaches its target.

## Collecting wages

`JobGiver_CollectWages` is appended to the priority sorter in `MainColonistBehaviorCore`. Its priority depends on the pawn's schedule:

| Schedule | Priority | For comparison |
|---|---|---|
| Joy | 7.5 | |
| Anything | 6.5 | Work is 5.5 |
| Work | 9.5, only once wages are a day late | Work is 9 |
| Sleep | 0 | |

The job walks to the nearest payroll register that has silver, waits 90 ticks, then takes `min(owed, silver available)`. The commoner gets the "payday" +3 thought and the register makes its ka-ching sound. A shortfall stays owed.

"Wages owed" is a situational thought that only applies while the pawn is spawned, so caravans don't count:

| Days owed | Label | Mood |
|---|---|---|
| Under 1 | none | 0 |
| 1–2 | wages late | −3 |
| 2–4 | wages unpaid | −6 |
| 4 or more | wages long unpaid | −10 |

## Protecting savings

The protected amount is `min(purse, silver in inventory)`. That calculation also registers spending automatically.
- **Vanilla:** the `Pawn_InventoryTracker.FirstUnloadableThing` getter handles caravan unloading, transport pods and "unload everything". A prefix moves silver to the end of the inventory list, so other items are found first. A postfix then caps the silver count it returns at the unprotected amount, or returns nothing.
- **Combat Extended:** a `Utility_HoldTracker.GetStorageByThingDef` postfix subtracts savings, so they're never counted as loadout excess. `GetAnythingForDrop` gets the same prefix and postfix treatment as vanilla.

## Spending

- **Gastronomy:** a `DiningUtility.HasToPay` postfix returns true for commoners. Gastronomy's own checks then apply: it only offers meals the pawn can afford, and gives its "bought food" thought. The payment goes into the restaurant's register.
- **Rent:** `CompRent` is added to every humanlike colonist bed def at startup. Hospitality guest beds are skipped.
  - The bed gizmo sets a daily rent (presets from 0 to 100), which applies to all selected beds.
  - On payday, rent comes out of today's owed wages first. That silver never leaves payroll.
  - Any remainder comes out of savings and is moved straight into a payroll register on the map.
  - If the commoner can't pay, they're evicted (−6 for 5 days).
- **Moving up:** on payday, a commoner whose owed wages plus savings cover `rent × buffer days` (5 by default) claims the most impressive unowned rented colonist bed that beats their current room. A commoner sharing a bed is never moved.
- **Citizenship:** a "Buy citizenship" gizmo on the commoner, through `HediffComp.CompGetGizmos`. The job runs at a payroll register:
  1. Collect any owed wages.
  2. Pay the price.
  3. `CasteUtility.Promote` without the normal promotion thought; instead the pawn gets "bought my citizenship" (+12 for 15 days).
  4. Set `UnloadEverything`, so leftover savings go to the colony's stores.

## v2: wages for every caste (built 2026-10-16, not tested in-game)

As built, compared with the plan below:
- **Ledger:** the ledger is `CompWages`, added at startup to every humanlike race def. A `PayrollTicker` GameComponent runs payday every 250 ticks for all free colonists. The v1 `HediffComp_Wages` (and its XML patch onto `CM_Commoner`) is kept only so v1 saves load; the ticker moves its data onto `CompWages`.
- **Who's paid:** only adults (`EarnsWages` checks `DevelopmentalStage.Adult()`) in a paid caste.
- **When payday starts:** only once a player home map has a payroll register. Before that, wages don't build up, so a new colony isn't hit with "wages unpaid" right away.
- **Buying citizenship** now leaves the pawn's leftover savings with them, since citizens are paid too. Buying a noble seat clears the purse and owed wages, and runs `UnloadEverything`.
- **Seat check:** the "Buy a noble seat" gizmo is disabled when no seat is free (`CasteUtility.CanChangeCaste` with `purchased: true`), and the job checks again at the register.
- **Commissions** come from the `CasteUtility.ArtisanMadeItem` event, and are only paid once a payroll register exists.
- **Settings:** the commoner wage keeps its v1 key, `dailyWage`.

### Added later on 2026-10-16 (not tested in-game)

- **Payroll view:** Commoners exposes `CastesTabExtensions` (columns plus summary lines). `PayrollView` adds Wage, Rent, Owed, Savings (with a pocket/strongbox tooltip) and Last paid columns. It also adds two summary lines: daily payroll and rent, and silver in payroll registers with days covered and the total owed. `CompWages` records `lastCollectedTick` and `lastCollectedAmount`.
- **Radius pull:** every 250 ticks, `PayrollTicker` tops each payroll register up to its target with stored, unforbidden, unreserved silver on the register's `Fields`, the cells inside its Cash Register radius. Haulers still cover silver further away. Controlled by the `registersPullSilver` setting.
- **Strongbox (`CW_Strongbox`, 30 steel, Furniture):**
  - It's a `CompFacility` that links to beds within 6 cells; `WagesStartup` adds it to every humanlike bed's `linkableFacilities`. It isn't storage: its silver sits in its own `ThingOwner`.
  - `CompWages.stashed` and `stashBox` track each pawn's share. A couple can share a box.
  - `JobGiver_TendSavings` runs in free time. It deposits anything above `pocketMoney` (50), moves savings left in an old box over, and tops the pocket back up when it falls under half.
  - Rent and purchases are paid from the pocket first, then from the box by transfer (`PaySavings`), so pawns don't have to walk to it.
  - Silver nobody has a claim on (its owner died or was ennobled) is dropped next to the box for the haulers.
  - Removing the box gives its silver to the colony and adds each owner's share to their owed wages instead.

- **Drop guard (`SavingsDropGuard`):** prefixes on both base `ThingOwner.TryDrop` overloads (the generic versions call them). They cap any drop of silver from a spawned, standing pawn's inventory at the unprotected amount, whichever mod or vanilla system asks for it. The first time it blocks a drop for a pawn, it logs a warning with the caller. The reported spill was most likely Gastronomy: a meal payment goes into the waiter's inventory as a separate stack, and if no restaurant register is within 50 cells, Gastronomy drops it at the waiter's feet. The guard leaves that silver alone, because it isn't savings.

- **Bugs found from the user's video and fixed:**
  - **Emptying loop:** Cash Register's `WorkGiver_EmptyRegister` emptied payroll registers onto the floor, and the radius pull refilled them from nearby storage. The JIT likely inlines the one-line `ShouldEmpty` getter, which skips that patch. `HasJobOnThing` and `JobOnThing` are now patched directly. Because the registers were always empty, collectors found nothing and their wages stayed owed.
  - **Pick Up And Haul unloading savings:** PUAH's `JobDriver_UnloadYourHauledInventory.FirstUnloadableThing` would return the pawn's whole merged silver stack, savings included. A postfix now splits the unprotected part off into its own stack and unloads only that, or drops silver from PUAH's list of hauled things if all of it is savings.
  - **Build From Inventory job loop (2026-10-21):** Build From Inventory (`Build_From_Inventory.UseInventory`) delivers construction materials from the builder's own inventory first, then other colonists'. Its haul job starts by dropping the stack, so for a silver build (a silver headstone) the drop guard refused, the job failed and the same savings were picked again, giving "started 10 jobs in one tick". Postfixes on `FindInOwnInventory` and `FindInOthersInventory` now skip silver the guard wouldn't let go of in full, so the builder fetches stockpile silver. A partly protected stack is skipped too, because Build From Inventory reserves the full job count on whatever it dropped. Smoke-tested against the real 1.7 DLL; not tested in-game.

  - **The root cause of both videos:** `WageUtility.TransferSilver` added up `ThingOwner.TryTransferToContainer`'s return value. That value is 0 whenever the silver merges into a stack already in the target container, so wages went unrecorded for any pawn already carrying silver. The silver had moved, but owed didn't go down and savings didn't go up. CE then dropped the uncredited silver, haulers took it to storage, and the radius pull put it back. The same bug hit rent, purchases and strongbox deposits. `TransferSilver` now measures what actually left the source container. This bug was already in v1, for commoners after their first payday.

### The plan as agreed

This goes with the five-caste plan in `Mods/Commoners/DESIGN.md`. Every caste except nobles is paid for the work they do. Nobles get nothing, because they effectively own the settlement.

| Caste | Daily wage (default) | Rent and Gastronomy | Can buy |
|---|---|---|---|
| Commoner | 25 | pays | citizenship (1500) or nobility (5000) |
| Citizen | 40 | pays | nobility (5000) |
| Artisan | 60, plus commissions | pays | nobility (5000) |
| Noble | none | free | — |

- **Ledger:** `HediffComp_Wages` can't stay on `CM_Commoner`, because citizens have no marker. Move it to a ThingComp, added at startup to every humanlike race def (the same way `CompRent` is added to beds). The comp only does anything while the pawn is a paid caste. Migrate the v1 hediff comp's data when a save loads.
- **Commissions:** artisans earn a bonus for each item in their specialty of excellent quality or better. Suggested defaults: excellent +10, masterwork +25, legendary +60. It's added to `owed`.
- **Ennoblement purchase:**
  1. A "Buy a noble seat" gizmo appears when savings plus owed wages reach the price and a seat is free.
  2. The job runs at a payroll register: collect owed wages, then pay the price.
  3. The pawn becomes a noble and gets a "bought my seat" thought.
  4. `UnloadEverything` runs, so leftover savings go to the colony's stores.
  - A one-time letter is sent when the pawn can afford it, the same as for citizenship. If no seat is free, the letter says so.
- **Nobles:** their purse is cleared on ennoblement. Rent beds are free for them, and the Gastronomy `HasToPay` postfix returns false.
- **Balance risk:** payroll scales with population (20 citizens at 40 is 800 silver a day). Rent and Gastronomy bring some of it back, but every wage needs to be a setting, and testing should check the silver flow.

## v3: land ownership (built 2026-10-16, not tested in-game)

The user's calls: it lives inside Wages; prices are appraised but can be overridden; pawns buy both by player order and on their own; the owner's whole family sleeps free.

- **Lots** are a zone, `Zone_Lot`, on the Zone menu after growing zones. Any indoor room a painted cell touches belongs to the lot, walls and furniture included. Outdoor cells only count where painted. `LotMapComponent` maps rooms to lots and refreshes every 250 ticks. If two lots touch one room, the lower zone ID keeps it, and the designator refuses to paint a room another lot already has.
  - Vanilla's `CheckContiguous` is skipped for lots (patched), so walls splitting a lot don't drop paid-for rooms.
  - Building on a lot's last painted cell doesn't delete the lot; only the player deleting or shrinking it does.
- **Price:** the appraisal is land (cells × `landPricePerCell`, default 10) plus `buildingValuePercent` (default 100%) of the market value of the colony's buildings on the lot, cached for 250 ticks. The zone gizmo can set a fixed price instead.
- **Who can buy:** citizens, artisans and nobles, adults only, and the lot must have a colonist bed. The money goes into a payroll register. Lots start out *not* for sale.
  - "Buy a lot" gizmo on the pawn, and "Sell to…" on the lot: the pawn walks to a payroll register (`CW_BuyLot`; the job carries the zone ID in `job.count`).
  - Autonomous buying (`autoBuyLots`, on by default): on payday, a paid citizen or artisan who owns no land buys the most impressive lot for sale they can afford, paid by note, if it's at least as good as their current room. With it off, a one-time letter is sent instead.
  - "Grant to…" gives a lot to anyone for free, any caste or age.
- **Owning:** on purchase, the owner (and a partner who shared their bed) moves into the best bed on the lot. Non-family pawns in its rent-free beds are moved out. Family is partner, parents and children (`LotUtility.IsFamily`).
  - Family sleeps free in any bed on the lot (`RentUtility.RentFor`).
  - Rent-free beds on an owned lot are private: an `IsValidBedFor` postfix keeps the AI from choosing them for anyone outside the family, unless they're already assigned to the bed.
  - On payday, family members move into a free bed on the family lot before looking at rented rooms, and never leave it for a rented one.
  - Mood: "homeowner" +4 while owning land, "bought my own home" +10 (15 days), "granted a home" +8 (15 days).
- **Rent to landowners:** when a bed's lot has an owner, a tenant's rent goes to the owner's savings. The wage part is paid out of the payroll registers, and the rest comes from the tenant's pocket and then their strongbox. It lands in the owner's strongbox if one is linked to their bed, otherwise in their pocket. `Zone_Lot.rentCollected` tracks the total.
- **Savings** are now kept by anyone in `WageUtility.KeepsSavings`: paid castes, nobles and landowners. That covers protection from unloading, the strongbox and the payroll view.
- **Losing a lot:**
  - When the owner dies, it passes to their spouse, then fiancé, then lover, then the oldest child, then a parent. Without an heir it goes back to the colony.
  - It also goes back to the colony if the owner leaves the faction or is enslaved. Arrested owners keep it.
  - "Repossess…" lets the colony buy it back at the asking price, paid from payroll into the owner's savings, or seize it ("my home was seized", −15 for 20 days). Deleting an owned lot counts as seizing it.
- **Payroll view:** a Home column, rent tooltips naming the landlord, a summary line for lots, and the payroll line splits out the rent that goes to landowners.

### Renting out lots (2026-10-17, not tested in-game)

The user's calls: a whole lot can be rented out, like a bedroom. An owner can rent out their lot, and an unowned lot can be set for rent instead of for sale, with rent paid into a payroll register. Rent is due every 15 days, and a tenant 2 days late is evicted. The code is in `Source/LotRent.cs` (`LeaseUtility`). The period and grace are constants there.

- **Listing:** a "For rent" toggle on every lot. On an unowned lot it's exclusive with "For sale". On an owned lot the player sets it for the owner. Turning it off stops new tenants but doesn't end a lease. After a lease ends, a lot that's still listed is offered again.
- **Rent:** by default `lotRentPercent` (10%) of the appraised value per 15 days, rounded to 10, minimum 10. It can be overridden from presets.
- **Who rents:** adult commoners, citizens and artisans (`EarnsWages`) on the lot's map. The owner's family can't rent, because they already live free. A payroll register must exist.
  - The pawn gizmo "Rent a lot" and the lot gizmo "Rent to…" sign straight away; nobody walks to a register.
  - With `autoRentLots` on (the default), on payday after auto-buying, a paid pawn with no home (no lot owned, rented or lived on by family) rents the best listed lot that beats their current room. They must have one period's rent now, and 15 days of wages must cover it.
- **Signing:** the first period is paid up front. Everyone outside the tenant's family gives up their bed on the lot, the owner included, and the tenant moves into the best bed. Renting another lot ends the old lease, and so does buying or being granted a lot elsewhere.
- **Living there:** `Zone_Lot.Resident` is the tenant, if there is one, otherwise the owner. Free family sleeping (`RentFor`), the private-bed check (`IsPrivateFrom`; on a rented lot *every* bed is private), `FamilyLot` and `BestBedOn` all use `Resident`. The owner of a rented-out lot has no home there, so they go back to renting rooms.
- **Paying:** `CheckLeases` runs every 250 ticks. Rent comes from unpaid wages first, then savings. On an owned lot it goes through `RentUtility.PayLandlord` into the owner's savings; on a colony lot the wage part stays in payroll and the rest goes into a register. If it can't be paid, the tenant is warned once, payment is retried, and the tenant is evicted at due + 2 days (`CW_Evicted` thought). Rent to the colony waits while the map has no payroll register.
- **Vacant lots on the market** (`Zone_Lot.OnMarket`: no owner, no tenant, listed for sale or rent): the user found a colonist sleeping free in a bed on a for-sale lot. Rent-free beds there are now private from everyone (`IsPrivateFrom`), and `CheckOwners` unclaims anyone in them every 250 ticks, player-assigned pawns included. Beds with a bed rent set still work as ordinary rented rooms. Unlisted colony lots are unaffected.
- **Endings:** the tenant dies, leaves or is enslaved; the player uses "End lease" (with an eviction thought); the lot is deleted; or the tenant buys, is granted or inherits the lot. A lease survives the lot changing owner (sold, granted, inherited, seized or bought back); the rent then goes to the new owner or the colony. Auto-buy skips lots rented by someone else. When a lease ends, the tenant's family leaves the beds and the owner moves back in.

### Citizenship needs a home (2026-10-17, not tested in-game)

The user's call: an adult commoner can only rise to citizen while they own or rent a lot. A citizen who later loses the lot is not demoted. `LeaseUtility.CanLeaveCommoners` checks this; living free on family land doesn't count, and children are exempt (the age-3 "raise as citizen" choice is unchanged). The setting `citizenshipNeedsHome` (on by default) turns it off.
- A Harmony postfix on `CasteUtility.CanChangeCaste` blocks any target above Commoner, so it covers the caste menu (citizen, artisan, noble) and buying a noble seat. It's a Wages patch, so Commoners needed no rebuild.
- Buying citizenship sets the caste directly, so the gizmo, the "can afford" letter and the register (checked again on arrival) each check it too.
- Since commoners can't buy land, in practice they rent a lot or are granted one.

### Food for sale without a restaurant (2026-10-17, not tested in-game)

The user wanted meals colonists buy without going through a Gastronomy diner. The code is in `Source/Market.cs`. The user's calls:
- **Marking:** a "Food for sale" toggle on storage. Stockpile zones get it from a `Zone_Stockpile.GetGizmos` postfix, and their IDs are kept in `MarketMapComponent`. Every `Building_Storage` def (shelves, fridges, modded storage) gets `CompForSale` at startup.
- **Price:** the item's market value × `foodMarkupPercent` (default 100%), paid from savings (pocket, then strongbox) into a payroll register.
- **Who pays:** commoners, citizens and artisans. Nobles and the colony's children eat free.

How it works:
- **Choosing food:** the food-search guard (`RestaurantStockGuard.cs`, now applied with or without Gastronomy) records the eater while `BestFoodSourceOnMap` runs. Unless the search is desperate, `IsForbidden` reports for-sale food as forbidden to anyone outside the colony (slaves, prisoners, guests, animals) and to paid pawns who can't afford one unit. Binges ignore forbidden things, so binging pawns pay if they can.
- **Paying:** a `StartJob` postfix opens a sale when an Ingest or FeedPatient job targets for-sale food for a paid eater, keyed by job `loadID`. A `Thing.Ingested` prefix/postfix counts how many were eaten and charges unit price × count. A starving pawn pays what they have. Sales are dropped after a day if the food is never eaten. Nothing is charged while the map has no payroll register.
- **Leaks plugged:** vanilla `JobGiver_PackFood` and CE's `JobGiver_UpdateLoadout` run with `MarketUtility.packing` set, so for-sale food is forbidden to everyone there and can't be carried off free. Hauling between stockpiles and cooking with stored ingredients are unaffected.

All 8 new or changed patches were smoke-tested against Assembly-CSharp, Commoners, CashRegister, Gastronomy and CE, including the citizenship `CanChangeCaste` postfix.

#### Goods for sale (2026-10-17, not tested in-game)

The user wanted the toggle to work for any storage, so colonists can buy anything from it. Their example was clothes allowed by a pawn's outfit policy. The toggle is now "For sale", and `IsForSale` accepts any item (`ThingCategory.Item`).
- **Personal searches:** `Patch_PersonalSearchGuard` sets the same `searchActive` and `searchEater` scope as the food search around `JobGiver_OptimizeApparel`, `JobGiver_TakeDrugsForDrugPolicy`, `JobGiver_SatisfyChemicalNeed` and `JoyGiver_Ingest`/`JoyGiver_SocialRelax` (`TryGiveJob` and `TryGiveJobInGatheringArea`). Inside it, `IsForbidden` hides for-sale goods the pawn can't take: non-members, or paid pawns who can't afford them from `SpendableSavings`. There's no starving exemption outside the food search.
- **Paying:** ingestibles pay through the existing Ingest path. Clothes pay in a `Pawn_ApparelTracker.Wear` prefix, which vanilla `JobDriver_Wear` calls while the item is still spawned on the shelf. That covers player-ordered wear too, and a pawn who's short pays what they have.
- **Price:** one markup for everything. It's still the `foodMarkupPercent` setting, kept so saved settings carry over; its label now says goods.
- Weapons aren't covered: vanilla pawns never pick up weapons on their own. CE loadouts and food packing still treat all for-sale goods as forbidden.

### Taxes (2026-10-17, not tested in-game)

The user wanted taxes "to pay for the roads": a per-caste amount set in mod settings, paid every 15 days. The user's calls:
- **Destination:** into a payroll register, or a stockpile when the map has none.
- **Unpaid tax:** becomes debt, with a mood penalty.
- **Schedule:** a colony-wide tax day on the first day of each quadrum (15 days) at payday hour.

The code is in `Source/Taxes.cs`.
- **Rates:** `taxCommoner` 30, `taxCitizen` 60, `taxArtisan` 100, `taxNoble` 0 per quadrum by default. Nobles default to 0 because they have no wages, only landlord rent, so a noble tax would mostly turn into debt. Only adult colony members pay.
- **Timing:** `TaxCollector` (a GameComponent, saved) is ticked from `PayrollTicker` after wages, so tax day can use the day's pay. In a game that didn't have it before, the first tax day is the next quadrum's.
- **Paying:** unpaid wages go first (that silver is already in payroll, so `owed` is reduced), then savings. Savings are paid by note into a payroll register on the pawn's home map. With no register there, silver is split off the pocket stack, then the strongbox, placed near the pawn or box, and moved to the best stockpile cell (`TryFindBestBetterStoreCellFor` with no carrier, after spawning).
- **Debt:** `CompWages.taxDebt` (saved) is added to the next tax day and collected each payday hour as money comes in. The `CW_TaxDebt` thought is −4 while owing. Debt shows in the inspect string, the payroll view's Tax column, and a summary line with the next tax day.

#### Debtors save up (2026-10-17, not tested in-game)

The user saw debt piling up and asked that debtors hold off spending to pay it off. In that save, every debtor earned $5 a day and paid $5 rent. Rent is charged from the wage at payday, before the debt sweep, so nothing was ever left for the sweep. Holding back spending alone wouldn't help, because the sweep already took everything else.
- **Spending:** `WageUtility.SpendableSavings` and `SpendableFunds` subtract tax debt. Food for sale (`MarketUtility.CanTake`), lot auto-buy and the "can buy" letters use them. A pawn in debt never auto-rents a lot or moves up to a rented room. Player-ordered purchases are left alone.
- **Moving up** also keeps the next tax bill (`TaxFor`) in reserve on top of `rentBufferDays` of rent, so a pawn doesn't upgrade straight back into debt.
- **Moving down:** `RentUtility.TryMoveDown` runs on payday after `TryMoveUp`, behind `debtorsMoveDown` (default on). It applies to a pawn in debt whose rent exceeds `AffordableRent` (wage minus the tax per day, rounded up). They move to the nicest free bed within that budget, or else the cheapest bed that still costs less than both their current rent and their wage. Couples sharing a bed stay put.
- **Bedless debtors (2026-10-17, not tested in-game):** the user's save had three debtors with no bed whose debt never fell. Vanilla claims any bed a pawn lies down in (sleep, nap, relax, watch TV). Each night they claimed a $5 barracks bedroll, and payday rent took the whole $5 wage. They were bedless again by mid-morning; what unassigned them wasn't found. The fixes: `RentUtility.DebtorCantAfford` makes `RestUtility.IsValidBedFor` reject unowned beds whose rent exceeds `AffordableRent` for a debtor, and `TryMoveDown` now also gives a bedless debtor an affordable bed.

#### Tax withholding and forgiveness (2026-10-17, not tested in-game)

The user still saw commoners stuck in debt. The save (wage $5, tax $30, so $75 of wages per quadrum) showed two causes. Payments weren't being lost: `purse` matched the pocket silver for every pawn, and only `TransferSilver` calls `TryTransferToContainer`.
- **Broke on tax day:** a pawn out of debt could spend all their savings on goods for sale, because nothing set the coming tax aside. Three days before tax day, most commoners had $0–35, and Komodo spent about $60 in those three days. They hit tax day with one day's wage, went about $25 into debt, paid it off over about five days, then did it all again the next quadrum. Fix: `TaxUtility.Withhold` runs each payday and moves part of the wage from `owed` to `CompWages.taxSaved` (saved as `cwTaxSaved`). The part is `ceil((TaxFor − taxSaved) / DaysToTaxDay)`, capped at what's owed, so pawns who start mid-quadrum catch up. The silver never leaves payroll. `TaxUtility.Pay` uses `taxSaved` first, then owed wages, then savings, so the daily debt sweep uses it too. A pawn who's no longer taxed (enlisted as a soldier, say) gets their `taxSaved` back as owed wages on their next payday.
- **Rent equal to the wage:** Fava (231 days since last collecting) and Spider each paid $5 rent on a $5 wage, so the debt grew $30 every quadrum, and with no free bed, `TryMoveDown` had nowhere to send them. Fix: for colony beds, the payday order is wage, then withholding, then rent, and `ChargeRent` lets off colony rent the pawn can't cover from savings, up to the amount withheld that day, instead of evicting them. Colony income doesn't change, since the colony gets that silver either way. Rent to a landowner still comes first (`RentUtility.PaysLandlord`), and withholding takes what's left of the wage.
- **Forgive taxes:** Commoners' Castes tab gained a button API (`CastesTabButton`, `CastesTabExtensions.RowButtons`, `GroupButtons` and `TopButtons`). Wages adds a "Forgive" button on each indebted pawn's row, a "Forgive taxes ($X)" button on each caste header, and a "Forgive all taxes ($X)" button by the title. Each button shows only while someone in its scope owes tax. The caste and all-colony buttons ask for confirmation first. `TaxUtility.Forgive` clears `taxDebt` and gives each forgiven pawn the `CW_TaxesForgiven` memory (+6 mood for 6 days). `taxSaved` isn't touched.
- Existing debts still clear only through the daily sweep. Big ones, like Fava's $178, take a long time, which is what the forgive buttons are for.


### Soldiers (2026-10-17)

A new Commoners caste at citizen standing (see Commoners' DESIGN.md "Soldiers"). They're paid at `soldierWage` 25, like commoners, and pay no tax. Enlisting from commoner doesn't need a home: `Patch_CasteUtility_CanChangeCaste` skips a target of Soldier, so military service is the other way out of the commoner caste.

#### Soldiers' beds (2026-10-17, not tested in-game)

The user asked to mark beds "free for soldiers", as an option under the existing rent button. The bed rent menu gained "Soldiers only (free)", which sets `CompRent.soldiersOnly` (saved as `cwSoldiersOnly`) and rent to 0. Picking any rent preset clears it. No Commoners API was needed, because `CasteUtility.GetCaste` already exists.
- **Free:** `RentUtility.RentOf` returns 0 for a soldiers' bed, so nobody is charged rent for it.
- **Soldiers only:** `RentUtility.SoldiersOnlyBlocks` (the bed is soldiers-only and the pawn isn't a Soldier) is checked in several places. The `RestUtility.IsValidBedFor` postfix blocks every sleeper, not just colonists; that check covers sleeping, napping and relaxing, which all claim the bed. A `CompAssignableToPawn_Bed.CanAssignTo` postfix covers the assign-owner dialog, showing "Soldiers only". It's also checked in `CanMoveInto` (moving up and down) and in `LotUtility.BestBedOn` and `MoveIn`, including a partner moving in too.
- **Unassigning:** switching a bed to soldiers-only unassigns any owner who isn't a soldier, with a message. `PayrollTicker` runs `CheckSoldiersBeds` every 250 ticks, so a soldier who changes caste gives the bed up.
- **Lots:** a soldiers' bed ignores lot privacy (`IsPrivateFrom`), and vacant lots don't turn its sleepers out. It follows the soldiers-only rule instead.
- **Inspect string:** "Soldiers' bed: free, and only soldiers may sleep here". The gizmo reads "Rent: soldiers".
- **Moving in (2026-10-17, not tested in-game):** the user saw Tharozz, a soldier, paying $5 for a bed in a plainer room (impressiveness 34) while free soldiers' beds sat empty in a nicer barracks (63). `TryMoveUp` skips beds with no rent, and vanilla never moves a pawn who already owns a bed. Now `TryMoveToSoldiersBed` runs on payday, after the family-home step and the couples check. It moves a soldier into the nicest soldiers' bed whose room scores at least as well as the rented bed they pay for, or more than 1 better than the free bed they have. A soldier who later moves up to a nicer rented room isn't pulled back.

### Couples share rented beds (2026-10-18, not tested in-game)

The user asked for couples to look for a double bed first when renting, with one partner renting it and the other sleeping there free. It's all in `Rent.cs`; no Commoners API was needed.
- **Pricing:** `RentFor` = `BaseRentFor` (the old rule: nobles free, family on their own lot free), then 0 if `SharingPartner` finds that the bed's renter is the pawn's lover, fiancé(e) or spouse (`LovePartnerRelationExists`). `RenterOf` is the first owner who'd be charged. Vanilla sorts a bed's owners by thing ID, so the renter is the lowest-ID paying owner, not whoever claimed first. All of this is worked out fresh on every call, so a breakup, or the renter leaving or dying, puts the other partner back on normal rent at the next payday. There's no saved state. A third, unrelated sleeper in a bigger bed still pays.
- **Payday room choice (`TryMoveUp`):** a pawn who already shares a bed with a partner only moves as a couple, to a clearly nicer empty double (`TryMoveInTogether`, needing a score above current + 1). A pawn whose partner sleeps elsewhere first tries joining the partner's bed (`TryJoinPartner`), then moving the couple into an empty double. That double must be at least as good a room as the worse of their two current rooms, and the partner only comes from a bed of their own, never a shared bed or their family's lot. Affordability is checked for whoever will actually pay (`RenterIfJoined`), using the same test as moving up. Joining a partner's bed for free is allowed even when the pawn is in tax debt. The couple steps come before the soldiers' bed step.
- **Soldier with a non-soldier partner:** the couple wins over the barracks. The soldier joins their partner's bed, or the two move into a double outside the soldiers' quarters. Once they share a bed, the soldiers' bed step isn't reached. Soldiers' beds stay soldiers-only, so a non-soldier partner can never join one. Two soldiers can share a soldiers' double, which is free anyway.
- **Auto-claim:** vanilla `RestUtility.FindBedFor` already tries the partner's bed first for a pawn with no bed, and our `IsValidBedFor` rules (soldiers only, privacy, what a debtor can afford) still apply. A debtor can always take a partner's rented bed, since it's free for them.
- **Display:** the bed's inspect string adds "Shared with partner (free): X sleeps here free, since Y pays the rent". The Castes tab Rent column shows "Shared", with a tooltip naming the paying partner and the rent.
- **Known gap:** if the renter is evicted for unpaid rent, only they lose the bed. The partner stays and becomes the renter at the next payday.

### Scholars (2026-10-17)

A new Commoners caste on the artisan tier (see Commoners' DESIGN.md "Scholars"). They're paid (`EarnsWages`) at `scholarWage` 50 a day with no commissions, and taxed `taxScholar` 80 a quadrum. Every text listing the paid castes now names scholars.

### Gastronomy dining check (2026-10-16)

The user saw no colonists come to an open, staffed and stocked restaurant. Gastronomy only sends a pawn to a restaurant from the normal hunger food search (a prefix on `FoodUtility.TryFindBestFoodSourceFor`), so a pawn has to get hungry while the restaurant is open. It skips the restaurant if the pawn is on shift as staff, if no stocked meal scores at least 50, or, for paying castes, if the pawn doesn't carry enough silver for the meal. Meals On Wheels and Hospitality only add postfixes there, so they don't block it. A dev-mode "DEV: Restaurant check" gizmo on colonists (`DiningCheck.cs`) runs these checks for one pawn, shows which one fails, and lists every mod patching the food search.

### Restaurant stock is for sale only (2026-10-17)

Gastronomy's stock is every unforbidden menu item inside a restaurant register's radius, and nothing protects it. Once the user allowed fine meals in the food policy, colonists took them off the restaurant's shelves for free whenever it was closed. With `restaurantStockForSale` (default on), `IsForbidden(Thing, Pawn)` reports restaurant stock as forbidden while `FoodUtility.BestFoodSourceOnMap` runs, unless the search is desperate (starving). A `[ThreadStatic]` flag is set in a prefix and restored in a finalizer. It covers everyone: colonists, prisoners, pets. Dining and serving don't use that search, so they're unaffected. Both methods are hundreds of bytes of IL, so the JIT won't inline them past the patches. `WillEat` (93 bytes) and `IsFoodSourceOnMapSociallyProper` (42 bytes) were rejected as hooks for that reason.

Also on 2026-10-17: Cash Register's `InternalDefOf` fields are `static readonly`, which .NET 10 won't let RimWorld bind, so `CashRegister_EmptyRegister` stayed null. A postfix on `WorkGiver_EmptyRegister.JobOnThing` fills in the def from DefDatabase. Def-less empty-register jobs had crashed StartJob and locked up the game.

### Testing tools, money log and economy view (2026-10-21, not tested in-game)

The shortlist from "Ideas" below, built the same day. No new Harmony patches. Society: Homes was rebuilt, because its grocery runs now write to the money log.

- **Money log (`Source/MoneyLog.cs`):** `CompWages.moneyLog` (saved as `cwMoneyLog`) keeps the pawn's last 40 `MoneyEntry`s: tick, `MoneyKind`, signed amount and a text detail (an item, a lot, the other pawn's name). Every place that moves a pawn's money calls `MoneyLog.Note` once, after paying, with what was actually paid: payday wage (after the owed cap), commissions, collecting, tax withholding and its refund, tax and tax-debt payments (`TaxUtility.Pay` now takes the kind), forgiveness, bed rent (colony, let off, to and from a landlord), lot rent (colony, to and from a landlord), goods for sale (food and clothes; `Sale` now carries the item label), citizenship, noble seats and what's left to the colony after one, lots bought and bought back, strongbox removal, Homes grocery runs, and the dev tools. Moves that are only bookkeeping (collecting, holding back for tax, let-off rent, forgiveness, strongbox removal) show without a sign. Pocket↔strongbox moves aren't logged.
  - The Castes tab Savings tooltip shows the last 10 moments, newest first. Entries from the same tick share a line, so a payday reads "+$25 wage, $2 held for tax, -$5 rent".
  - `MoneyKind` is saved by name, so kinds can be added anywhere but never renamed.
- **Economy ledger:** `EconomyLedger` (a GameComponent) keeps 30 days of colony totals by `ColonyFlow`, filled by the same `Note` calls. In: bed rent, lot rent, taxes, goods for sale, citizenship/seats/lots. Out: wages and commissions (counted when earned on payday), lots bought back. Also tracked, not in the net: rent between colonists (counted once, on the tenant's side) and tax forgiven. `ColonyFlow` is saved by position, so new flows go at the end. Gastronomy meals aren't counted, because Gastronomy pays them into the restaurant's register.
  - Castes tab: a summary line ("Last 15 days: $X in, $Y out, net $Z") and an "Economy" button by the title. Its tooltip is the full breakdown, and clicking it opens the same text in a window. Summary lines can't have tooltips, so the breakdown went on the button rather than changing Commoners' API.
  - On tax day, after collection, a "Quadrum economy report" letter with the last 15 days.
- **Dev tools (`Source/DevTools.cs`, dev mode only, English only like vanilla's):** the debug actions menu has a "Society: Wages" category: payday now (everyone; it doesn't move the real payday), tax day now (with the report letter), collect tax debts now, clear all tax debt (no mood), pay out all owed wages now, fill payroll registers to target (new silver), and economy report now. A "DEV: Wages..." button on the pawn offers: payday now, collect owed wages now (straight from the map's registers), give 100 or 500 silver as savings, clear savings (destroyed), clear tax debt (no mood) and show the full money log.
- **Wage and tax buttons:** each caste header on the Castes tab gets "Wage $25 · tax $30" (soldiers: wage only; nobles: tax only). It opens presets for each, with the current value marked. `WageUtility.SetWage` and `TaxUtility.SetTax` change the mod settings and save them (`Write()`), so it applies to every save. Header buttons are only given the caste's members, so an empty caste has no button; use mod settings for it. `WageUtility.WageFor(Caste)`, `IsPaidCaste`, `TaxUtility.TaxForCaste` and `IsTaxedCaste` were added for this, and `DailyWage`/`TaxFor` now use them.

### Payday every few days (2026-10-21, not tested in-game)

The user asked for payday every 5 days instead of daily, since a quadrum is 15 days. Their calls: wages and bed rent stay daily rates, paid and charged for each day since the last payday; everyone shares the same paydays; the gap is a setting.
- **Setting:** `paydayInterval` (default 5), a slider over 1, 3, 5 and 15 days. All of them divide the quadrum, so tax day (day 1) is always a payday. `WagesSettings.PaydayInterval` falls back to 5 for any other value.
- **Schedule:** `WageUtility.IsPaydayToday` is true when the local `DayOfQuadrum % interval == 0`, so with 5 that's days 1, 6 and 11, at payday hour. `CompWages.lastPaydayKey` still means "the last day already paid for", so `CheckPayday` pays `key - lastPaydayKey` days, capped at the interval. A pawn who becomes paid partway through a period is paid only for their days. Old saves carry on without losing or doubling a day.
- **Not paid, not counted:** while `EarnsWages` is false, `CheckPayday` resets `lastPaydayKey` to -1, so a pawn who becomes paid later (promoted, released, grown up) starts fresh instead of being paid for the gap.
- **Payday(days):** the wage is `DailyWage × days` (logged as "+$125 wage for 5 days"), and `RentUtility.ChargeRent` takes `days` and charges `RentFor × days`, wages first, as before. Moving rooms, auto-buying and auto-renting lots, and the "can buy" letters now happen on paydays only.
- **Owed cap:** `OwedCapDays` is the old 10 days or two paydays' worth, whichever is more, so a 15-day payday isn't cut short.
- **Withholding:** `TaxUtility.Withhold` spreads what's still due over the paydays left before tax day (`ceil(DaysToTaxDay / interval)`, today included) instead of the days left. With 5, the tax-day payday holds nothing back (last quadrum's withholding pays the tax), and the next two hold back half each.
- **Unchanged:** tax debt is still swept daily, lot rent is still due every 15 days, and commissions are still added right away.
- **Display:** the Castes tab payroll line adds "Paid every 5 days; next payday in 3 days". The dev "Payday now" tools pay a full period and don't move the real payday.
- **Watch for:** each payday pays out five days at once, so a payroll register on the old default target of 500 can run short. Collectors then take what's there, and the rest stays owed, with the "wages late" mood after a day. Raise register targets.

## Deferred

- Storefront integration. Its shopping only serves visiting guests. Colonists now buy clothes through "For sale" storage instead (see "Goods for sale").
- Casino.
- Survival mode, where commoners must buy food.
- Raises for individual pawns.

## Ideas (brainstormed 2026-10-21, nothing built)

The user's call: focus on testing tools and UI first (A and F below), because only wage collection is confirmed in-game. Everything else is noted for later. Ideas marked ⚠️ build on systems that are still untested (lots, leases, taxes, for-sale storage), so they should wait until those are tested.

### Shortlist

Built 2026-10-21; see "Testing tools, money log and economy view" above for what changed from these plans.

**A1. Dev tools (small).** Debug actions so a whole quadrum can be tested in minutes. Proposed list:
- Payday now, for one pawn or for everyone (runs the normal payday: wage, withholding, rent, moving up and down, auto-buy and auto-rent).
- Tax day now.
- Give a pawn silver as savings, or clear their savings.
- Clear a pawn's tax debt (the same as Forgive, but without the mood thought).
- Pay all owed wages instantly, skipping the walk to the register.
- Where (the user's call): both. Colony-wide actions go in vanilla's debug actions menu under a "Society: Wages" category (`[DebugAction]`), and one-pawn actions are "DEV:" gizmos on the pawn, like the existing "DEV: Restaurant check".

**A2. Money log (medium).** Each pawn keeps a list of their recent transactions: when, how much, what for, and who with (wages collected +$25, rent −$5 to Tharozz, bought a parka −$40, tax −$30, rent received +$5 from Fava).
- Proposed storage: a capped list (about 30 entries) on `CompWages`, saved with the pawn, so it survives reloading and can be read from a bug-report save.
- Proposed hook: the few functions all silver already goes through (`TransferSilver`, `PaySavings`, `PayLandlord`, the tax and sale code), each passing a reason.
- Display (the user's call): the Castes tab Savings column tooltip shows the last ~10 entries, under the existing pocket/strongbox split. No separate window or inspect tab for now.

**F1. Economy summary (small–medium).** A colony-wide tally of where the silver went: wages paid, rent collected (colony and landlords), taxes, for-sale sales, citizenship, nobility and lot purchases.
- Proposed storage: a saved GameComponent with daily totals for the last 15 days, filled from the same hooks as the money log.
- Display (the user's call): both. Castes tab summary lines ("Last 15 days: $X in, $Y out, net $Z") with a per-category breakdown in the tooltip, and a letter on tax day with the quadrum's totals.

**F2. Wage and tax settings on the Castes tab (small).** A group button on each caste header ("Wage $25 · Tax $30") that opens presets or a number field for that caste's wage and tax. It uses the existing `GroupButtons` API, so Commoners doesn't need a rebuild.
- The user's call: it edits the mod settings, so a change applies to every save, the same as editing them in mod settings. Per-save wages were turned down as a bigger change.

### Noted for later

New things to buy:
- **Pay-to-play recreation (medium):** a "Fee" toggle on recreation buildings (billiards, TV, Tiny Recreation Pack). Paid castes pay per use into payroll; nobles play free. Same pattern as "For sale". ⚠️ builds on the for-sale code path.
- **Furniture for their home (big):** packed-up furniture in for-sale storage; a lot owner buys it and installs it on their own lot. ⚠️ lots.
- **Commission an artisan (big):** a pawn orders an art piece or fine furniture, an artisan makes it as a bill, and the price goes to the artisan's savings.
- **Buy a relative's freedom (small):** a pawn pays to have an enslaved partner, child or parent raised to commoner.
- **Private hospital care (medium):** paid castes pay for a better medical bed or better medicine. Basic care stays free, so money still never buys survival.
- **Shopping at RimMarket stalls (big):** colonists spend savings at visiting caravans' stalls (`forti.rimmarket`, active). Needs a look at RimMarket's code.
- **Casino and Storefront for colonists (big):** already deferred above. Hospitality: Casino and Storefront are both active.

Ways to earn:
- **Hazard pay (small):** soldiers and called-up militia earn a bonus per day drafted or in combat (ties into Society: Guards).
- **Bonuses and raises (small):** a "Give bonus" button and a per-pawn raise on the Castes tab (raises were already deferred above).
- **Skill-based pay (small):** wages rise a little with the pawn's best skill.
- **Tips (small–medium):** Gastronomy waiters and Hospitality hosts keep a share of what guests pay.
- **Bounties (small):** silver for killing or capturing raiders, paid to whoever did it.

Ways to spend or save:
- **Inheritance of savings (small):** savings follow the lot-heir order (spouse, fiancé, lover, oldest child, parent). Today lots are inherited but savings aren't: a dead owner's strongbox share is dropped for the haulers, and their pocket silver stays on the corpse.
- **Interest (small):** strongbox savings earn a little interest each quadrum, paid from payroll.
- **Mortgages (medium–big):** buy a lot with a down payment and pay the rest each quadrum; missed payments mean repossession. ⚠️ lots, leases and taxes together.
- **Gifts (medium):** pawns buy for-sale items for a lover or close friend, which raises opinion. ⚠️ for-sale storage.
- **Tithes (small–medium):** donations at an Ideology altar raise ritual quality or count toward standing.

Social and caste effects:
- **Wealth thoughts (small):** "comfortably off" or "broke" moods from savings. Greedy pawns care twice as much, ascetic pawns don't care, and jealous pawns envy rich commoners.
- **Strikes (medium):** commoners stop working after long-unpaid wages until they're paid. Risky: a wage bug could stall the colony.
- **Servants for nobles (big):** a noble pays a commoner a stipend to keep their home (hauling and cleaning on the noble's lot).
