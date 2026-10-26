# Society Suite

A set of RimWorld 1.7 mods by Cashew about how a colony is organised: who belongs to which class, how people are paid, where they live, who guards the walls and who runs the estates.

Each mod is its own folder in this repository, laid out the way RimWorld expects (`About/`, `Assemblies/`, `Defs/`, `Languages/`, `Patches/`, `Textures/`, plus the C# project in `Source/`). To use one, copy its folder into `RimWorld/Mods/`.

Most features have not been tested in-game yet.

## The mods

| Mod | Folder | packageId | Needs |
|---|---|---|---|
| Society: Castes | `Commoners` | `cashew.commoners` | Harmony |
| Society: Wages | `CommonersWages` | `cashew.commoners.wages` | Harmony, Society: Castes, Cash Register (Continued) |
| Society: Homes | `SocietyHomes` | `cashew.society.homes` | Harmony, Society: Castes, Society: Wages |
| Society: Estates | `SocietyEstates` | `cashew.society.estates` | Society: Castes |
| Society: Guards | `GuardPosts` | `cashew.guardposts` | nothing, works on its own |

The folder names, packageIds and namespaces are older than the "Society" names and are kept on purpose: saves store class names, and mod settings files are named after the folders. Renaming them would break existing saves and settings.

### Society: Castes (`Commoners`)

A caste system: slave, commoner, citizen, soldier, artisan, scholar and noble. Colonists are citizens by default. Higher castes expect more from life but are more useful or more privileged.

- Commoners have lower expectations, no suppression or rebellion, but can't do Research, Art or Dark Study and stop learning at level 6 in most skills.
- Artisans master one craft (crafting, art, construction or cooking) and scholars one field (research, dark study or medicine): faster learning, better quality, stat bonuses, but they want good workshops and dislike menial work.
- Soldiers are a citizen-tier caste that learns Shooting and Melee faster.
- Nobles trade better, lift the mood of lower castes nearby, need impressive bedrooms and a "standing" need, and refuse menial work. One noble seat per 10 colonists; Empire titles count as nobility.
- Opinion and romance across castes, inherited caste for children, an Ideology "castes" issue, a Militarist meme and a military parade ritual.
- A Castes main tab for moving people between castes. Other Society mods add their own columns and buttons to it.

Works without DLCs; Royalty, Ideology, Biotech and Anomaly features turn on when those DLCs are active.

### Society: Wages (`CommonersWages`)

An internal economy for the castes.

- Payday every 5 days by default: every caste except nobles is paid from a payroll register (any cash register, or a Progression: Hospitality merchant box). Pay goes into the pawn's own savings, which they won't haul away or drop for a Combat Extended loadout. A bed-linked strongbox can hold savings.
- Rent on beds, lots (zones) that pawns can buy or rent, a "For sale" toggle on storage for food, clothes and drugs, and restaurant meals with Gastronomy.
- Citizenship (1500 silver) and noble seats (5000) can be bought with savings.
- Taxes per caste, withheld from wages, with debt and "forgive taxes" buttons.
- A per-pawn money log, a 15-day economy report and dev tools for testing.

### Society: Homes (`SocietyHomes`)

Colonists who own or rent a lot keep house.

- Mark storage on a lot as the household pantry. Only the household takes from it.
- When it runs low, an adult goes shopping at your for-sale storage and pays from their savings.
- Household members cook at a stove on their own lot, outside colony bills, and get a mood boost for home-cooked meals.
- They prefer recreation on their own lot.

### Society: Estates (`SocietyEstates`)

Royals (nobles and anyone with an Empire title) act for the colony. A "Colony management" button on the Castes tab opens a window:

- **Colony needs:** keep-on-hand levels with optional price caps and a silver reserve. When a trade caravan (or a RimMarket stall merchant) has something the colony is short of, the best-negotiating free royal walks over and buys it.
- **Colony sells:** royals sell anything above a "keep at least" level, with an optional price floor.

### Society: Guards (`GuardPosts`)

A Guard work type, with guard posts and patrol routes in four colours (Architect > Security).

- A Guards tab to assign posts and routes, with combat skill and (with Combat Extended) loadout columns.
- On-duty, off-duty and automatic duty, each with its own apparel policy and CE loadout.
- Per-guard shifts, a muster spot, "Muster guards", and a militia that can be called up, geared and drafted.

No Harmony and no Combat Extended reference: CE is reached by reflection when it's loaded.

## Load order

1. Harmony
2. Combat Extended, Cash Register (Continued), Gastronomy, Hospitality and other optional mods
3. Society: Castes
4. Society: Wages
5. Society: Homes
6. Society: Estates

Society: Guards can go anywhere. Each `About.xml` lists its dependencies and `loadAfter` entries, so RimWorld's mod manager will sort and warn as well.

Optional mods the suite works with: Combat Extended, Gastronomy, Progression: Hospitality, Build From Inventory, RimMarket, VIE: Memes and Structures (the Militarist meme's "Death: don't care"), Vanilla Skills Expanded.

## Building

The built DLLs are in each mod's `Assemblies/` folder, so you only need to build if you change the code.

- The projects target **net10.0**, for RimWorld 1.7 running on the Waylith engine. Install the .NET 10 SDK.
- Build each mod from its `Source/` folder with `dotnet build -c Release`. The output goes to `../Assemblies/`.
- The game DLLs are referenced by absolute path: `Assembly-CSharp.dll` and `UnityStubs.dll` from `C:\Program Files (x86)\Steam\steamapps\common\RimWorld`, and Harmony, Cash Register and Gastronomy from the Steam Workshop folder (`steamapps\workshop\content\294100`). If your game is elsewhere, pass `-p:RimWorldDir=...` and `-p:WorkshopDir=...`, or edit the paths in the `.csproj`.
- Wages, Homes and Estates reference the installed `Commoners.dll` (and Homes the installed `CommonersWages.dll`) in `RimWorld\Mods\`. Build in this order: Castes, then Wages, then Homes and Estates. Rebuild Wages whenever the `Caste` enum changes, because enum values are compiled in as constants, and rebuild Homes when Wages' public API changes.
- Wages accepts `-p:CommonersDll=<path>` to build against a `Commoners.dll` that isn't installed yet.
- A running RimWorld locks the mod DLLs. Build to another folder with `-p:OutputPath=<folder>` and copy the DLLs in after closing the game.

Castes, Wages and Estates each have a `DESIGN.md` with the design notes.
