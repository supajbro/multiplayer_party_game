# Couch Guys — Seven-Day Suburbs Completion Plan

**Plan date:** 13 October 2026  
**Required-work budget:** 38 hours across seven development days  
**Milestone:** A polished, funny, replayable 20–30 minute Suburbs run for one human with AI teammates or up to four human players  
**Status:** Day 2 implementation in progress; SUB-01 and SUB-04 code/assets implemented, manual physics/multiplayer validation pending

## Instructions for Future Codex Sessions

1. Read this roadmap, then read **Current Development Handoff**.
2. Select the next unfinished task whose dependencies are complete. Do not restart the project-wide audit.
3. Inspect only the files named by that task, plus direct dependencies discovered while implementing it.
4. Implement the smallest solution that satisfies the acceptance criteria. Preserve the existing FishNet authority model and reuse existing prefabs, builders, ScriptableObjects, and runtime UI.
5. Run the task's automated checks and its stated Play Mode or multiplayer test. A compile-only result is not validation of physics or game feel.
6. Mark acceptance checkboxes only after observing them. Change the task heading from `[ ]` to `[x]` only when every acceptance criterion passes.
7. Record newly discovered issues under **Current Development Handoff**. Put non-blocking ideas in **Stretch Goals**, not the required schedule.
8. Update elapsed hours, remaining budget, relevant files, blockers, and the next recommended task before ending the session.

If a task exceeds its estimate by more than 25%, stop and record why. Prefer cutting cosmetic variants, tuning breadth, or optional edge cases before adding time. Do not silently expand the 38-hour required scope. Do not mark a task complete without validating its acceptance criteria.

## Current Development Handoff

- **Current development day:** Day 2 — SUB-04 validation.
- **Next recommended task:** Complete the `SUB-04` manual matrix in `Assets/Scenes/CouchPhysicsTest.unity`: all four anchors, 1–4 carriers/AI, loose couch loading, slopes, kerbs, spills, and remote-client occupancy/release.
- **Progress:** 0 of 14 required tasks complete; SUB-01 and SUB-04 implementations compile and the SUB-04 host scene starts, but their physics/Steam acceptance tests remain open.
- **Completed tasks:** Audit and roadmap creation. SUB-01 code changes compile. SUB-04 now has an authored `sled_01` network prefab, four server-validated grips, count-specific speeds, physical steering/sliding, AI assistance, out-of-bounds recovery, a repeatable $150 shop item, spawn registration, tests, prompt, and test-scene spawn.
- **Known blockers:** Physical feel, two-machine Steam behaviour, shop purchase edge cases, and late-join state still require manual Editor/hardware validation. The repository contains no gameplay audio clips.
- **Relevant starting files:** `SledController.cs`, `SledSettings.cs`, `Sled.prefab`, `PlayerCouchCarrier.cs`, `DebugCouchBotController.cs`, `CouchPhysicsTest.unity`, `DeliveryManagerShop.asset`, and `NeighbourhoodOutOfBoundsRecovery.cs`.
- **Outstanding testing:** Run the focused Edit Mode tests in the open Unity Editor, complete the SUB-01 manual matrix, then complete every SUB-04/SUB-05 slope/collision/cargo/shop/two-client check. The host smoke test reached FishNet server/client startup without sled exceptions.
- **Time-budget concern:** The plan has no spare feature capacity. Day 7's eight hours are the required test/fix reserve, not a source of time for new content. If the sled exceeds six hours, cut its shop polish or cosmetic variants—not networking, recovery, or couch-physics work.

## Concise Current-Game Assessment

This is a code-and-asset audit, not a claim that unobserved Play Mode behaviour is shippable.

| Area | Existing implementation to retain | Remaining milestone risk |
| --- | --- | --- |
| Couch carrying | A server-authoritative Rigidbody couch has four independent carry points, count-based speeds, conflicting-input efficiency, physical torque, kerb step assistance, slope forces, upright assistance, vehicle-impact torque, player bounce, three model variants, network occupancy, and out-of-bounds recovery. Editor physics tests and a dedicated couch test scene exist. | The richest requested behaviours already exist but still need feel tuning across 1–4 carriers, generated kerbs/hills, latency, and high-energy impacts. There is no general stuck/flip recovery for an in-bounds wedged couch. |
| Players and solo | Owner-controlled movement, stamina, health/respawn, weapons, animation parameters, and three server-driven AI teammates are present. AI uses NavMesh, follows a human, joins their couch, and fights thieves. | Solo end-to-end reliability is unproven. AI does not choose routes or initiate objectives, so it must consistently follow and assist without blocking loading, delivery, or recovery. |
| Thieves and combat | Three armed thief prefabs, health/death, formation movement, fair spread controls, tier-based group sizes, and couch-stealing behaviour exist. Up to two thieves can contest a couch and flee while others fight. | Spawning is periodic rather than authored per stage, and real-session fairness is unverified. Thieves could create unavoidable pressure, crowd delivery zones, or stall on generated NavMesh edges. |
| Vans | Three purchasable networked vans have distinct settings, fuel, health, collision damage, driver interaction, rear doors, secure couch loading/unloading, cargo recovery on destruction, and HUD prompts. | Driving, couch collisions, spawn clearance, flip/stuck cases, disconnects, and remote-client feel still need validation and tuning. |
| Delivery/progression/shop | The delivery manager is authoritative, has easy/medium/hard timed tiers, destination filters, currency, failure reset, seven replicated chapter stages, random unused destinations, HUD/map support, and a catalogue with three vans. | `RequiresVan` and `VanUnlocked` are not used to gate choices; any successful tier advances the chapter. The ending is only “Region complete,” and the tier economy can conflict with the intended seven-stage pace. These are completion-loop gaps, not a reason to rebuild the system. |
| Procedural neighbourhood | Deterministic progressive roads, terrain elevation, difficulty zones, destination metrics, roads/sidewalks, lots, houses, parks, landmarks, residential decoration, border colliders, recovery, and runtime NavMesh are implemented. | The ground, depot, mansion, and most neighbourhood-detail prefabs are still generated/placeholders. Several seeds must be checked for couch/sled/van clearance and visual composition. |
| Visuals/animation | Custom URP toon shader with shadows, rim/specular controls, toon material presets, authored player/couch/house/road/thief/van/frog/street-light models, bright environment settings, post-processing, weapon particles, recoil, camera shake, HUD, shop, and main menu exist. | The authored street-light model is not used by the placeholder street-light prefab; parks, fountain, statue, bins, mailbox, and depot remain conspicuously primitive. Carry poses are state-driven but grip alignment/readability is unverified. Delivery/purchase/couch feedback is sparse. |
| Audio | No project audio files or runtime `AudioSource`/`AudioClip` integration were found. | The game is effectively missing its audio feedback layer. External clips must be sourced or created separately; implementation must work with a small licensed placeholder/final clip set. |
| Multiplayer/stability | FishNet/FishySteamworks/Steamworks, deterministic seed synchronisation, server authority for couch/delivery/enemies/vehicles, solo local host, object registration, and disconnect release paths are present. | No evidence from this audit proves a complete remote-client run. Late join, disconnect during carry/drive/dialogue, shop duplication, and transport synchronisation are release-critical tests. Host migration remains out of scope. |

