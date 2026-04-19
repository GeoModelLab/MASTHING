using System.Collections.Generic;
using runner.data;
using System.IO;

namespace runner
{
    /// <summary>
    /// Reader for the species-specific MASTHING / SWELL parameter CSV files
    /// (<c>files/parametersData/SWELLparameters.csv</c> and
    /// <c>files/parametersData/MASTHINGparameters.csv</c>).
    /// Each CSV row describes a single model parameter with its functional
    /// group, name, min/max search interval, nominal value and calibration
    /// flag; the reader organises these rows into a two-level dictionary
    /// keyed by species and by "<c>class_name</c>" (e.g. "parGrowth_minimumTemperature").
    /// </summary>
    public class paramReader
    {
        /// <summary>
        /// Parses a parameter CSV file for a given species.
        /// </summary>
        /// <param name="file">
        /// Absolute or relative path of the parameter CSV. The file is expected
        /// to have a single header row and comma-separated columns in the
        /// order: species, class, name, minimum, maximum, value, calibration.
        /// </param>
        /// <param name="species">
        /// Species tag (e.g. "beech") used as the top-level key of the
        /// returned dictionary. All parameters in the file are assumed to
        /// refer to this species.
        /// </param>
        /// <returns>
        /// A nested dictionary <c>species → (class_name → parameter)</c>
        /// suitable for consumption by the <c>optimizer</c> class. The
        /// second-level key is built as <c>class + "_" + name</c> and
        /// uniquely identifies a parameter within the species.
        /// </returns>
        public Dictionary<string, Dictionary<string, parameter>> read(string file, string species)
        {
            Dictionary<string, Dictionary<string, parameter>> species_nameParam = new Dictionary<string, Dictionary<string, parameter>>();

            if (!species_nameParam.ContainsKey(species))
            {
                species_nameParam.Add(species, new Dictionary<string, parameter>());
            }

            StreamReader sr = new StreamReader(file);
            sr.ReadLine(); // skip header

            while (!sr.EndOfStream)
            {
                string[] line = sr.ReadLine().Split(',');

                // Insert placeholder and then replace with fully-populated record.
                species_nameParam[species].Add(line[1] + "_" + line[2], new parameter());
                parameter parameter = new parameter();
                parameter.value = float.Parse(line[5]);
                parameter.minimum = float.Parse(line[3]);
                parameter.maximum = float.Parse(line[4]);
                parameter.calibration = line[6];
                parameter.classParam = line[1];
                species_nameParam[species][line[1] + "_" + line[2]] = parameter;
            }
            sr.Close();
            return species_nameParam;
        }
    }
}
