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
using Steam.Models.SteamCommunity;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VkNet;
using VkNet.AudioBypassService.Extensions;
using VkNet.Enums.Filters;
using VkNet.Model;
using VkNet.Model.RequestParams;

namespace SpotifyStatusInVkontakte
{
    public static class Program
    {
        private static readonly IConfigurationRoot s_Configuration;
        private static readonly IStringLocalizer s_StringLocalizer;

        private static string s_VKLogin;
        private static string s_VKPassword;

        private static string s_SpotifyClientId;
        private static string s_SpotifyClientSecret;

        private static ulong s_SteamId;
        private static string s_SteamToken;

        private static readonly VkApi s_VKAPI;
        private static SpotifyWebAPI s_SpotifyAPI;
        private static readonly SteamUser s_SteamAPI;

        private static Token s_SpotifyToken;
        private static readonly AuthorizationCodeAuth s_SpotifyAuth;

        private static bool s_IsQuitting;
        private static bool s_SpotifyConnected;
        private static string s_VKStatus;
        private static string s_VKAudioId;
        private static int s_Delay;

        static Program()
        {
            s_VKStatus = string.Empty;
            s_Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            ReadConfig();

            s_SpotifyAuth = new AuthorizationCodeAuth(
                s_SpotifyClientId,
                s_SpotifyClientSecret,
                "http://localhost:4002",
                "http://localhost:4002",
                Scope.UserReadCurrentlyPlaying | Scope.UserReadPlaybackState
            );
            s_VKAPI = new VkApi(new ServiceCollection().AddAudioBypass());

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(s_Configuration.GetSection("Log").Get<LogEventLevel>())
                .WriteTo.Console()
                .CreateLogger();

            var steamWebInterfaceFactory = new SteamWebInterfaceFactory(s_SteamToken);
            s_SteamAPI = steamWebInterfaceFactory.CreateSteamWebInterface<SteamUser>();

            var loggerFactory = new LoggerFactory().AddSerilog();
            s_StringLocalizer = new JsonStringLocalizer(Path.Combine(Directory.GetCurrentDirectory(), "Resources"),
                "test", loggerFactory.CreateLogger("test"));
        }

        private static void ReadConfig()
        {
            s_VKLogin = s_Configuration["VkLogin"];
            s_VKPassword = s_Configuration["VkPassword"];
            s_SpotifyClientId = s_Configuration["SpotifyClientId"];
            s_SpotifyClientSecret = s_Configuration["SpotifySecretClientId"];
            s_SteamId = s_Configuration.GetSection("SteamId").Get<ulong>();
            s_SteamToken = s_Configuration["SteamToken"];
            s_Delay = s_Configuration.GetSection("Delay").Get<int>();
        }

        private static async Task Main(string[] args)
        {
            if (string.IsNullOrEmpty(s_VKLogin))
            {
                Log.Information("Enter all information into appsettings.json");
                await Task.Delay(3000);
                return;
            }
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            await AuthVK();
            AuthSpotify();
            await StartMainLoop();
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            s_IsQuitting = true;
            Log.CloseAndFlush();
        }

        private static async Task StartMainLoop()
        {
            while (!s_IsQuitting)
            {
                if (!s_SpotifyConnected)
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
                    await Task.Delay((s_Delay > 0 ? s_Delay : s_VKAPI.RequestsPerSecond) * 1000);
                }
            }
        }

