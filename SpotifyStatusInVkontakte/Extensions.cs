using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpotifyStatusInVkontakte
{
    public static class Extensions
    {
        public static string GetTime(int? fulltime, int? time)
        {
            if (fulltime == null || time == null)
            {
                return "(0:00/0:00)";
            }

            var t = TimeSpan.FromMilliseconds(time.Value);
            var ft = TimeSpan.FromMilliseconds(fulltime.Value);

            return $"({t:mm\\:ss}/{ft:mm\\:ss})";
        }

        public static string GetArtists(List<SimpleArtist> artists)
        {
            if (artists == null)
            {
                return string.Empty;
            }
            return string.Join(", ", artists.Select(x => x.Name));
        }
    }
}
