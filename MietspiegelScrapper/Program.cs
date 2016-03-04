
#region Usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using org.GraphDefined.Vanaheimr.Illias;
using System.IO;

#endregion

namespace de.OffenesJena.Mietspiegel.Scrapper
{

    public static class Ext
    {

        public static String Scrape(this String HTML, String PatternStart, String PatternEnd, ref Int32 pos)
        {

            var PosStart  = HTML.IndexOf(PatternStart, pos);
            var PosEnd    = HTML.IndexOf(PatternEnd, PosStart);

            pos = PosStart + PatternEnd.Length;

            return HTML.Substring(PosStart + PatternStart.Length,
                                  PosEnd - PosStart - PatternStart.Length);

        }

    }

    public enum SizeType
    {
        under50         = 1,
        between50and80  = 2,
        over80          = 3
    }

    public enum YearType
    {
        till1949            = 1,
        between1950and1962  = 2,
        between1963and1990  = 3,
        between1991and2001  = 4,
        since2002           = 5
    }


    /// <summary>
    /// Scrape the Mietspiegel...
    /// </summary>
    public class Program
    {

        #region Data

        private static DNSClient    DNSClient;
        private static IPv4Address  HTTPAddress;
        private static IPPort       HTTPPort;

        private static readonly Char[] SplitMe = new Char[] { ' ' };

        #endregion


        #region GetStreetnames(IncludeFilter = null)

        /// <summary>
        /// Get all streetnames from https://mietspiegel.jena.de
        /// </summary>
        private static async Task<IEnumerable<String>>

            GetStreetnames(Func<String, Boolean> IncludeFilter = null)

        {

            var response = await new HTTPClient(HTTPAddress,
                                                HTTPPort,
                                                (sender, certificate, chain, sslPolicyErrors) => true,
                                                DNSClient).

                                     Execute(client => client.GET("/",
                                                                  requestbuilder => {
                                                                      requestbuilder.Host = "mietspiegel.jena.de";
                                                                  }),

                                             TimeSpan.FromSeconds(30),
                                             new CancellationTokenSource().Token);

            var HTML          = response.HTTPBody.ToUTF8String();

            var PatternStart  = @"<script>$(function() {var availableTags = [";
            var PatternEnd    = @"]; $( ""#strassenliste"" ).autocomplete({";

            var PosStart      = HTML.IndexOf(PatternStart);
            var PosEnd        = HTML.IndexOf(PatternEnd);

            return HTML.Substring(PosStart + PatternStart.Length,
                                  PosEnd - PosStart - PatternStart.Length).
                        Split(',').
                        Where (street => street.Length > 2).
                        Select(street => street.Substring(1, street.Length - 2)).
                        Where (street => IncludeFilter != null ? IncludeFilter(street) : true).
                        ToArray();

        }

        #endregion

        #region GetHousenumbers(Streetname)

        /// <summary>
        /// Get all housenumbers for a given streetname from https://mietspiegel.jena.de
        /// </summary>
        private static async Task<Tuple<String, IEnumerable<String>>>

            GetHousenumbers(String Streetname)

        {

            #region Initial checks

            if (Streetname.IsNullOrEmpty())
                throw new ArgumentNullException(nameof(Streetname), "The given streetname must not be null or empty!");

            #endregion

            var response = await new HTTPClient(HTTPAddress,
                                                HTTPPort,
                                                (sender, certificate, chain, sslPolicyErrors) => true,
                                                DNSClient).

                                     Execute(client => client.POST("/lib/ajax.php?strassenauswahl_%22.md5(lcg_value()).%22",
                                                                   requestbuilder => {
                                                                       requestbuilder.Host         = "mietspiegel.jena.de";
                                                                       requestbuilder.ContentType  = HTTPContentType.XWWWFormUrlEncoded;
                                                                       requestbuilder.Content      = ("name=" + Streetname + "&data=33").ToUTF8Bytes();
                                                                   }),

                                             TimeSpan.FromSeconds(30),
                                             new CancellationTokenSource().Token);

            var HTML          = response.HTTPBody.ToUTF8String();

            var PatternStart  = @"&lt;select class=""form-control"" name=""nummer""&gt;&lt;option&gt;";
            var PatternEnd    = @"&lt;/option&gt;&lt;/select&gt;&lt;/div&gt;";

            var PosStart      = HTML.IndexOf(PatternStart);
            var PosEnd        = HTML.IndexOf(PatternEnd);

            var Housenumbers  = (PosEnd - PosStart - PatternStart.Length < 0)
                                    ? new String[] { "..." }
                                    : HTML.Substring(PosStart + PatternStart.Length,
                                                     PosEnd - PosStart - PatternStart.Length).
                                           Replace("&lt;/option&gt;&lt;option&gt;", ",").
                                           Split(',').
                                           ToArray();

            return new Tuple<String, IEnumerable<String>>(Streetname, Housenumbers);

        }

        #endregion

