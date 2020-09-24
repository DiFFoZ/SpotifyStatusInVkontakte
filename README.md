# Spotify Status In Vkontakte
![Image](https://github.com/DiFFoZ/SpotifyStatusInVkontakte/blob/master/images/example.png)

Данное приложение выводит в статус ВК, что слушайте в Spotify и играете в Steam.

# Как использовать
Запустите приложение, подождите когда напишет `Enter all information into appsettings.json`. После этого закройте приложение и откройте файл `appsettings.json`. Введите информацию..

```
{
  "VkToken": "YOUR_VK_TOKEN",

  "SpotifyClientId": "YOUR_SPOTIFY_CLIENTID",
  "SpotifySecretClientId": "YOUR_SPOTIFY_SECRET_CLIENTID",

  "SteamId": 160,
  "SteamToken": "YOUR_STEAM_TOKEN",

  "Delay": 30,
  "Log": "Information"
}
```
- `VkToken` можно получить по [этой ссылке](https://oauth.vk.com/authorize?client_id=7186653&redirect_uri=https://oauth.vk.com/blank.html&display=page&scope=1024&response_type=token&v=5.21&state=1234567&revoke=1)

- `SpotifyClientId` и `SpotifySecretCliendId` можно получить [здесь](https://developer.spotify.com/dashboard/applications). Нужно зарегистрировать приложение **И указать в `Redirect URIs` сайт `http://localhost:4002`**.

- `SteamToken` можно получить [тут](https://steamcommunity.com/dev/apikey). Домен указываете любой.
