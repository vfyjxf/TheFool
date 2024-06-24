using Lagrange.Core;
using Lagrange.Core.Common;
using Lagrange.Core.Common.Interface;
using Lagrange.Core.Common.Interface.Api;
using TheFool.Bot;
using TheFool.Module;

namespace TheFool;

internal static class Program
{
    public static async Task Main(string[] _)
    {
        await Login();
    }

    private static async Task Login()
    {
        if (File.Exists(UserData.KeyStore))
        {
            BotContext bot;
            var config = new BotConfig
            {
                Protocol = Protocols.Linux,
                AutoReconnect = true
            };
            bot = BotFactory.Create(config, UserData.GetDeviceInfo(), UserData.LoadKeystore()!);
            bot.Invoker.OnBotLogEvent += (context, eventArgs) =>
            {
                Console.WriteLine(eventArgs.ToString());
                Utility.Console.ChangeColorByTitle(eventArgs.Level);
            };
            bot.Invoker.OnBotOnlineEvent += (context, eventArgs) => { Console.WriteLine(context.BotName); };
            bot.Invoker.OnFriendMessageReceived += LogUploader.OnFriendMessageReceived;
            await bot.LoginByPassword();
        }
        else
        {
            await UserData.FetchQrCode();
        }
    }
}