# Map Support Validation

This page records map-level promotion evidence for Game History support. A map is not supported just because its identity token is known; it needs live validation for map identity, lifecycle events, player stats, timing, and box events.

## Promotion Rules

Before a map can be added to the supported map table:

- The live base map token must be recorded.
- The live start-location token must be recorded when the map family uses one.
- `start_of_round`, `end_of_round`, and `end_game` must publish the expected Event Monitor records.
- Required Player Stats Read fields must be readable and plausible.
- Game Timing Read behavior must be acceptable for Game History durations.
- Box events must publish, and weapon alias capture must be recorded as present or absent.
- Open risks must be explicit.

## Die Rise

Status: validated
Support decision: Die Rise is ready to add to the supported map table as a base-map-token-only standalone identity.
Validation package: `../../.scratch/all-bo2-map-tracking/die-rise-validation.md`

Die Rise is the next standalone target from the remaining round-based Black Ops II Zombies map plan. The local Steam install contains `zone\all\zm_highrise.ff` and `zone\all\zm_highrise_patch.ff`.

Current evidence:

| Area | Status | Notes |
| --- | --- | --- |
| Target selection | confirmed | Die Rise was selected as the next standalone target and local fastfiles `zm_highrise.ff` and `zm_highrise_patch.ff` were present. |
| Map identity | confirmed | Lobby capture observed `ui_mapname=zm_highrise` while `mapname` was empty. Active-match captures observed `mapname=zm_highrise` and `ui_mapname=zm_highrise`. `ui_zm_mapstartlocation` was present as `rooftop`, but it did not vary and should not participate in standalone identity resolution. |
| Mode dvars | confirmed irrelevant | Active captures observed `g_gametype=zclassic` and `ui_gametype=zclassic`. `party_gametype` remained stale as `TranZit`, so it must not be used as a standalone discriminator. |
| Lifecycle events | confirmed | Die Rise published `start_of_round` value `1`, `end_of_round` value `2`, next `start_of_round` value `2`, `end_of_round` value `3`, next `start_of_round` value `3`, and two final `end_game` records through Event Monitor shared memory. |
| Player stats | confirmed | Current Game UI read plausible required stats during Die Rise: points changed `330` -> `1,080`, kills changed `4` -> `14`, headshots changed `2` -> `5`, and downs/revives remained readable at `0`. |
| Timing | confirmed | Current Game UI showed active plausible timing: round 1 `0:28` / `0:28`, later `1:30` / `0:45`, and post-game timers returned to `--:--`. |
| Event Monitor health | confirmed | Final shared-memory capture reported snapshot version `6`, compatibility state `2`, no dropped events, no dropped notifies, published notify count `7`, and event count `11`. |

Promotion result:

- Die Rise is ready for the implementation slice that adds a supported map identity from active `mapname=zm_highrise` to internal token `zm_highrise` and friendly name `Die Rise`.
- The resolver should not require a Green Run start-location token or mode discriminator for Die Rise.

Open risks:

- `mapname` was empty in the Die Rise lobby before spawn, while `ui_mapname=zm_highrise` identified the lobby target. Active-match `mapname=zm_highrise` is the promotion evidence.
- `party_gametype` remained stale as `TranZit` during the Die Rise validation run, so it must not be used as a standalone map discriminator.
- This validation does not promote other remaining standalone maps such as Mob of the Dead (`zm_prison`), Nuketown (`zm_nuked`), or Origins (`zm_tomb`).

## Buried

Status: validated
Support decision: Buried is ready to add to the supported map table as a base-map-token-only standalone identity.
Validation package: `../../.scratch/all-bo2-map-tracking/buried-validation.md`

Buried is the selected first standalone target because it is the earliest installed non-Green-Run standalone, round-based Zombies map on this machine. The local Steam install contains `zone\all\zm_buried.ff` and `zone\all\zm_tomb.ff`, but not the earlier standalone DLC fastfiles for Nuketown Zombies, Die Rise, or Mob of the Dead.

Current evidence:

| Area | Status | Notes |
| --- | --- | --- |
| Target selection | confirmed | Buried was selected from installed standalone candidates `zm_buried.ff` and `zm_tomb.ff`. |
| Map identity | confirmed | Active-match captures observed `mapname=zm_buried` and `ui_mapname=zm_buried`. `ui_zm_mapstartlocation` stayed present as `processing`, but it did not identify a submap and should not participate in standalone identity resolution. |
| Lifecycle events | confirmed | Buried published `start_of_round` value `1`, `end_of_round` value `2`, next `start_of_round` value `2`, and final `end_game` records through Event Monitor shared memory. |
| Player stats | confirmed | Current Game UI read plausible required stats during Buried: points changed `500` -> `1,430` -> `720`, kills changed `0` -> `6` -> `13`, headshots reached `6`, and downs/revives remained readable at `0`. |
| Timing | confirmed | Current Game UI showed active plausible timing: round 1 `0:14` / `0:14`, round 2 `1:07` / `0:04`, post-box `2:23` / `1:20`, and post-game timers returned to `--:--`. |
| Box events | confirmed | Buried published `randomization_done` and `user_grabbed_weapon` with `tar21_zm`, another `user_grabbed_weapon` with `frag_grenade_zm`, plus `closed` and `chest_accessed`; the UI rendered `tar21_zm` as MTAR. |

Promotion result:

- Buried is ready for the implementation slice that adds a supported map identity from active `mapname=zm_buried` to internal token `zm_buried` and friendly name `Buried`.
- The resolver should not require a Green Run start-location token for Buried.

Open risks:

- `mapname` remained stale as `zm_transit` in the Buried lobby before spawn, while `ui_mapname=zm_buried` identified the lobby target. Active-match `mapname=zm_buried` is the promotion evidence.
- `party_gametype` remained `TranZit` during the Buried validation run, so it must not be used as a standalone map discriminator.
- This validation does not promote other standalone maps such as Origins (`zm_tomb`).

