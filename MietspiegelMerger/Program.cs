using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

namespace MietspiegelMerger
{

    public class Program
    {

        private static Dictionary<String, IEnumerable<String>>  Adressen;
        private static Dictionary<String, List<JObject>>        OSMData;

        private static Dictionary<String, IEnumerable<String>> GetAdressen()
        {

            var Adressen = new Dictionary<String, IEnumerable<String>>();
            var Splitter = new String[] { "=>", "," };

            foreach (var Strasse in File.ReadLines("Mietspiegel_StrassenamenUndHausnummern.log"))
            {

                var Elements = Strasse.Split(Splitter, StringSplitOptions.RemoveEmptyEntries);

                Adressen.Add(Elements[0].Trim(), Elements.Skip(1).Select(_ => _.Trim()));

            }

            return Adressen;

        }

        private static Dictionary<String, List<JObject>> GetOSMData()
        {

            var OSMInput = JObject.Parse(File.ReadAllText("Mietspiegel_OSMBuildings.json"));
            var OSMData  = new Dictionary<String, List<JObject>>();

            foreach (var Item in OSMInput["elements"])
            {

                var tags = Item["tags"] as JObject;
                if (tags == null)
                    continue;

                var street = tags["addr:street"];
                if (street == null)
                    continue;

                var housenumber = tags["addr:housenumber"];
                if (housenumber == null)
                    continue;

                var key = street.Value<String>() + " " + housenumber.Value<String>();

                if (!OSMData.ContainsKey(key))
                    OSMData.Add(key, new List<JObject>() { Item as JObject });

                else
                    OSMData[key].Add(Item as JObject);

            }

            return OSMData;

        }


        public static void Main(String[] Arguments)
        {


            var OSM23Input = JObject.Parse(File.ReadAllText("Mietspiegel_Datensätze.json"));


            OSMData = GetOSMData();

            using (var OSMGoodput = File.CreateText("Mietspiegel_StrassenamenUndHausnummern_OSM.json"))
            {
                using (var Badput = File.CreateText("Mietspiegel_Missing_StrassenamenUndHausnummern.json"))
                {
                    using (var Goodput = File.CreateText("Mietspiegel_Vorhandene_StrassenamenUndHausnummern.json"))
                    {

                        OSMGoodput.WriteLine("{");

                        foreach (var Strasse in GetAdressen())
                        {

                            foreach (var Hausnummer in Strasse.Value)
                            {

                                List<JObject> OSMBuildings = null;

                                if (OSMData.TryGetValue(Strasse.Key + " " + Hausnummer, out OSMBuildings) ||
                                    OSMData.TryGetValue(Strasse.Key.Replace("traße", "trasse") + " " + Hausnummer, out OSMBuildings) ||
                                    OSMData.TryGetValue(Strasse.Key.Replace("trasse", "traße") + " " + Hausnummer, out OSMBuildings))
                                {
                                    OSMGoodput.WriteLine(OSMBuildings.AggregateWith("," + Environment.NewLine) + ",");
                                    Goodput.WriteLine(Strasse.Key + " => " + Hausnummer);
                                }

                                else
                                {
                                    Console.WriteLine(Strasse.Key + " => " + Hausnummer);
                                    Badput.WriteLine(Strasse.Key + " => " + Hausnummer);
                                }

                            }

                        }

                        OSMGoodput.WriteLine("}");

                    }
                }
            }

        }

    }

}