        private static async Task MakeCall()
        {
            Log.Information("Making call");
            if (s_SpotifyToken.IsExpired())
            {
                Log.Debug("Spotify token expired... Refreshing");
                await RefreshSpotifyToken();
            }

            var playbackContext = await s_SpotifyAPI.GetPlaybackAsync();
            var summary = await s_SteamAPI.GetPlayerSummaryAsync(s_SteamId);

            if (playbackContext?.HasError() != false)
            {
                Log.Warning("playbackContext is null or has a error");
                if (playbackContext.Error is not null)
                {
                    Log.Warning(playbackContext.Error.ToString());
                }
            }

            // Spotify music is playing?
            var isPlayingSpotify = playbackContext?.IsPlaying == true && playbackContext.Item != null;

            if (isPlayingSpotify)
            {
                var audios = await s_VKAPI.Audio.SearchAsync(new AudioSearchParams
                {
                    SearchOwn = false,
                    PerformerOnly = false,
                    Count = 1,
                    Query = $"{Extensions.GetArtists(playbackContext.Item.Artists)} {playbackContext.Item.Name}"
                });

                if (audios?.Any() == true)
                {
                    var audio = audios[0];
                    var audioId = $"{audio.OwnerId}_{audio.Id}";

                    Log.Debug($"Found music in VK {audio.Title}({audioId})");

                    if (s_VKAudioId == audioId)
                    {
                        Log.Debug("Old VkAudio is equals to new.. Skipping");
                        return;
                    }
                    s_VKAudioId = audioId;
                    await s_VKAPI.Audio.SetBroadcastAsync(audioId);
                    return;
                }

                Log.Debug("Didn't found a music in VK");
                Log.Debug($"Searched: {playbackContext.Item.Name}");
            }
            else if (!string.IsNullOrEmpty(s_VKAudioId))
            {
                Log.Debug("Set not listening");
                s_VKAudioId = string.Empty;
                await s_VKAPI.Audio.SetBroadcastAsync();
            }

            // Steam playing game
            var gameNameSteam = summary.Data?.PlayingGameName;
            // Steam user is online
            var isOnlineSteam = summary.Data?.UserStatus is UserStatus.Online;
            var sb = new StringBuilder();

            if (isPlayingSpotify)
            {
                sb.Append(s_StringLocalizer["Spotify", new[] { Extensions.GetArtists(playbackContext.Item.Artists), playbackContext.Item.Name,
                    Extensions.GetTime(playbackContext.Item.DurationMs, playbackContext.ProgressMs) }]);
            }
            else
            {
                sb.Append(s_StringLocalizer["SpotifyNothing"]);
            }

            sb.Append(s_StringLocalizer["Steam",
                new[] { string.IsNullOrEmpty(gameNameSteam) ? isOnlineSteam ? "в онлайне" : "в оффлайне" : gameNameSteam }]);

            if (!(isPlayingSpotify || !string.IsNullOrEmpty(gameNameSteam)))
            {
                sb.Append(s_StringLocalizer["AFK"]);
            }

            var status = sb.ToString();

            Log.Verbose(status);
            if (s_VKStatus == status)
            {
                Log.Debug("Old status is equals to new. Skip the updating");
                return;
            }

            s_VKStatus = status;
            await s_VKAPI.Status.SetAsync(s_VKStatus);
        }

        private static async Task RefreshSpotifyToken()
        {
            var token = await s_SpotifyAuth.RefreshToken(s_SpotifyToken.RefreshToken);
            if (!string.IsNullOrEmpty(token.Error))
            {
                Log.Error(token.Error);
                Log.Error(token.ErrorDescription);
            }

            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                Log.Debug("Token refresh is null");
                token.RefreshToken = s_SpotifyToken.RefreshToken;
            }

            s_SpotifyAPI.AccessToken = token.AccessToken;
            s_SpotifyAPI.TokenType = token.TokenType;
            s_SpotifyToken.CreateDate = DateTime.Now;
            s_SpotifyToken = token;
        }

        private static async Task AuthVK()
        {
            await s_VKAPI.AuthorizeAsync(new ApiAuthParams
            {
                Settings = Settings.Audio,
                Password = s_VKPassword,
                Login = s_VKLogin,
                TwoFactorSupported = true,
                TokenExpireTime = 0,
                ForceSms = false,
                TwoFactorAuthorization = () =>
                {
                    Log.Information("Enter 2FA code");
                    return Console.ReadLine();
                }
            });
        }

        private static void AuthSpotify()
        {
            s_SpotifyAuth.AuthReceived += async (_, payload) =>
            {
                s_SpotifyAuth.Stop();
                var token = await s_SpotifyAuth.ExchangeCode(payload.Code);
                s_SpotifyAPI = new SpotifyWebAPI()
                {
                    TokenType = token.TokenType,
                    AccessToken = token.AccessToken
                };
                s_SpotifyToken = token;
                s_SpotifyConnected = true;
            };
            s_SpotifyAuth.Start();
            s_SpotifyAuth.OpenBrowser();
        }
    }
}