### Highest-Impact Conclusions

- Do not rebuild couch carrying, deliveries, procedural generation, vans, combat, HUD, or the shop. Their foundations are already present.
- Treat couch work as measured tuning and recovery, not a new physics architecture.
- Add one new transport only: a manually loaded, physically loose **sled**. Its uncontrolled downhill behaviour creates comedy without refactoring the van's secured-cargo path.
- Complete the existing seven-stage loop by enforcing stage intent and adding a clear final payoff.
- Replace only the placeholders most visible in normal play: depot, street lights, one park landmark set, and common roadside/garden props.
- Audio is the largest presentation gap; use a small event-driven set rather than an audio-framework project.

## Scope, Experience Target, and Cuts

The objective should be understood within one minute: buy/accept a couch job, choose hand carry, sled, or van, reach the marked house before time expires, earn money, and complete seven escalating deliveries. The run ends with an explicit Suburbs-complete celebration and “next neighbourhood unlocked” tease; implementing the next biome is not required.

### Required Transport Trade-offs

| Method | Required role | Trade-off |
| --- | --- | --- |
| Hand carry | Always available and flexible; best for doors, gardens, and final approaches. | Slow and resistant with one carrier; fast and more stable with cooperation; conflicting inputs rotate or stall it. |
| Sled | Purchasable low-cost shortcut for slopes and grass. Couch is placed physically on its bed rather than snapped into van cargo. | Fast downhill and easy to pull on gentle terrain; poor steering/braking at speed; couch can bounce or fall off but is recoverable. |
| Van | Existing long-distance option with secure cargo and three price/performance tiers. | Fastest on roads, but costs money, uses fuel, needs loading space, takes collision damage, and is awkward near houses. |

### Fun Chaos Versus Frustration

Preserve sled runaways, couch tipping, player disagreement, thief tug-of-war, vehicle impacts, bounce reactions, risky shortcuts, and last-second finishes. Remove permanent wedges, world fall-through, AI refusal, blocked unloading with no recovery, invisible objectives, duplicate charges, constant remote jitter, and failures caused by desynchronisation.

### Deliberately Excluded From the Required Week

- Furniture trolley/dolly; it is a stretch goal only.
- A second biome, biome transition, campaign save system, host migration, matchmaking overhaul, or networking rewrite.
- New enemy types, boss fights, melee combat, weapon expansion, revive-system redesign, or complex thief scripting.
- A general inventory/equipment framework, general quest/dialogue framework, or general audio middleware layer.
- Dynamic weather/day-night cycle, rendering-pipeline replacement, bespoke art for every placeholder, or full animation reauthoring.
- Fully physical couch joints inside vans; the stable secured-cargo implementation remains.
- More than one sled style, sled upgrades, or character-driven sled riding controls.

## Seven-Day Schedule

| Day | Focus | Required tasks | Hours | Expected result |
| --- | --- | --- | ---: | --- |
| 1 | Core physics and solo stability | `SUB-01`–`SUB-03` | 6.0 | Carrying is funny but controllable across party sizes; stuck recovery and solo assistance work. |
| 2 | Transportation variety | `SUB-04`–`SUB-05` | 6.0 | One networked, purchasable sled is complete and meaningfully different from vans. |
| 3 | Cooperative chaos | `SUB-06`–`SUB-07` | 4.5 | Thieves and high-energy collisions create readable recoverable disasters. |
| 4 | Complete loop and progression | `SUB-08`–`SUB-09` | 4.5 | Seven deliveries form a paced 20–30 minute run with a clear ending and useful economy. |
| 5 | Visual polish | `SUB-10`–`SUB-11` | 5.0 | Ordinary gameplay has a cohesive, screenshot-ready cartoon presentation. |
| 6 | Audio, UI, and feedback | `SUB-12`–`SUB-13` | 4.0 | Core actions have sound, impact, celebration, prompts, and purchase feedback. |
| 7 | Release validation and fixes | `SUB-14` | 8.0 | Solo and 1–4-player paths are tested; critical failures are fixed or the milestone is held. |
|  | **Total** | **14 required tasks** | **38.0** | Day 7 is a 21% dedicated validation/fix reserve. |

## Day 1 — Core Physics and Solo Stability

### [ ] SUB-01 Tune and Validate Core Couch Feel

**Implementation Status:** In progress — code and focused automated tests added on 13 October 2026; assemblies compile with zero warnings/errors. Unity Edit Mode execution, Play Mode feel testing, and remote-client validation remain pending, so this task is intentionally not marked complete.