        #region GetStreetsAndNumbers(IncludeFilter = null)

        /// <summary>
        /// Get all streets and their housenumbers from https://mietspiegel.jena.de
        /// </summary>
        private static async Task<IEnumerable<Tuple<String, IEnumerable<String>>>>

            GetStreetsAndNumbers(Func<String, Boolean> IncludeFilter = null)

        {

            var Streetnames            = await GetStreetnames(IncludeFilter);

            var StreetsAndNumberTasks  = Streetnames.
                                             Select(streetname => GetHousenumbers(streetname)).
                                             ToList(); // Runs faster with ToList()?!

            return StreetsAndNumberTasks.Select(reply => reply.Result);

        }

        #endregion


        #region GetResults(Streetname, Housenumber, Size, Year)

        /// <summary>
        /// Get all housenumbers for a given streetname from https://mietspiegel.jena.de
        /// </summary>
        private static async Task<Tuple<String, IEnumerable<String>>>

            GetResults(String    Streetname,
                       String    Housenumber,
                       SizeType  Size,
                       YearType  Year)

        {

            #region Initial checks

            if (Streetname.IsNullOrEmpty())
                throw new ArgumentNullException(nameof(Streetname), "The given streetname must not be null or empty!");

            #endregion

            var response = await new HTTPClient(HTTPAddress,
                                                HTTPPort,
                                                (sender, certificate, chain, sslPolicyErrors) => true,
                                                DNSClient).

                                     Execute(client => client.POST("/",
                                                                   requestbuilder => {
                                                                       requestbuilder.Host         = "mietspiegel.jena.de";
                                                                       requestbuilder.ContentType  = HTTPContentType.XWWWFormUrlEncoded;
                                                                       requestbuilder.Content      = ("submit=1&strasse=" + Streetname +
                                                                                                      "&nummer=" + Housenumber +
                                                                                                      "&form_3=" + (Int32) Size +
                                                                                                      "&form_4=" + (Int32) Year +
                                                                                                      "&form_5=0&form_6=0&form_7=0&form_8=0&form_9=0&form_13=0&form_14=0&form_23=0").ToUTF8Bytes();
                                                                   }),

                                             TimeSpan.FromSeconds(30),
                                             new CancellationTokenSource().Token);


            String Cookie;

            // Set-Cookie: PHPSESSID=b4f1c5ea36b12e92f81f152ecbab6c00; path=/
            if (response.TryGet("Set-Cookie", out Cookie))
            {

                var response2 = await new HTTPClient(HTTPAddress,
                                                     HTTPPort,
                                                     (sender, certificate, chain, sslPolicyErrors) => true,
                                                     DNSClient).

                                          Execute(client => client.GET("/result.php",
                                                                        requestbuilder => {
                                                                            requestbuilder.Host = "mietspiegel.jena.de";
                                                                            requestbuilder.Cookie = Cookie.Replace("; path=/", "");
                                                                        }),

                                                  TimeSpan.FromSeconds(30),
                                                  new CancellationTokenSource().Token);

                var HTML = response2.HTTPBody.ToUTF8String();

                Int32 pos = 0;

                var Wohnlage         = HTML.Scrape("<tr><td><b>Wohnlage</b></td><td>",
                                                   "</td></tr>", ref pos);

                var Wohnwertpunkte   = HTML.Scrape("<tr><td><b>Wohnwertpunkte</b></td><td>",
                                                   "</td></tr>", ref pos);

                var Vergleichsmiete  = HTML.Scrape(@"<tr><td><b>ortsübliche Vergleichsmiete</b></td><td style=""white-space: nowrap;"">&nbsp;",
                                                   "  €/m²</tr>", ref pos).
                                            Replace("€/m² -", " ").
                                            Split(SplitMe, StringSplitOptions.RemoveEmptyEntries);


                return new Tuple<String, IEnumerable<String>>("", new String[] { "" });

            }

            return new Tuple<String, IEnumerable<String>>("", new String[] { "" });

        }

        #endregion

        public static void Main(String[] Arguments)
        {

            DNSClient    = new DNSClient(SearchForIPv6DNSServers: false);
            HTTPAddress  = DNSClient.Query<A>("mietspiegel.jena.de").Result.FirstOrDefault().IPv4Address; // 195.37.112.180
            HTTPPort     = IPPort.Parse(443);

            var aa = GetResults("Biberweg", "18", SizeType.between50and80, YearType.between1991and2001).Result;



            String line = null;

            using (var logfile = File.AppendText("Mietspiegel_StrassenamenUndHausnummern.log"))
            {
                foreach (var item in GetStreetsAndNumbers().Result)
                {

                    line = (item.Item1.EndsWith("tr.") ? item.Item1.Replace("tr.", "trasse") : item.Item1) + " => " + item.Item2.AggregateWith(",");

                    Console.WriteLine(line);
                    logfile.WriteLine(line);

                }
            }

        }

    }

}
