# Detailing components — version 1.2

All geometry is native Revit Rebar, RebarCoupler or StructuralConnectionHandler. This is a detailing assistant with explicit calculation scope, not a complete structural design engine. No density, material strength, splice percentage or steel connection capacity is inferred.

## Calculation profiles

`revit_calculate_rebar_detailing(design_json)` runs without Revit. All lengths are mm, stresses MPa, steel areas mm². Each result preserves inputs, edition, clauses, coefficients, minimum length, upward rounding to 5 mm and outstanding checks. Profiles cannot be mixed.

TCVN example (values are a numerical test case, not project defaults):

```json
{"standard":"TCVN5574:2018","operation":"Anchorage","steel_stress":"Tension","surface":"HotRolledRibbed","end_shape":"Straight","diameter_mm":16,"rs_mpa":350,"rbt_mpa":1.05,"as_required_mm2":201,"as_provided_mm2":201,"concrete_kind":"NormalWeight"}
```

- TCVN 5574:2018: clauses 10.3.5.4–5, 10.3.6.2 and 10.3.7. Non-prestressed bars only. Basic length `Rs*d/(4*eta1*eta2*Rbt)`. Required length includes area ratio and code minima; no confinement/hook reduction is applied. Lap requires explicit `spliced_percent`; diameters above 40 mm are rejected for lap. Unspecified diameter band between 32 and 36 mm is rejected. FineGrainedA anchorage requires `concrete_stress`; fine-grained lap is not implemented. Plain straight/L ends and bent compression anchorage are rejected in this bounded implementation.
- EN1992-1-1:2004: sections 8.3, 8.4 and 8.7. Ribbed, non-prestressed bars ≤32 mm, normal-weight concrete, static detailing only. Replace TCVN strength/area fields with `sigma_sd_mpa`, `fyd_mpa`, `fctd_mpa`, `bond_condition` (`Good`/`Poor`) and explicit `national_annex` design basis. All reduction factors alpha1–alpha5 remain 1.0. National-annex factored strengths must be supplied. The 2023 edition is not implemented.
- Other standards, seismic rules and prestressing are not enabled. Steel capacities under TCVN5575:2024/EC3 are not calculated by these tools. Unsupported codes are rejected instead of silently substituting another edition.

References: [official TCVN listing](https://tieuchuan.vsqi.gov.vn/tieuchuan/view?sohieu=TCVN+5574%3A2018); [European Commission JRC EC2 detailing workshop](https://eurocodes.jrc.ec.europa.eu/sites/default/files/2022-06/05_EC2WS_Arrieta_Detailing.pdf). The TCVN equations were checked against the full project-local standard; that copyrighted PDF is not distributed.

## Project and region settings

Use `revit_set_detailing_profile(profile_json)` and `revit_get_detailing_profile`. Settings are stored in the RVT through Extensible Storage and travel with the project. Setting a profile replaces the previous profile; read and merge it first when preserving other keys.

```json
{"support_layout":{"area_per_support_m2":1.0},"support_regions":{"S1":{"area_per_support_m2":0.8},"S2":{"spacing_x_mm":800,"spacing_y_mm":1000}},"steel_connection_rules":{}}
```

The values above only demonstrate syntax. Choose **m² per support OR both X/Y spacings**. Region settings override the project fallback; an explicit request layout overrides both. There is no invented universal number of chairs per square metre.

## Slab supports

`revit_create_slab_supports(request_json)` needs `host_id` (horizontal concrete Floor), `upper_rebar_id`, `lower_rebar_id`, `bar_type_id`, `zone_key`, `kind` (`PlanarChair`, `SpatialChair`, `ZigzagRail`), `seat_width_mm`, `foot_length_mm`, and `seat_depth_mm` for spatial chairs. Optional `zone_box_mm:[minX,minY,maxX,maxY]` uses project coordinates. Optional `layout` follows the profile syntax; `dry_run:true` validates then rolls back.

Seat elevations come from the actual selected upper/lower bar centreline and diameters. Net region area excludes floor openings. Density is a minimum count, rounded up; clipping and grid fitting can produce more supports. The response reports actual count and achieved m²/support. Spacing mode uses a cell-centred grid with steps no larger than the supplied values. Support footprint clearance is sampled at ≤25 mm; this is not a complete clash or construction-load check. Horizontal planar slabs only; crossing-layer obstructions need review.

Repeating a host/zone key rebuilds only this tool's stored supports. Referenced couplers, dimensions or tags prevent destructive replacement when reported as dependents. Element IDs can change on regeneration; review external references. The shape remains editable native Rebar. Spatial FreeForm supports retain the Revit constraint limitations described in native-modeling-policy.md.

## Independent end anchors and zigzag beam/stair paths

`revit_create_designed_rebar(request_json)` takes `host_id`, `bar_type_id`, `body_points_json` (array encoded as a string), `normal_x/y/z`, `design`, `start_anchor` and `end_anchor`. Body endpoints are the explicitly selected critical sections. Each anchor requires `enabled:true/false`, `style:Straight|Up|Down`, and `horizontal_embedment_mm` for bends. Use `Down` at both ends for top reinforcement bent down. Inclined intermediate body segments support stair/zigzag paths; Up/Down anchor terminal segments must be horizontal.

The tool verifies nominal bar diameter and selected bar type bend diameter. It accounts for real rounded 90° arc length. Cover, splitting, confinement and containment beyond the critical section remain project checks. Disabled anchors are explicitly flagged for design review. Supply `replace_component_id` and the changed anchor settings to reuse a stored recipe; this rebuilds only a managed bar and returns its new ID. Existing unrelated bars are never replaced.

## Lap and mechanical splices

`revit_create_rebar_splice(request_json)` requires `mode:ParallelLap|CrankedLap`, `host_id`, `bar_type_id`, `start_mm`, `end_mm`, `splice_center_mm`, `offset_direction`, `centerline_offset_mm`, `design` (including `spliced_percent`) and, for cranked bars, `crank_run_mm`. All points have three coordinates. It creates a new pair; it does not cut/delete existing beam bars. Crank rounding is placed before the calculated parallel overlap zone. Stirrups, stagger, allowable splice zones and fabrication slope are separate checks.

`revit_create_rebar_coupler` uses an actual loaded coupler `type_id`, two native Rebar IDs and end indices 0/1. Revit validates compatibility/end treatments; no generic cylinder fallback is used. Coupler capacity, product certification and mechanical end fabrication require the selected manufacturer's specification. A cranked lap models a bent offset bar, **not** a mechanically pressed/reduced bar end.

## I-section nodes

`revit_create_smart_steel_node(request_json)` needs `primary_id`, `secondary_id`, `node_tolerance_mm`. `analyze_only:true` is the default. Physical solid section centroids at 25%/75% define axes, so this does not assume a bounding-box or insertion-point centre. Supports straight I-section members; tapered, curved or nonintersecting axes are rejected.

For creation, use `analyze_only:false`, a loaded detailed `connection_type_id` (or the project joint-kind mapping) and an explicit `dry_run` choice. Revit's native connection service controls plates/bolts. The result always distinguishes the computed node from actual plate-centre verification and connection capacity: these are not automatically certified. This does not implement a full Tekla-equivalent connection design engine.

## Verification status

Calculation/layout unit tests and protocol/build results are recorded in verification.md. Native new-feature runtime status must be checked there separately; a successful build is not proof of live Revit geometry.