**Priority:** Critical  
**Estimated Time:** 2.5 hours  
**Day:** 1  
**Relevant Files:** `Assets/_Project/Scripts/Gameplay/Couch/CouchCarryController.cs`, `CouchCarrySettings.cs`, `Assets/_Project/Settings/Gameplay/CouchCarrySettings.asset`, `PlayerCouchCarrier.cs`, `ThirdPersonPlayerController.cs`, `Assets/Scenes/CouchPhysicsTest.unity`, `CouchPhysicsTests.cs`.

**Current State:** Count-based speeds, conflict efficiency, torque, slopes, kerb assistance, impacts, bounce, and authoritative replication already exist. Defaults are 2/2.5/3/4 m/s for one through four carriers. Runtime feel and remote-client behaviour are not validated.

**Implementation Note (13 October 2026):** The attachment limiter previously reduced the world-space intent sent to the couch: inward pushing was scaled to 30%, while outward pulling near maximum separation could fall to zero. Player spacing is now constrained independently from couch intent. Point damping follows each carrier's target velocity rather than world zero, and horizontal force/torque are applied explicitly. A player must face the couch to grab it (0.35 local facing dot, 0.20 server-tolerant validation), then automatically faces the occupied grip while carrying. Configured 2/2.5/3/4 m/s speeds and networking authority are unchanged.

**Objective:** Make hand carrying enjoyable for 1–4 players while retaining weight, rotation, disagreement, downhill motion, and readable impacts.

**Implementation Steps:**

1. Use the existing test scene to record a small baseline matrix: flat start/stop, 90-degree turn, kerb, 20-degree hill up/down, opposing inputs, vehicle impact, player landing, and release for 1–4 carriers.
2. Tune existing constants/settings only where observation shows a problem: acceleration/braking, count speed, upright threshold, step lift, slope resistance, collision torque, and free-motion caps.
3. Keep the couch fully dynamic while carried and keep the server as the only Rigidbody authority. Do not add client prediction this week.
4. Add or adjust narrow editor tests for any pure calculation changed; document final values in `COUCH_CARRYING_IMPLEMENTATION.md` only if implementation changes them.

**Acceptance Criteria:**

- [ ] One carrier can complete flat and moderate-hill routes but is visibly slower and less stable than two carriers.
- [ ] Two to four cooperating carriers gain control/speed without making the couch rigid or weightless.
- [ ] Opposing inputs reduce translation and can rotate the couch; they do not cause explosive jitter.
- [ ] A couch crosses representative kerbs without repeated snagging and slides/tumbles downhill after release.
- [ ] Van hits and player landings produce noticeable bounded reactions; no tested case tunnels or exceeds the safety caps.

Automated/compile checks already completed:

- [x] Runtime and editor assemblies compile with zero warnings and zero errors.
- [x] Focused tests cover orientation-independent desired velocity, preserved intent under attachment resistance, intent-relative damping, mirrored off-centre torque, opposing-input cancellation, and grab-facing acceptance/rejection.
- [ ] Focused Edit Mode tests have been executed by Unity's Test Runner (blocked this session because another Unity instance owns the project).

**Dependencies:** None.  
**Testing:** Run editor tests, the full test matrix as host, then repeat two-carrier turning and release with one remote client at normal and simulated 150 ms latency.  
**Expected Player Experience:** Moving a couch is entertaining before objectives or enemies are involved; extra friends improve control while also creating opportunities for mistakes.

### [ ] SUB-02 Add In-Bounds Couch and Transport Recovery

**Priority:** Critical  
**Estimated Time:** 1.5 hours  
**Day:** 1  
**Relevant Files:** `NeighbourhoodOutOfBoundsRecovery.cs`, `CouchCarryController.cs`, `VanController.cs`, `PlayerVanInteractor.cs`, future `SledController.cs`.

**Current State:** Players and couches recover after leaving bounds or falling below the terrain, and van destruction unloads cargo. An upright in-bounds couch or transport can still become wedged, flipped, or inaccessible.

**Objective:** Provide one server-authoritative, cooldown-limited recovery action that restores the delivery object and nearby carriers without erasing ordinary funny crashes.

**Implementation Steps:**

1. Add a hold-to-recover request available only when speed is low and the object has been inverted, immobile, or unreachable for a short grace period.
2. Recover to the last valid grounded pose already tracked by `NeighbourhoodOutOfBoundsRecovery`, with overlap clearance and depot fallback.
3. Release or reposition occupants consistently; preserve the active delivery, timer, and ownership state.
4. Extend the recovery registry to the sled during `SUB-05`; do not add automatic resets for normal temporary tipping.

**Acceptance Criteria:**

- [ ] A wedged or inverted couch can be recovered within ten seconds without restarting the delivery.
- [ ] Recovery is denied while the object is moving normally or another player is actively controlling it.
- [ ] Recovered objects do not overlap terrain, houses, players, or vehicles.
- [ ] All clients observe one identical recovered pose and carrier state.

**Dependencies:** `SUB-01` for final motion thresholds.  
**Testing:** Force couch wedges against kerbs, fences, a van, a steep slope, and the border; test host and non-host requests plus repeated-request cooldown.  
**Expected Player Experience:** Disasters stay funny because recovery is quick, explicit, and cannot be abused to teleport a healthy delivery forward.

### [ ] SUB-03 Prove and Harden Solo AI Assistance

**Priority:** High  
**Estimated Time:** 2.0 hours  
**Day:** 1  
**Relevant Files:** `NeighbourhoodPlayerSpawner.cs`, `DebugCouchBotController.cs`, `DebugCouchBotSettings.cs`, `PlayerHealth.cs`, `EnemySpawnManager.cs`, `Region_Suburbs.asset`.

**Current State:** Solo spawns three server-driven NavMesh teammates that follow, join the human's couch, mirror carrying intent, and fight thieves. They do not initiate deliveries or drive, which is acceptable if the player remains the leader.

**Objective:** Ensure one human can finish the chapter with dependable assistance and no AI-created soft locks.

