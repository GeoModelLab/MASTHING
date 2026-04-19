using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using source.data;

// ---------------------------------------------------------------------------
// MASTHING — weather reader.
// Parses the per-site ERA5-derived CSV weather files (daily Tmax, Tmin, prec,
// radiation) and returns them as a date-indexed dictionary ready to feed the
// simulation loop.
// ---------------------------------------------------------------------------

namespace runner
{
    /// <summary>
    /// Reads site-level daily meteorological drivers (ERA5 reanalysis) from CSV
    /// into <see cref="input"/> objects keyed by date.
    /// </summary>
    public class weatherReader
    {
        /// <summary>
        /// Parses a weather CSV for one site and returns a dictionary mapping each
        /// simulation date to a populated <see cref="input"/> instance.
        /// </summary>
        /// <param name="fileName">Absolute or relative path to the site weather CSV.</param>
        /// <returns>Dictionary of (date → daily input).</returns>
        public Dictionary<DateTime, input> readWeather(string fileName)
        {
            Dictionary<DateTime, input> date_input = new Dictionary<DateTime, input>();
            StreamReader streamReader = new StreamReader(fileName);

            float latitude = 0;
            ///get latitude
            //for (int i = 0; i < 13; i++)
            //{
            //    if(i==3)
            //    {
            //        string[] line = streamReader.ReadLine().Split(' ');
            //        latitude = float.Parse(line[3]);
            //    }
            //    streamReader.ReadLine();
            //}
            streamReader.ReadLine();

            
            while (!streamReader.EndOfStream)
            {
                string[] line = streamReader.ReadLine().Split(',');
                input input = new input();

                if (line[4] != "NA")
                {
                    #region read weather data
                    DateTime date = Convert.ToDateTime(line[2]);
                    if (date.Year == 1978)
                    {
                        for (int i = 1970; i <= 1978; i++)
                        {
                            input = new input();
                            DateTime thisDate = new DateTime(i, date.Month, date.Day);

                            input.date = thisDate;
                            input.precipitation = (float)Convert.ToDouble(line[5]);
                            input.airTemperatureMaximum = (float)Convert.ToDouble(line[4]);
                            if (line[3] != "NA")
                            {
                                input.airTemperatureMinimum = (float)Convert.ToDouble(line[3]);
                            }
                            else
                            {
                                input.airTemperatureMinimum = input.airTemperatureMaximum - 10;
                            }
                            //TODO check
                            input.latitude = (float)Convert.ToDouble(line[0]);
                            
                            date_input.Add(thisDate, input);
                        }
                    }
                    else
                    {
                        input.date = date;
                        input.precipitation = (float)Convert.ToDouble(line[5]);
                        input.airTemperatureMaximum = (float)Convert.ToDouble(line[4]);
                        if (line[3] != "NA")
                        {
                            input.airTemperatureMinimum = (float)Convert.ToDouble(line[3]);
                        }
                        else
                        {
                            input.airTemperatureMinimum = input.airTemperatureMaximum - 10;
                        }
                        //TODO check
                        input.latitude = (float)Convert.ToDouble(line[0]);
                        
                        date_input.Add(date, input);
                    }
                    #endregion
                }
            }
            streamReader.Close();

            date_input = date_input.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);

            return date_input;

        }
    }
}
