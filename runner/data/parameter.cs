namespace runner.data
{
    /// <summary>
    /// Metadata record describing a single MASTHING / SWELL model parameter as
    /// read from the species-specific CSV parameter files.
    /// For each parameter the class stores the functional group it belongs to,
    /// the admissible search interval [minimum, maximum] used by the multi-start
    /// Simplex optimizer, the default (nominal) value, and a flag that indicates
    /// whether the parameter is included in the calibration subset.
    /// </summary>
    public class parameter
    {
        /// <summary>
        /// Name of the parameter class (functional group) to which the
        /// parameter belongs (e.g. parGrowth, parEndodormancy, parResources,
        /// parReproduction). Used by the parameter reader to dispatch values
        /// to the correct sub-container in the <c>parameters</c> object.
        /// </summary>
        public string classParam { get; set; }

        /// <summary>
        /// Lower bound of the search interval used by the Simplex optimizer
        /// when the parameter is part of the calibration subset.
        /// </summary>
        public float minimum { get; set; }

        /// <summary>
        /// Upper bound of the search interval used by the Simplex optimizer
        /// when the parameter is part of the calibration subset.
        /// </summary>
        public float maximum { get; set; }

        /// <summary>
        /// Default (nominal) value of the parameter. Used when the parameter
        /// is excluded from calibration (i.e. <see cref="calibration"/> is
        /// empty).
        /// </summary>
        public float value { get; set; }

        /// <summary>
        /// Calibration flag. A non-empty value (conventionally "x") marks the
        /// parameter as part of the calibration subset; an empty string keeps
        /// it fixed at <see cref="value"/> during optimization.
        /// </summary>
        public string calibration { get; set; }
    }
}
