# Direct transfer after native mixing completes

V12's captured gameplay frame892 has the complete chocolate recipe in bowl6 at native mixing progress10.200017/12 seconds and both original fryer baskets empty. The earlier common heat policy parked this bowl before normal transfer, occupying a storage counter and requiring another pickup.

The V14 source candidate permits a selected common-heat bowl to walk to its original mixer, wait for native `Mixed`, take the whole bowl, pour it into an exact reserved empty fryer basket, and return the original empty bowl. No controller code advances food progress. A wait action emits neutral inputs and observes native completion.

Admission requires the full exact recipe to be in the ordinary order window, an unleased original bowl/mixer, native12-second mixing at progress9 through less than12, and an empty10-second basket on its original fryer. The observed walking speed and full-clearance paths, remaining native mixing time, and a two-second interaction allowance must fit the21-second mixer deadline. The Work owns the original bowl, mixer, basket and fryer throughout; it allocates no plate or parking counter. Far-future bakery leases keep their existing parking and later frying admission rules.

An observer checks exact entity ordinals, recipe address, food and exclusive Work ownership on every completed native frame. It distinguishes observed `Mixed`, whole-bowl pickup, exact recipe transfer, and original empty-bowl restoration. Existing independent mixer21-second and fryer19-second deadlines remain active, including while the Work owns those vessels. A changed identity, wrong recipe, premature pickup, lost ownership or missed deadline terminates the attempt.

`NearReadyDoughSelfTest` uses the actual frame892 geometry and44 focused assertions; subsequent food transitions are explicitly synthetic fixture observations. Together with the unchanged heat-recovery33, pot54, heat25 and general planner71 checks,227 assertions pass in `artifacts/near-ready-tests.json`. These checks make no game calls. The V14 source has not yet demonstrated this admission in a native full planner run.

The analogous pre-Cooked pot wait is deferred. Existing exact Cooked-pot direct harvesting and the source-board storage fallback remain unchanged; extending a carried-bun wait requires a separate identity/lifecycle regression around that fallback.