**Implementation Steps:**

1. Run a representative delivery with three teammates through turns, hills, combat, couch drops, and a van load/unload.
2. Fix only observed blockers: stale point reservations, unreachable carry-point retries, NavMesh reattachment, doorway/load-zone crowding, dead-leader selection, and combat-to-carry transitions.
3. Add a simple “regroup/release couch” command only if bots cannot otherwise be cleared from a blocked interaction.
4. Keep bots reactive to the human; do not build autonomous objective selection or driving.

**Acceptance Criteria:**

- [ ] Three AI teammates spawn exactly once in solo and remain on the NavMesh across tested seeds.
- [ ] At least one teammate joins the human's couch within five seconds when a reachable point exists.
- [ ] Bots release/reposition so the player can load/unload a van or sled and complete delivery.
- [ ] After combat or death, surviving/respawned bots resume following or carrying without a scene restart.

**Dependencies:** `SUB-01`.  
**Testing:** Complete one easy and one hard delivery solo on two seeds; deliberately drop the couch, enter a van, and let the leader die once.  
**Expected Player Experience:** Solo feels like leading three enthusiastic, imperfect helpers rather than fighting broken automation.

## Day 2 — One Polished New Transportation Method

### [ ] SUB-04 Build the Server-Authoritative Physics Sled

**Priority:** Critical  
**Estimated Time:** 3.5 hours  
**Day:** 2  
**Relevant Files:** `CouchCarryController.cs`, `CouchCarryPoint.cs`, `PlayerCouchCarrier.cs`, `NetworkSpawnablePrefabs.asset`, `Couch.prefab`; new `SledController.cs`, `SledSettings.asset`, `Sled.prefab`.

**Current State:** Implementation exists: `sled_01` is used by a dynamic, server-authoritative prefab with four synchronized grips, physical loose cargo, AI assistance, tuning settings, shop integration, spawn registration, recovery, and test-scene coverage. Manual feel and remote-client validation remain outstanding, so this task is intentionally not marked complete.

**Objective:** Add a simple sled players manually place the couch onto, then push/pull through server-authoritative physics.

**Implementation Steps:**

1. Create one low-poly/toon sled with a Rigidbody, compound bed/runners/low rails, FishNet `NetworkObject`/`NetworkTransform`, two interaction handles, and tunable friction/drag/steering.
2. Reuse the carrier contract and input flow where practical so players pull at physical points; keep the couch unparented and collision-enabled on the sled bed.
3. Tune for low resistance downhill/grass, limited uphill ability, speed-dependent steering, bounded velocity, and impacts capable of spilling the couch.
4. Register the prefab and add only essential prompt/occupancy state. Do not add seats, riding controls, upgrades, or multiple models.

**Acceptance Criteria:**

- [ ] A couch can be carried onto/off the sled without a snap or bespoke inventory action.
- [ ] One player can pull it slowly; two cooperating players improve control; conflicting players can fishtail it.
- [ ] The sled gains meaningful downhill momentum, is weaker uphill, reacts to kerbs/collisions, and can lose its couch.
- [ ] Server, host, and remote client agree on sled, couch, and handle occupancy without dual simulation.
- [ ] Speed/angular limits prevent unbounded launch or permanent high-frequency jitter.

**Dependencies:** `SUB-01`.  
**Testing:** Flat road, sidewalk, grass, 10/20-degree slopes, kerb impact, van impact, couch spill, one/two pullers, and a 150 ms remote client.  
**Expected Player Experience:** The sled is an immediately legible gamble: gloriously fast downhill, manageable with teamwork, and capable of turning a shortcut into a rescue mission.

**Implementation/validation note (13 October 2026):** Unity generated the prefab/settings/shop assets from `SledSystemBuilder`; the full solution builds with 0 errors and 0 warnings, required serialized references were created, and the test scene reached local FishNet host/client startup without sled exceptions. `SledPhysicsTests` and the extended catalogue test are compiled but still need to be run in Unity's Test Runner. No acceptance checkbox above is marked until hands-on physics and remote-client tests are observed.

### [ ] SUB-05 Integrate Sled Purchase, Objectives, AI Clearance, and Recovery

**Priority:** High  
**Estimated Time:** 2.5 hours  
**Day:** 2  
**Relevant Files:** `ShopPurchaseBehaviour.cs`, `ShopItemDefinition.cs`, `ShopCatalogue.cs`, `DeliveryManager.cs`, `DialogueShopUI.cs`, `DeliveryManagerShop.asset`, `NeighbourhoodOutOfBoundsRecovery.cs`, `DebugCouchBotController.cs`, `ShopCatalogueTests.cs`; new sled shop assets/behaviour.

**Current State:** The catalogue cleanly supports new `Equipment` items, but the existing purchase fulfilment is van-specific. Transactions are server-authoritative and duplicate-request protected.

**Objective:** Make the sled a meaningful, reliable shop purchase using the existing economy and UI.

**Implementation Steps:**

1. Add a narrowly scoped network-equipment purchase behaviour or sled purchase behaviour using the manager's existing clearance, authoritative charge-after-spawn pattern.
2. Price the sled below the light van so it is reachable early but still competes with saving for a van; begin tuning near $150.
3. Add concise shop copy explaining “fast downhill / weak uphill / couch not secured,” register the network prefab, and extend catalogue tests.
4. Include sled world-bound/stuck recovery, disconnect handle release, late-join state, and AI load-zone clearance.

**Acceptance Criteria:**

- [ ] One successful request spawns one networked sled and charges exactly once; blocked placement charges nothing.
- [ ] The sled appears under the existing shop Equipment category with clear trade-offs and affordability state.
- [ ] Disconnecting a puller frees only that handle; late joiners see the correct sled and couch state.
- [ ] Out-of-bounds or genuinely stuck sleds recover without advancing/resetting the delivery.
- [ ] Updated catalogue tests pass and the sled is registered in both solo and Steam spawnable configurations.

