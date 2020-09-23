using LocalizationCultureCore.StringLocalizer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VkNet;
using VkNet.AudioBypassService.Extensions;
using VkNet.Enums.Filters;
using VkNet.Model;

namespace SpotifyStatusInVkontakte
{
    public static class Program
    {
        private static readonly IConfigurationRoot _Configuration;
        private static readonly IStringLocalizer _StringLocalizer;

        private static string _VKToken;
        private static string _VKLogin;
        private static string _VKPassword;

        private static string _SpotifyClientId;
        private static string _SpotifyClientSecret;

        private static ulong _SteamId;
        private static string _SteamToken;

        private static VkApi _VKAPI;
        private static SpotifyWebAPI _SpotifyAPI;
        private static readonly SteamUser _SteamAPI;

        private static Token _SpotifyToken;
        private static readonly AuthorizationCodeAuth _SpotifyAuth;

        private static bool _IsQuitting;
        private static bool _SpotifyConnected;
        private static string _VKStatus;
        private static int _Delay;

        static Program()
        {
            _VKStatus = string.Empty;
            _Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            Configurate();
            _SpotifyAuth = new AuthorizationCodeAuth(
                _SpotifyClientId,
                _SpotifyClientSecret,
                "http://localhost:4002",
                "http://localhost:4002",
                Scope.UserReadCurrentlyPlaying | Scope.UserReadPlaybackState
            );
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(_Configuration.GetSection("Log").Get<LogEventLevel>())
                .WriteTo.Console()
                .CreateLogger();
            var steamWebInterfaceFactory = new SteamWebInterfaceFactory(_SteamToken);
            _SteamAPI = steamWebInterfaceFactory.CreateSteamWebInterface<SteamUser>();
            var loggerFactory = new LoggerFactory().AddSerilog();
            _StringLocalizer = new JsonStringLocalizer(Path.Combine(Directory.GetCurrentDirectory(), "Resources"),
                "test", loggerFactory.CreateLogger("test"));
        }

        private static void Configurate()
        {
            _VKToken = _Configuration["VkToken"];
            _VKLogin = _Configuration["VkLogin"];
            _VKPassword = _Configuration["VkPassword"];
            _SpotifyClientId = _Configuration["SpotifyClientId"];
            _SpotifyClientSecret = _Configuration["SpotifySecretClientId"];
            _SteamId = _Configuration.GetSection("SteamId").Get<ulong>();
            _SteamToken = _Configuration["SteamToken"];
            _Delay = _Configuration.GetSection("Delay").Get<int>();
        }

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            await AuthVK();
            AuthSpotify();
            await Start();
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            _IsQuitting = true;
            Log.CloseAndFlush();
        }

        private static async Task Start()
        {
            while (!_IsQuitting)
            {
                if (!_SpotifyConnected)
                {
                    continue;
                }
                try
                {
                    await MakeCall();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Making call throw a error");
                }
                finally
                {
                    await Task.Delay((_Delay != 0 ? _Delay : _VKAPI.RequestsPerSecond) * 1000);
                }
            }
        }

        private static async Task MakeCall()
        {
            Log.Information("Making call");
            if (_SpotifyToken.IsExpired())
            {
                Log.Debug("Spotify token expired.. Creating new");
                _SpotifyToken = await _SpotifyAuth.RefreshToken(_SpotifyToken.RefreshToken);
                _SpotifyAPI.AccessToken = _SpotifyToken.AccessToken;
                _SpotifyAPI.TokenType = _SpotifyToken.TokenType;
            }
            var playbackContext = await _SpotifyAPI.GetPlaybackAsync();
            var summary = await _SteamAPI.GetPlayerSummaryAsync(_SteamId);

            var b = playbackContext != null && (playbackContext?.IsPlaying ?? false) && playbackContext.Item != null;
            var b2 = summary.Data?.PlayingGameName;
            var b3 = summary.Data?.UserStatus == Steam.Models.SteamCommunity.UserStatus.Online;
            var status = string.Concat(
                b ? _StringLocalizer["Spotify", new object[] { GetArtists(playbackContext.Item.Artists), playbackContext.Item.Name, GetTime(playbackContext.Item.DurationMs, playbackContext.ProgressMs) }]
                    : _StringLocalizer["SpotifyNothing"],
                _StringLocalizer["Steam", new object[] { string.IsNullOrEmpty(b2) ? b3 ? "в онлайне" : "в оффлайне" : b2 }],
                !(b || !string.IsNullOrEmpty(b2)) ? _StringLocalizer["AFK"] : string.Empty);

            Log.Verbose(status);
            if (_VKStatus == status)
            {
                Log.Debug("Old status is equals to new. Skip the updating");
                return;
            }
            _VKStatus = status;
            await _VKAPI.Status.SetAsync(_VKStatus);
        }

        private static string GetArtists(List<SimpleArtist> artists)
        {
            if (artists == null)
            {
                return string.Empty;
            }
            return string.Join(", ", artists.Select(x => x.Name));
        }

        private static string GetTime(int? fulltime, int? time)
        {
            if (fulltime == null || time == null)
            {
                return "(0:00/0:00)";
            }
            var t = TimeSpan.FromMilliseconds(time.Value);
            var ft = TimeSpan.FromMilliseconds(fulltime.Value);

            return $" ({t.Minutes}:{(t.Seconds.ToString().Length == 1 ? "0" : "") + t.Seconds}/{ft.Minutes}:{(ft.Seconds.ToString().Length == 1 ? "0" : "") + ft.Seconds})";
        }

        private static async Task AuthVK()
        {
            _VKAPI = new VkApi(new ServiceCollection().AddAudioBypass());
            await _VKAPI.AuthorizeAsync(new ApiAuthParams
            {
                ApplicationId = 7186653,
                Settings = Settings.All,
                AccessToken = _VKToken,
                Login = _VKLogin,
                Password = _VKPassword,
                TwoFactorSupported = false
            });
        }

        private static void AuthSpotify()
        {
            _SpotifyAuth.AuthReceived += async (_, payload) =>
            {
                _SpotifyAuth.Stop();
                var token = await _SpotifyAuth.ExchangeCode(payload.Code);
                _SpotifyAPI = new SpotifyWebAPI()
                {
                    TokenType = token.TokenType,
                    AccessToken = token.AccessToken
                };
                _SpotifyToken = token;
                _SpotifyConnected = true;
            };
            _SpotifyAuth.Start();
            _SpotifyAuth.OpenBrowser();
        }
    }
}
