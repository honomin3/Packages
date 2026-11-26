# Armature Scale Sync

Published by **honomin3**. The VPM repository display name is **honomin3's Packages**; the package display name remains **Armature Scale Sync**. The package ID is `dev.honomin.armature-scale-sync`.

Declare hierarchy scale on body bones and preserve outfit fit while synchronizing independent rigs. MA owns Merge Armature, Bone Proxy and Scale Adjuster behavior; NDMF owns the build and shared preview infrastructure.

**Unreleased 1.0.0 source candidate.** See the repository's Releases for published package archives. Source availability alone does not mean a version is available through VCC.

## Requirements and installation

- Tested Unity version: **2022.3.22f1**. Check the [VRChat documentation](https://creators.vrchat.com/sdk/upgrade/current-unity-version/) when setting up a project.
- MA **1.19.1** and NDMF **1.15.1** are the tested stable pair with VRChat SDK **3.10.6**. MA **1.19.0-alpha.3** / NDMF **1.15.0-beta.2** are also exercised as an internal development API profile; distribution requires the stable minimums above. Other versions in the declared package ranges require verification.
- A configured VRChat SDK3 Avatars project for SDK acceptance tests. Keep the exact installed SDK version with test evidence.

Install MA/NDMF through their official VPM repository. For a local source trial, add this directory as a local UPM package in an existing avatar project. This package does not install, activate or distribute Unity or the VRChat SDK.

## Authoring

1. Add **Hierarchical Bone Scale** to a body Armature or bone. Uniform mode uses one multiplier; its default is `1`. A parent and child declaration intentionally compound through the Transform hierarchy.
2. Add **Armature Scale Sync** to the avatar root. Auto Sync discovers independent rigs that need bindings. MA merged outfits, Bone Proxy objects and directly parented accessories receive their normal inheritance, with no additional per-outfit copy; their inherent MA/native behavior remains present when additional discovery is disabled.
3. For an independent clothing, hair or accessory rig, add a binding for each needed source-to-target correspondence. Bindings use MA's public object references. ASS applies only contributors missing from the target hierarchy: `target local scale × missing multiplier`. Existing target scale adjustments remain intact.
4. Categorize explicit bindings as Clothing, Hair or Accessories. Category selection filters additional delta sync. MA and native children still inherit their parent's scale; the tool cannot exclude an inherited child by category. Unknown independent rigs are diagnosed without guessing by bone names.

Attach HBS to body bones where possible. An HBS on an MA outfit bone can make MA retain it. The Inspector and build report issue a strong placement warning; this is not equivalent to putting a declaration on the body destination.

Advanced mode permits XYZ nonuniform multipliers and keeps its warning visible. Rotated descendants may shear. Negative scale, dynamics, constraints, different rig axis frames, ambiguous mappings and nonfinite/zero values receive diagnostics. Existing nonuniform source scale alone does not trigger the advanced-mode warning.

The Inspector shows avatar-wide diagnostics. Repeated Bone Proxy and dynamics warnings are summarized by code and count; expand **Show affected objects and reasons** for every original message and object. A single warning, all errors and advanced-scale warnings stay visible. Match Parent Scale being disabled may be intentional for an accessory: inspect placement before changing MA settings. Summarizing warnings does not change detection, preview or build behavior.

## Preview, animation and migration

Enable the Armature Scale Sync node in NDMF Preview and leave Preview Enabled on the root. Supported shared-shadow combinations update through NDMF's dependency monitoring without changing source transforms, meshes or clips. MA Scale Adjuster's mesh-only proxy remains distinct from HBS hierarchy scale.

For Bone Proxy, the candidate adds only the uniform hierarchy delta when the authoring pose already represents MA's placement baseline. This uses shared bones and the origin ledger; MA still owns placement. Unrepresented placement modes, nested routes and nonuniform/constraint cases receive an unproven-preview diagnostic and must be checked with **Inspect processed avatar…**. That window runs a fresh NDMF build on a clone in an isolated preview scene; rebuild its view after a configuration change. The fitted Keep World Pose fixture, including avatar-root scale and Auto Sync disabled, passed actual preview/build vertex comparison. Other placement baselines are not inferred from that result.

Scale animation correction is enabled by default. The build edits virtual clips after MA path remapping, multiplying values and finite tangents and preserving weighted/step curves. Source AnimationClips are not edited. Disabling correction or encountering an immutable marker clip produces a reset-risk warning. Actual SDK FX/Gesture and additive/reference-pose fixtures were tested. Native humanoid playback is compared with its MA-only baseline; these tests do not promise that Unity will animate a transform-scale curve it ignores without ASS.

Use **Migrate an existing transform scale…** only when converting an existing direct scale edit on a Scene or Prefab instance. Review current scale, reference scale and the resulting declaration, then select Migrate. This explicit operation supports Undo/Redo, composes an existing enabled declaration, stores instance overrides and rejects stale previews, Prefab assets, Prefab Mode contents and actual/pending Play Mode. Return to a Scene instance in Edit Mode before migrating. Repeating it without a new transform delta does nothing.

## Validation and limits

**Prefab Mode** uses an automatically rebuilt NDMF output in the existing Scene View. Changes and Undo/Redo trigger processing; the label indicates an update or failure. Native context surroundings remain visible. Picking returns authoring objects; use Hierarchy for unreadable meshes, unsupported primitives or custom shader surfaces. Synthetic native rendering, actual OS clicking, source invariance, lifecycle and bounded cleanup have been verified. This does not guarantee arbitrary avatar latency or every custom handle/shader. The separate processed-avatar window remains available for inspection.

The included synthetic Editor tests require `dev.honomin.armature-scale-sync` in the Unity project's `testables`, together with the dependencies above. Run them through Unity Test Runner. Passing a synthetic fixture does not establish compatibility with every avatar or dependency version.

No runtime expression parameters, menus, networking or avatar scale controls are generated. Components contain authoring data and are removed from processed build clones. See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md).