**Dependencies:** `SUB-02`, `SUB-04`.  
**Testing:** Purchase with exact/insufficient funds, double-click/retransmit, blocked spawn, host/non-host buyer, disconnect while pulling, late join, and border recovery.  
**Expected Player Experience:** Buying a sled is a tempting route decision, not a separate subsystem or a guaranteed upgrade.

## Day 3 — Cooperative Chaos and Gameplay

### [ ] SUB-06 Tune Thief Encounters Around Deliveries

**Priority:** High  
**Estimated Time:** 2.5 hours  
**Day:** 3  
**Relevant Files:** `EnemySpawnManager.cs`, `ThiefNavigator.cs`, `EnemyWeapon.cs`, `ThiefHealth.cs`, `SuburbsChapterDefinition.cs`, `DeliveryManager.cs`, thief prefabs.

**Current State:** Tier ranges are easy 1–2, medium 2–4, hard 4; thieves fight, steal available couch points, flee, and die. Groups spawn on a global timer, so pressure is not yet paced to delivery state/stage.

**Objective:** Turn thieves into occasional delivery complications rather than constant shooting or unavoidable failure.

**Implementation Steps:**

1. Gate normal spawning to active transport, suppress early tutorial pressure, and enforce a post-failure/post-respawn grace period.
2. Tune group frequency, accuracy, damage, tactical distance, and maximum active enemies against solo and four-player runs.
3. Preserve couch theft/tug-of-war; add timeout/repath/release handling only where thieves stall or drag the couch to an unreachable edge.
4. Clear or disengage enemies on delivery completion/failure so shop/depot time can breathe.

**Acceptance Criteria:**

- [ ] Stage 1 is enemy-free; later tiers escalate without exceeding the configured active cap.
- [ ] A solo player with bots can recover an unattended stolen couch without a forced restart.
- [ ] Thieves prefer contesting/attacking relevant carriers but do not all stack one carry point or fire through blocked sightlines.
- [ ] Completion, failure, and respawn create a safe grace period and leave no stale couch occupants.

**Dependencies:** `SUB-03`.  
**Testing:** Observe early, medium, and hard delivery encounters in solo and two-client sessions; include unattended couch, contested couch, thief death while carrying, and delivery completion during combat.  
**Expected Player Experience:** Players repeatedly choose between defending friends and rescuing furniture, with enough breathing room for the result to feel comic instead of punitive.

### [ ] SUB-07 Tune Cross-System Chaos and Collision Readability

**Priority:** High  
**Estimated Time:** 2.0 hours  
**Day:** 3  
**Relevant Files:** `CouchCarryController.cs`, `VanController.cs`, `VanSettings.cs`, sled files from `SUB-04`, `ThirdPersonPlayerController.cs`, `PlayerHealth.cs`.

**Current State:** Couch impacts/bounce, van collisions/damage, player knockback, and physical sled interactions can create emergent moments. Their combined energy, recovery, and readability are unvalidated.

**Objective:** Make vehicle/couch/sled/player collisions dramatic and survivable without adding scripted events.

**Implementation Steps:**

1. Test a compact collision matrix at low/medium/high speed and tune impulses, cooldowns, damage, angular response, and caps.
2. Ensure carried players release only at severe impacts; ordinary bumps should twist or displace the group rather than erase input.
3. Ensure van-versus-couch/sled contact uses ordinary server physics and never bypasses recovery or delivery ownership.
4. Leave visual/audio embellishment hooks for Day 6; do not build a new damage framework.

**Acceptance Criteria:**

- [ ] A moderate van hit visibly moves/tips a free couch or sled without tunnelling or exploding.
- [ ] Low-speed loading-area bumps do not repeatedly damage, launch, or detach everyone.
- [ ] Severe crashes can spill cargo, knock players, and damage vans, followed by recovery within ten seconds.
- [ ] Remote clients see one collision outcome with no persistent rubber-banding.

**Dependencies:** `SUB-01`, `SUB-02`, `SUB-04`.  
**Testing:** Record the collision matrix with host plus one client, then repeat the worst case with four player/bot bodies nearby.  
**Expected Player Experience:** Crashes generate clip-worthy chain reactions while preserving the possibility of a frantic comeback.

## Day 4 — Delivery Loop, Economy, and Ending

### [ ] SUB-08 Enforce a Clear Seven-Delivery Progression

**Priority:** Critical  
**Estimated Time:** 2.5 hours  
**Day:** 4  
**Relevant Files:** `SuburbsChapterManager.cs`, `SuburbsChapterDefinition.cs`, `SuburbsDeliveryStageDefinition.cs`, `DeliveryManager.cs`, `DeliveryDestinationQuery.cs`, `DeliveryTierConfig.cs`, `PlayerWeaponHud.cs`, `NeighbourhoodMap.cs`.

**Current State:** Seven named stages and completion replication exist, but any purchased tier can advance any stage; `RequiresVan`/`VanUnlocked` are effectively informational, and stage-specific route intent is not enforced.

**Objective:** Make the current seven-delivery run coherent, understandable, and finishable in 20–30 minutes without introducing a quest system.

**Implementation Steps:**

1. Extend each existing stage record with the minimum needed choice: allowed tier/route band, enemy permission, target time, and transport hint; reuse destination metrics and unused-destination tracking.
2. Restrict or recommend shop delivery choices by current stage so early jobs teach carrying, middle jobs reward sled/van use, and the final job uses the hardest valid destination with a deterministic fallback.
3. Make `RequiresVan` either an enforced “van available/owned” gate or rename its meaning to a transport recommendation; never allow a money soft lock.
4. Surface stage name, `n/7`, objective, and completion state in the existing HUD/map. Keep retry on the same stage after timer failure.

**Acceptance Criteria:**

