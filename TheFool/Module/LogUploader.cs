using System.Text;
using System.Text.Json.Nodes;
using Lagrange.Core;
using Lagrange.Core.Common.Interface.Api;
using Lagrange.Core.Event.EventArg;
using Lagrange.Core.Message;
using Lagrange.Core.Message.Entity;

namespace TheFool.Module;

public class LogUploader
{
    private const string McLogs = "https://api.mclo.gs/1/log";

    private static HttpClient _client = new()
    {
        BaseAddress = new Uri(McLogs),
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static HttpClient _qClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly string[] SupportTypes =
    {
        "txt",
    };

    private static readonly string[] ZipTypes =
    {
        "zip", "7z"
    };

    private static readonly string[] AllTypes = SupportTypes.Concat(ZipTypes).ToArray();

    public static void OnFriendMessageReceived(BotContext bot, FriendMessageEvent eventArgs)
    {
        var rawChain = eventArgs.Chain;
        var last = rawChain.Last();
        if (last is not FileEntity fileEntity) return;
        var fileInfo = new FileInfo(fileEntity.FileName);
        var fileExtension = fileInfo.Extension.Replace(".", "");
        var supportType =
            (from type in AllTypes
                where fileExtension.Equals(type)
                select type).FirstOrDefault();
        if (supportType == null)
        {
            Console.WriteLine($"Unsupported file type: {fileInfo.Extension}");
            return;
        }

        var isZip = ZipTypes.Contains(supportType);
        if (!isZip)
        {
            if (fileEntity.FileSize > 10485760) //10mib
            {
                Console.WriteLine("File is larger than 10MiB!");
                return;
            }

            var fileUrl = fileEntity.FileUrl;
            if (fileUrl == null)
            {
                Console.WriteLine("Cannot get file url!");
                return;
            }

            var content = _qClient.GetStringAsync(fileUrl).Result;
            UploadFile(content).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    var text = result == null ? "上传失败" : $"上传成功：{result}";
                    var nextChain = MessageBuilder.Friend(rawChain.FriendUin).Forward(rawChain).Text(text).Build();
                    bot.SendMessage(nextChain);
                }
                else
                {
                    Console.WriteLine(task.Exception);
                }
            });
        }
        else
        {
            //TODO:impl
        }
    }

    private static async Task<string?> UploadFile(string contents)
    {
        var content = new StringContent($"content={Uri.EscapeDataString(contents)}", Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var response = await _client.PostAsync(McLogs, content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var jsonObj = JsonNode.Parse(json)?.AsObject();
        if (jsonObj != null) return jsonObj["url"]?.GetValue<string>() ?? null;
        Console.WriteLine($"Cannot parse response json: {json}");
        return null;
    }
}