## Farm

Status: validated
Support decision: Farm is ready to add to the supported map table.
Validation package: `../../.scratch/all-bo2-map-tracking/farm-validation.md`

Farm is the selected first non-Town target because it is a Green Run start-location variant and is the smallest expansion from current Town-only support. Existing map identity notes record Green Run as `mapname=zm_transit` plus `ui_zm_mapstartlocation`, with `farm` as an observed start-location token.

Current evidence:

| Area | Status | Notes |
| --- | --- | --- |
| Target selection | confirmed | Farm is the first validation target. |
| Map identity | confirmed | Active-match capture observed `mapname=zm_transit`, `ui_mapname=zm_transit`, and `ui_zm_mapstartlocation=farm`, deriving internal token `zm_transit_gump_farm`. |
| Lifecycle events | confirmed | Farm published `start_of_round`, `end_of_round`, next `start_of_round`, and `end_game` through Event Monitor shared memory. |
| Player stats | confirmed | Current Game UI read plausible required stats during Farm: points changed `1,420` -> `1,760` -> `810`, kills changed `11` -> `13`, and downs/revives/headshots remained readable at `0`. |
| Timing | confirmed | Current Game UI showed active monotonic timing: game time advanced `0:12` -> `3:46`, and round time advanced `0:12` -> `2:28`. |
| Box events | confirmed | Farm published box events. `randomization_done` and `user_grabbed_weapon` carried `saiga12_zm`; another `user_grabbed_weapon` carried `knife_zm`; `closed` and `chest_accessed` published without aliases. |

Prior live behavior:

- The 2026-05-10 Game History validation recorded a live Farm check where the app connected in a Farm lobby, BO2Monitor was compatible, round 1 started, the UI remained in the `Town required` state, and no Farm history entry was saved.
- That evidence proves the old unsupported-map guard held. It does not prove Farm is safe to promote.
- The 2026-05-14 live lobby continuation connected the packaged BO2 app to Steam Zombies Farm and confirmed `Monitor compatible`, `ui_mapname=zm_transit`, and `ui_zm_mapstartlocation=farm`. During the observed poll window, the app stayed in lobby state with `--:--` timers and no Event Monitor records.
- The 2026-05-14 live Farm completion captured active identity, lifecycle, Player Stats Read, Game Timing Read, box events with aliases, and `end_game`. Event Monitor shared memory reported snapshot version `6`, compatibility state `2`, no dropped events/notifies, and published notify count `9` by the final `end_game` capture.

Promotion result:

- Farm has been promoted in managed map identity and Game History recording policy. `ui_zm_mapstartlocation=farm` now resolves to supported internal token `zm_transit_gump_farm` with friendly name `Farm`.

Open risk:

- `mapname` can be empty/null in a Farm lobby; active-match validation should use the post-spawn value.

## Remaining Green Run

Status: completed
Validation package: `../../.scratch/all-bo2-map-tracking/green-run-validation.md`

Remaining app-relevant Green Run validation has not promoted any additional maps yet. A static pass on 2026-05-14 inspected the local Steam install at `C:\Program Files (x86)\Steam\steamapps\common\Call of Duty Black Ops II` and found Green Run fastfile candidates in `zone\all`.

Current target candidates:

| Target | Observed base map token | Observed start-location token | Mode discriminator | Status |
| --- | --- | --- | --- | --- |
| Bus Depot Survival | `zm_transit` | `transit` | `ui_gametype=zstandard`; `ui_zm_gamemodegroup=zsurvival`; `party_gametype=Survival` | runtime validated; promote only with mode-aware identity |
| TranZit | `zm_transit` | `transit` | `ui_gametype=zclassic`; `ui_zm_gamemodegroup=zclassic`; `party_gametype=TranZit` | runtime validated by prior live run; promote only with mode-aware identity |
| Diner | not applicable | not applicable | Turned-only | out of scope; do not promote for this app |

Open risks:

- Static fastfile names are not promotion evidence. Each map still needs live identity, lifecycle, Player Stats Read, Game Timing Read, box event, and weapon alias validation.
- The 2026-05-14 Bus Depot Survival live run observed `mapname=zm_transit`, `ui_mapname=zm_transit`, and `ui_zm_mapstartlocation=transit` in lobby, active rounds, and post-game. Lifecycle, stats, timing, box aliases, and `end_game` worked with no dropped counters.
- The 2026-05-15 TranZit re-check observed the same `zm_transit` / `transit` identity tokens, with `g_gametype=zclassic`, `ui_gametype=zclassic`, and `party_gametype=TranZit`.
- The 2026-05-15 Bus Depot Survival lobby and active round 1 captures observed the same `zm_transit` / `transit` identity tokens, with `g_gametype=zstandard`, `ui_gametype=zstandard`, and `party_gametype=Survival`.
- `ui_zm_gamemodegroup` is a type `7` enum dvar, not a string dvar. Its observed enum domain is `0=zclassic`, `1=zsurvival`, `2=zencounter`; Bus Depot Survival observed current index `1`, while the TranZit string-reader capture observed current slot `0x00000000`, consistent with index `0`.
- Bus Depot Survival and TranZit must not be promoted from static fastfile names or from `zm_transit` / `transit` alone. Issue 4 needs a mode-aware identity resolver before assigning the correct friendly name.
- `bus_depot` is only a previous test placeholder. `busstation` was a static fastfile hypothesis, but it was not the observed dvar value for the live Bus Depot Survival run.
- Diner is Turned-only and not relevant to this Game History app, so the `zm_transit_gump_diner.ff` static fastfile candidate is not a support target.