- [ ] Seven successful deliveries are required exactly once; failure never increments progress or double-awards currency.
- [ ] Stages 1–2 select short/medium routes, stages 3–6 escalate, and stage 7 selects a hard valid destination with fallback.
- [ ] HUD shows the current named stage and progress out of seven; a new player can state the next action after viewing it.
- [ ] No stage can soft-lock because a van/sled was not purchased or was destroyed.
- [ ] A measured first-time run falls within 20–30 minutes after Day 7 tuning.

**Dependencies:** `SUB-05`, `SUB-06`.  
**Testing:** Automated state-transition/reward tests plus full runs on at least three seeds; test timer failure, repeat RPC, no eligible preferred destination, destroyed transport, and final completion.  
**Expected Player Experience:** The neighbourhood has a visible arc and ending while still allowing route and transport experimentation.

### [ ] SUB-09 Balance Currency, Purchases, and Final Payoff

**Priority:** High  
**Estimated Time:** 2.0 hours  
**Day:** 4  
**Relevant Files:** delivery tier assets, `SuburbsChapterDefinition.asset`, `DeliveryManagerShop.asset`, van/sled shop assets, `DialogueShopUI.cs`, `PlayerWeaponHud.cs`.

**Current State:** Easy/medium/hard jobs cost $0/$100/$250 and reward $100/$250/$500; vans cost $250/$500/$1000. Currency and purchase feedback exist, but the chapter pacing and completion payoff are not tuned.

**Objective:** Ensure earnings create real transport choices and a satisfying finish without grinding or irreversible mistakes.

**Implementation Steps:**

1. Spreadsheet-test the seven-stage earnings curve against sled and van prices, including one failed medium/hard attempt.
2. Tune costs/rewards so a sled is an early choice, a light/good van is achievable mid-run, and the best van is aspirational rather than required.
3. Add a clear completion panel/celebration with total earnings, time, deliveries, and “next neighbourhood unlocked” tease; a replay/restart action is sufficient.
4. Make failure messaging say what happened and that the same stage can be retried; do not add persistent campaign saving.

**Acceptance Criteria:**

- [ ] Players can finish with hand carry plus free retries; no purchase is mandatory for progression.
- [ ] At least two plausible spending paths exist: early sled then light van, or save directly for a better van.
- [ ] Purchases remain server-validated, charge once, and provide immediate readable confirmation.
- [ ] Final delivery produces an unmistakable completion screen and replay option on every client.

**Dependencies:** `SUB-08`.  
**Testing:** Simulate minimum/expected/max earnings paths; complete the chapter as host and client; verify duplicate purchase, insufficient funds, failure, and replay reset.  
**Expected Player Experience:** Money supports interesting group arguments and experimentation, then the run ends with closure rather than a silent HUD text change.

## Day 5 — Environment and Visual Polish

### [ ] SUB-10 Replace the Most Visible Environment Placeholders

**Priority:** High  
**Estimated Time:** 3.5 hours  
**Day:** 5  
**Relevant Files:** `Region_Suburbs.asset`, `NeighbourhoodGenerator.Environment.cs`, `NeighbourhoodEnvironmentAssetBuilder.cs`, environment placeholder prefabs, `street_light_01.fbx`, house/road/mansion models and prefabs, toon material presets.

**Current State:** Authored roads, houses, lighting configuration, environment placement, parks, landmarks, and decorations exist. The depot, ground/mansion assignment, and most detail prefabs remain simple generated primitives despite an authored street-light model being available.

**Objective:** Improve what appears in almost every screenshot without attempting to replace the full environment library.

**Implementation Steps:**

1. Replace the street-light placeholder visual with the existing authored model while preserving placement metadata/collider and keeping actual point lights disabled unless profiling allows them.
2. Build one cohesive reusable “hero park” kit from simple stylised geometry for fountain/statue/bench/flowers; update existing prefab references rather than generator rules.
3. Improve the depot silhouette/signage/colour and assign the existing mansion model for the final-destination category if its collider and drop point validate.
4. Harmonise road, sidewalk, grass, house, and prop palettes through existing toon materials; remove only the most obvious clipping/scaling issues found on three seeds.

**Acceptance Criteria:**

- [ ] No frequently encountered street light, fountain/statue, or depot focal point reads as an unstyled debug cube.
- [ ] Roads, sidewalks, lawns, houses, and props share the toon-shader palette and remain clearly separable at play distance.
- [ ] Delivery points, paths, kerbs, and driveable road edges remain readable after art replacement.
- [ ] Three generated seeds contain no systematic landmark/driveway/couch-clearance obstruction.
- [ ] Art changes do not change deterministic placement or NavMesh readiness.

**Dependencies:** `SUB-08` establishes final-route needs.  
**Testing:** Screenshot depot, ordinary street, hill, park, and final destination on three seeds; walk/drive/carry through each and run the existing environment validation runner.  
**Expected Player Experience:** The Suburbs looks intentionally bright and toy-like during ordinary play, not only in isolated character close-ups.

### [ ] SUB-11 Final Lighting, Shadows, and Character Readability Pass

**Priority:** Medium  
**Estimated Time:** 1.5 hours  
**Day:** 5  
**Relevant Files:** `CouchGuysToon.shader`, toon materials, `Region_Suburbs.asset`, `NeighbourhoodVolumeProfile.asset`, `QualitySettings.asset`, `CouchGuy.controller`, `CouchGuyAnimationDriver.cs`, player/couch/thief prefabs.

**Current State:** The toon shader supports realtime main-light shadows, ambient/baked light, rim light, specular, and additional lights. The Suburbs has bright sky/fog/post-processing settings and character movement/carry/weapon states.

**Objective:** Make players, couches, thieves, objectives, and slopes readable in gameplay shots at the target quality level.

**Implementation Steps:**

1. Tune sun direction, shadow distance/resolution, fog, colour grading, and material shadow/rim values using representative wide and close shots.
2. Verify carry/pickup/drop, walk/sprint, hit, and weapon reactions; adjust existing state blend/IK or carry-hand offsets only where hands/poses visibly fail.
3. Use colour/value contrast—not extra lights everywhere—to distinguish teammates, thieves, couches, destinations, and interactable transports.

