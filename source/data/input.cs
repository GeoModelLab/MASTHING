using System;

// ---------------------------------------------------------------------------
// MASTHING — data namespace
// Contains the data-transfer classes used to pass daily inputs to the model.
// See Bregaglio et al. (2026) for a full description of the model.
// ---------------------------------------------------------------------------

namespace source.data
{
    /// <summary>
    /// Daily model input. One instance is created per day per simulated tree and
    /// is passed as argument to every computing method (phenology, carbon balance,
    /// reproduction). It bundles meteorological drivers, the current date and a
    /// reference to the tree-level structural attributes.
    /// </summary>
    public class input
    {
        /// <summary>Remotely sensed vegetation index identifier (e.g. "NDVI").</summary>
        public string vegetationIndex {  get; set; }

        /// <summary>Daily maximum air temperature (°C).</summary>
        public float airTemperatureMaximum { get; set; }

        /// <summary>Daily minimum air temperature (°C).</summary>
        public float airTemperatureMinimum { get; set; }

        /// <summary>Daily precipitation (mm).</summary>
        public float precipitation { get; set; }

        /// <summary>Calendar date of the current simulation step.</summary>
        public DateTime date { get; set; }

        /// <summary>Site latitude in decimal degrees (used for radiation calculations).</summary>
        public float latitude { get; set; }

        /// <summary>Radiation-related variables (extraterrestrial and global solar radiation, day length).</summary>
        public radData radData = new radData();

        /// <summary>Tree-level structural attributes and reference seed data.</summary>
        public tree tree = new tree();
    }

    /// <summary>
    /// Individual tree structural state and reference seed observations.
    /// Structural variables are derived from DBH through allometric relationships
    /// (see Supplementary Information S1.1 in the MASTHING paper).
    /// </summary>
    public class tree
    {
        /// <summary>Unique tree identifier (matches the observational dataset).</summary>
        public string id { get; set; }

        /// <summary>Diameter at breast height (DBH, 1.3 m) in cm. Primary allometric input.</summary>
        public float diameter130 { get; set; }

        /// <summary>Diameter at the base of the crown (cm).</summary>
        public float diameterBaseCrown { get; set; }

        /// <summary>Total tree height (m), derived from DBH.</summary>
        public float treeHeight { get; set; }

        /// <summary>Height of the crown base (m).</summary>
        public float baseCrownHeight { get; set; }

        /// <summary>Total aboveground biomass (kg DM), derived from DBH.</summary>
        public float totalBiomass { get; set; }

        /// <summary>Stem biomass (kg DM), derived from DBH.</summary>
        public float stemBiomass { get; set; }

        /// <summary>Foliage biomass (kg DM), derived from DBH and height.</summary>
        public float foliageBiomass { get; set; }

        /// <summary>Branch biomass (kg DM), derived from DBH.</summary>
        public float branchesBiomass { get; set; }

        /// <summary>Crown projection area (m²), derived from DBH.</summary>
        public float crownProjectionArea { get; set; }

        /// <summary>Biomass of a single branch (kg DM), used when branch-level detail is needed.</summary>
        public float branchSingleBiomass { get; set; }

        /// <summary>Representative branch diameter (cm).</summary>
        public float branchDiameter { get; set; }

        /// <summary>Total leaf area of the tree (m²).</summary>
        public float leafArea { get; set; }

        /// <summary>Maximum leaf area index (m² leaf / m² crown projection). See eq. SE1.8.</summary>
        public float LAImax { get; set; }

        /// <summary>Specific leaf area (m² kg⁻¹). Species-specific parameter.</summary>
        public float SLA { get; set; }

        /// <summary>
        /// Reference seed production data keyed by year. Used for calibration and evaluation
        /// against the UK beech masting survey (1980–2025).
        /// </summary>
        public Dictionary<int, float> YearSeeds = new Dictionary<int, float>();
    }

    /// <summary>
    /// Radiation-related daily variables required by the carbon balance and phenology modules.
    /// </summary>
    public class radData
    {
        /// <summary>Extraterrestrial solar radiation (MJ m⁻² d⁻¹). Computed from latitude and day of year.</summary>
        public float etr { get; set; }

        /// <summary>Day length (hours) for the current day.</summary>
        public float dayLength { get; set; }

        /// <summary>Day length (hours) for the previous day — used for photoperiod-driven phenology cues.</summary>
        public float dayLengthYesterday { get; set; }

        /// <summary>Solar time of sunrise (decimal hour).</summary>
        public float hourSunrise { get; set; }

        /// <summary>Solar time of sunset (decimal hour).</summary>
        public float hourSunset { get; set; }

        /// <summary>Global solar radiation (MJ m⁻² d⁻¹). Drives GPP via the light-use-efficiency formulation.</summary>
        public float gsr { get; set; }
    }
}
