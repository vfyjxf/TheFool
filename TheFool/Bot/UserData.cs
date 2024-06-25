using System.Text.Json;
using System.Text.Json.Serialization;
using Lagrange.Core.Common;
using Lagrange.Core.Common.Interface;
using Lagrange.Core.Common.Interface.Api;
using Console = TheFool.Utility.Console;

namespace TheFool.Bot;

public class UserData
{
    public const string KeyStore = "Config/Keystore.json";
    public const string DeviceInfo = "Config/DeviceInfo.json";

    public static void SaveKeystore(BotKeystore keystore)
    {
        File.WriteAllText(KeyStore, JsonSerializer.Serialize(keystore));
    }

    public static BotKeystore? LoadKeystore()
    {
        if (!File.Exists(KeyStore)) return null;
        var text = File.ReadAllText(KeyStore);
        return JsonSerializer.Deserialize<BotKeystore>(text, new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve
        });
    }

    public static BotDeviceInfo GetDeviceInfo()
    {
        if (File.Exists(DeviceInfo))
        {
            var info = JsonSerializer.Deserialize<BotDeviceInfo>(File.ReadAllText(DeviceInfo));
            if (info != null) return info;

            info = BotDeviceInfo.GenerateInfo();
            File.WriteAllText(DeviceInfo, JsonSerializer.Serialize(info));
            return info;
        }

        var deviceInfo = BotDeviceInfo.GenerateInfo();
        File.WriteAllText(DeviceInfo, JsonSerializer.Serialize(deviceInfo));
        return deviceInfo;
    }

    public static async Task FetchQrCode()
    {
        var deviceInfo = GetDeviceInfo();
        var keyStore = LoadKeystore() ?? new BotKeystore();

        var bot = BotFactory.Create(new BotConfig
        {
            UseIPv6Network = false,
            GetOptimumServer = true,
            AutoReconnect = true,
            Protocol = Protocols.Linux
        }, deviceInfo, keyStore);

        bot.Invoker.OnBotLogEvent += (context, @event) =>
        {
            Console.ChangeColorByTitle(@event.Level);
            System.Console.WriteLine(@event.ToString());
        };

        bot.Invoker.OnBotOnlineEvent += (context, @event) =>
        {
            System.Console.WriteLine(@event.ToString());
            SaveKeystore(bot.UpdateKeystore());
        };

        var qrCode = await bot.FetchQrCode();
        if (qrCode != null)
        {
            await File.WriteAllBytesAsync("Config/qr.png", qrCode.Value.QrCode);
            await bot.LoginByQrCode();
        }
    }
}