**Acceptance Criteria:**

- [ ] Characters and the active couch remain readable against grass, roads, houses, and shadowed slopes.
- [ ] Realtime shadows are stable at normal gameplay distance with no severe acne, cutoff, or unacceptable frame cost.
- [ ] Carry transitions and locomotion do not visibly snap or leave hands in obviously broken positions in the tested views.
- [ ] Five representative unedited screenshots look cohesive and bright at gameplay camera distance.

**Dependencies:** `SUB-10`.  
**Testing:** Capture identical camera positions before/after; test low and high points of the generated terrain with 1–4 characters and profile GPU/frame time.  
**Expected Player Experience:** The action is attractive and easy to parse in motion, streams, and short clips.

## Day 6 — Audio, UI, and Feedback

### [ ] SUB-12 Add the Minimum Complete Gameplay Audio Layer

**Priority:** High  
**Estimated Time:** 2.5 hours for selection/import, engineering, and mix; bespoke clip creation or paid licensing is an external prerequisite and is not claimed in this budget  
**Day:** 6  
**Relevant Files:** couch, sled, van, player, thief, weapon, delivery, shop, and UI scripts/prefabs; new lightweight audio event component/config asset; `Assets/_Project/Audio/`.

**Current State:** No audio clips or runtime audio integration were found. Weapon visuals, camera recoil/shake, HUD events, and authoritative gameplay events already provide trigger points.

**Objective:** Give every frequent or important action audible feedback through one small reusable component and a limited clip palette.

**Implementation Steps:**

1. Source/create and import a legally usable small set: couch thud/scrape, sled slide/rattle, van engine/collision, footsteps/landing, three weapon shots, enemy hit/death, delivery success/failure, purchase/UI click, and one comedic heavy impact. Asset sourcing is external work and must be recorded honestly.
2. Add pooled or attached `AudioSource` playback with volume/pitch variation and impact cooldowns; use existing replicated events or local state changes so sounds do not double-play on host.
3. Add simple looping intensity for couch/sled scrape and van engine based on speed/contact; stop loops cleanly on unload, despawn, death, and scene change.
4. Mix for clarity: delivery/UI cues above ambience, impacts below weapons, and distance attenuation on world sounds.

**Acceptance Criteria:**

- [ ] Couch carry/release/impact/scrape, sled movement/impact, van engine/collision, weapons, enemy hit/death, delivery, purchase, and core UI actions have audible feedback.
- [ ] Host hears one event—not duplicated local plus RPC playback—and remote clients hear spatial events once.
- [ ] Continuous sounds start/stop without leaks and 15 simultaneous objects do not create unbounded sources.
- [ ] Important cues remain intelligible in a four-player combat/delivery mix without clipping.

**Dependencies:** `SUB-04`, `SUB-06`, `SUB-07`, `SUB-09`.  
**Testing:** Audio event checklist as host/client; spam impacts, purchases, shots, despawns, and scene replay while watching Audio Profiler/source count.  
**Expected Player Experience:** Weight, speed, collisions, success, and failure become immediately more tactile and funny.

### [ ] SUB-13 Add Focused Interaction and Celebration Feedback

**Priority:** High  
**Estimated Time:** 1.5 hours  
**Day:** 6  
**Relevant Files:** `PlayerWeaponEffects.cs`, `PlayerWeaponHud.cs`, `VehicleHud.cs`, `DialogueShopUI.cs`, `PlayerCouchCarrier.cs`, `CouchCarryController.cs`, delivery/shop scripts; existing particle prefabs/materials.

**Current State:** Weapons have particles, hit markers, recoil, and shake; HUD/shop/vehicle prompts exist. Couch interactions, denied grabs, purchases, and delivery completion have little visual feedback.

**Objective:** Make important actions readable with a few reusable cartoon effects rather than a broad VFX system.

**Implementation Steps:**

1. Reuse pooled particle techniques/materials for couch/sled dust, hard collision stars/puffs, and a short delivery-completion burst.
2. Add concise local feedback for available/occupied carry points, rejected interactions, successful purchase, load/unload, recovery availability, and timer urgency.
3. Apply small camera shake only to the affected local player for severe nearby impacts; cap frequency and never obscure aiming/carrying.
4. Verify colour-blind/readability through shape/text as well as colour.

**Acceptance Criteria:**

- [ ] Players can identify an available couch/sled handle and understand why an interaction failed.
- [ ] Hard impacts, successful loading/purchase/delivery, failure, and chapter completion each have distinct visual feedback.
- [ ] Effects remain legible but do not obscure couch position, crosshair, timer, or enemies.
- [ ] Pooled effects/source counts remain bounded during repeated impacts.

**Dependencies:** `SUB-05`, `SUB-09`, `SUB-12`.  
**Testing:** Trigger every feedback state locally and remotely, at HUD reference resolution plus 16:9 and ultrawide; stress repeated collisions.  
**Expected Player Experience:** Players instantly understand what happened, and successful or disastrous moments land with cartoon punch.

## Day 7 — Multiplayer Testing, Balancing, and Release Gate

### [ ] SUB-14 Execute the Release Matrix and Fix Critical Failures

**Priority:** Critical  
**Estimated Time:** 8.0 hours  
**Day:** 7  
**Relevant Files:** All files changed by `SUB-01`–`SUB-13`, `SteamLobbyController.cs`, `SteamClientBootstrap.cs`, `NeighbourhoodSeedSynchroniser.cs`, network prefabs, all three scenes, editor tests.

**Current State:** Architecture is strongly server-authoritative, but this audit found no evidence of a complete remote-client chapter playthrough. Day 7 is reserved for validation and fixes, not features.

**Objective:** Establish that the scoped Suburbs milestone is shippable for solo and 1–4 players, then use the remaining time only on Critical/High defects.

**Implementation Steps:**

1. Run all editor tests and a clean development build; fix compile errors, missing references, duplicate network IDs, and deterministic-generation validation failures first.
2. Run one full solo chapter with AI and one full two-machine Steam chapter. Use extra local clients/bots to exercise four-player carrying and load if four humans are unavailable, but do not claim four-human validation.
3. Test late join and disconnect while carrying, pulling sled, driving, loading, shopping, fighting, and completing a delivery. Verify authoritative state, released handles/driver, and no duplicate rewards/charges.
4. Profile representative dense streets and combat. Target stable 60 fps on the developer target PC, no sustained managed allocation from per-frame gameplay loops, and no server physics spike that disrupts input.
5. Fix release blockers in priority order: soft lock/data loss, network divergence, physics explosion/fall-through, progression/economy error, AI solo blocker, severe frame issue, then presentation defects.
6. Time the run and make data-only balancing changes to route bands, timers, rewards, enemy cadence, couch speeds, and sled/van prices. Do not add features.

**Acceptance Criteria:**

- [ ] Editor tests and a clean development build complete without errors.
- [ ] One human plus three AI teammates can complete all seven deliveries and immediately replay.
- [ ] Host and at least one real remote Steam client complete a full run with no persistent couch/sled/van divergence.
- [ ] Four participants can jointly carry, release independently, fight, buy, load/unload, and complete a delivery.
- [ ] Late join/disconnect cases leave no permanently occupied point, driver seat, shop lock, or delivery lock.
- [ ] Three seeds have reachable tier destinations, usable NavMesh, and viable couch/sled/van routes.
- [ ] A first-time run measures 20–30 minutes and no known Critical gameplay-blocking bug remains.
- [ ] Performance target is met or the measured exception and reduced scope are documented before release.

**Dependencies:** All required tasks.  
**Testing:** This task is the test matrix. Record build, seed, party composition, elapsed time, result, defects, and retest status in the handoff section.  
**Expected Player Experience:** Players can trust the rules enough to enjoy the chaos, recover from mistakes, finish the neighbourhood, and want another differently routed run.

## Dependency Order

`SUB-01 → SUB-02/SUB-03 → SUB-04 → SUB-05 → SUB-06/SUB-07 → SUB-08 → SUB-09 → SUB-10 → SUB-11 → SUB-12 → SUB-13 → SUB-14`

Parallel work is safe only where dependencies permit: `SUB-02` and `SUB-03`; `SUB-06` and `SUB-07`; and sourcing audio clips while Day 4–5 implementation proceeds. Do not merge competing physics edits without rerunning `SUB-01`'s matrix.

## Stretch Goals — Not Counted in the 38-Hour Target

Attempt these only after every `SUB-14` release criterion passes with time remaining.

- **Furniture trolley/dolly (6–10 hours):** Wheeled flat-road transport with high rolling efficiency, poor hills, tipping, manual couch placement, the same handle/authority/recovery/purchase patterns as the sled.
- **Sled cosmetic variants (1–2 hours):** Colour/material variations only; no stat tiers.
- **One extra hero prop set (2–3 hours):** Upgrade bus stop/gazebo/playground after the required park/depot pass.
- **Run summary records (2–3 hours):** Best time/earnings stored locally. Do not expand into campaign saving or online leaderboards.
- **Additional couch/transport audio variation (1–2 hours):** More clips after the minimum mix is complete.

## Definition of Done

- [ ] Couch carrying is reliable and enjoyable with 1, 2, 3, and 4 carriers.
- [ ] Conflict, rotation, slopes, kerbs, downhill sliding/tumbling, van impacts, and player bounce produce bounded physical comedy.
- [ ] The sled is polished, networked, purchasable, recoverable, and meaningfully different from hand carry and vans.
- [ ] All three existing vans remain purchasable, driveable, damageable, fuelled, and capable of loading/unloading couches.
- [ ] Thieves steal/contest couches, fight fairly, recover navigation, and do not overwhelm tutorial or recovery moments.
- [ ] Seven deliveries produce a clear 20–30 minute arc, meaningful earnings/spending, a final challenge, and an explicit ending.
- [ ] Generated routes/elevations remain varied and usable across at least three validated seeds.
- [ ] The depot, common streets, hero park elements, lighting, toon materials, characters, and objectives look cohesive in ordinary gameplay.
- [ ] Core movement, transport, combat, delivery, purchase, and UI actions have appropriate audio and visual feedback.
- [ ] Solo with three AI teammates can complete the chapter.
- [ ] Multiplayer state remains functional for up to four participants, with a real host/client full-run test and no known critical blocker.

## Final Playtest Questions

Release is blocked by a “No” to any of the first eight questions. The last two determine whether balancing/polish is sufficient.

- [ ] Can a new player understand the goal and accept the first delivery within one minute?
- [ ] Is moving a couch inherently enjoyable on flat ground, around a corner, over a kerb, and down a hill?
- [ ] Do multiple players make carrying more effective and more entertaining, including when they disagree?
- [ ] Are hand carry, sled, and vans meaningfully different choices with visible trade-offs?
- [ ] Can players create hilarious recoverable situations without a scripted set piece?
- [ ] Do ordinary streets, the depot, characters, objectives, and effects look appealing during normal gameplay?
- [ ] Is a typical delivery understandable and entertaining to watch without explanation?
- [ ] Can four participants complete a delivery without persistent networking, ownership, or physics failure?
- [ ] Does a full 20–30 minute run continue to introduce route, enemy, transport, and spending decisions?
- [ ] After the completion celebration, do players want to replay with a new seed or transport strategy?

## Final Scope Gate

If required work is behind schedule at the end of Day 4, preserve in this order: couch reliability, solo/multiplayer correctness, complete progression, sled correctness, recovery, audio cues, objective readability, environment cohesion. Cut in this order: extra prop replacements, animation refinements, sled shop cosmetics, nonessential effect variants, and fine-grained balance breadth. Never cut Day 7 network testing to save a stretch feature.
